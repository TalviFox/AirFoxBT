using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AirFoxBT.Models;

namespace AirFoxBT.Services;

/// <summary>
/// Zero-dependency mDNS / DNS-SD broadcaster for ESPHome auto-discovery (_esphomelib._tcp.local).
/// Broadcasts service records on UDP 224.0.0.251:5353 from an ephemeral outbound port.
/// Avoids port 5353 exclusive bind collisions on Windows so the app never crashes on boot.
/// </summary>
public class MdnsService : IDisposable
{
    private static readonly IPAddress MdnsMulticastAddress = IPAddress.Parse("224.0.0.251");
    private const int MdnsPort = 5353;
    private const int ServicePort = 6053;

    private readonly UdpClient _udpClient;
    private readonly CancellationTokenSource _cts = new();
    private bool _isRunning;

    public string SelectedIp { get; private set; } = "Auto";
    public string DeviceName { get; set; } = string.Empty;

    public event Action<string>? OnLog;

    public MdnsService()
    {
        // Bind to port 0 (ephemeral outbound port) to prevent WSAEADDRINUSE collisions with Windows Dnscache
        _udpClient = new UdpClient(0);
        try
        {
            _udpClient.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
        }
        catch { }
    }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;

        var (slug, friendly) = DeviceNaming.ResolveNames(DeviceName);
        OnLog?.Invoke($"[mDNS] 📡 Announcing '{friendly}' ({slug}) on port {ServicePort} for Home Assistant discovery...");

        // Send initial announcement
        SendAnnouncement();

        // Loop periodic announcement every 5 minutes
        _ = AnnounceLoopAsync(_cts.Token);
    }

    public void RestartWithIp(string targetIp, string? deviceName = null)
    {
        SelectedIp = targetIp;
        if (deviceName != null)
        {
            DeviceName = deviceName;
        }
        var (slug, friendly) = DeviceNaming.ResolveNames(DeviceName);
        OnLog?.Invoke($"[mDNS] 📡 Announcing '{friendly}' ({slug}) on {targetIp}:{ServicePort}...");
        SendAnnouncement();
    }

    public void SendAnnouncement()
    {
        try
        {
            var targetEp = new IPEndPoint(MdnsMulticastAddress, MdnsPort);
            var (slug, friendly) = DeviceNaming.ResolveNames(DeviceName);

            if (SelectedIp != "Auto" && IPAddress.TryParse(SelectedIp, out var specificIp) && !specificIp.Equals(IPAddress.Any))
            {
                // Send targeted announcement explicitly from the chosen network interface
                SendFromInterface(specificIp, targetEp, slug, friendly);
            }
            else
            {
                // Multi-homed Auto mode: Send announcement from EVERY active bindable interface so all VLANs discover it
                var bindable = GetBindableIpv4Addresses();
                if (bindable.Count == 0)
                {
                    var fallbackIp = GetPrimaryIpv4Address();
                    var packet = BuildMdnsResponsePacket(fallbackIp, slug, friendly);
                    _udpClient.Send(packet, packet.Length, targetEp);
                }
                else
                {
                    foreach (var item in bindable)
                    {
                        SendFromInterface(item.Ip, targetEp, slug, friendly);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"[mDNS] Send notice: {ex.Message}");
        }
    }

    private void SendFromInterface(IPAddress localIp, IPEndPoint targetEp, string slug, string friendly)
    {
        try
        {
            using var client = new UdpClient(new IPEndPoint(localIp, 0));
            client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
            try
            {
                client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, localIp.GetAddressBytes());
            }
            catch { }

            var packet = BuildMdnsResponsePacket(localIp, slug, friendly);
            client.Send(packet, packet.Length, targetEp);
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"[mDNS] Notice on {localIp}: {ex.Message}");
        }
    }

    private async Task AnnounceLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _isRunning)
        {
            try
            {
                // Announce every 5 minutes (standard RFC 6762 unsolicited refresh)
                await Task.Delay(300000, ct);
                SendAnnouncement();
            }
            catch when (ct.IsCancellationRequested)
            {
                break;
            }
            catch { }
        }
    }

    private static byte[] BuildMdnsResponsePacket(IPAddress localIp, string slug, string friendlyName)
    {
        var macStr = GetPrimaryMacAddress().Replace(":", "").ToLowerInvariant();

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // Header: Transaction ID (0), Flags (0x8400 = Authoritative Response), QDCOUNT=0, ANCOUNT=1, NSCOUNT=0, ARCOUNT=3
        WriteU16(bw, 0);
        WriteU16(bw, 0x8400);
        WriteU16(bw, 0); // QDCOUNT
        WriteU16(bw, 1); // ANCOUNT (1 Answer: PTR record)
        WriteU16(bw, 0); // NSCOUNT
        WriteU16(bw, 3); // ARCOUNT (3 Additional records: SRV, TXT, A)

        // 1. Answer 1: PTR Record (_esphomelib._tcp.local)
        WriteDnsName(bw, "_esphomelib._tcp.local");
        WriteU16(bw, 12); // Type PTR
        WriteU16(bw, 0x0001); // Class IN (MUST NOT set cache-flush bit 0x8000 on shared PTR records per RFC 6762)
        WriteI32(bw, 4500); // TTL: 4500 seconds

        using (var ptrData = new MemoryStream())
        {
            using var ptrBw = new BinaryWriter(ptrData);
            WriteDnsName(ptrBw, $"{slug}._esphomelib._tcp.local");
            var bytes = ptrData.ToArray();
            WriteU16(bw, (ushort)bytes.Length);
            bw.Write(bytes);
        }

        // --- ADDITIONAL RECORDS (ARCOUNT = 3) ---

        // 2. Additional 1: SRV Record ({slug}._esphomelib._tcp.local -> port 6053 -> {slug}.local)
        WriteDnsName(bw, $"{slug}._esphomelib._tcp.local");
        WriteU16(bw, 33); // Type SRV
        WriteU16(bw, 0x8001); // Class IN, flush
        WriteI32(bw, 4500); // TTL

        using (var srvData = new MemoryStream())
        {
            using var srvBw = new BinaryWriter(srvData);
            WriteU16(srvBw, 0); // Priority
            WriteU16(srvBw, 0); // Weight
            WriteU16(srvBw, ServicePort); // Port 6053
            WriteDnsName(srvBw, $"{slug}.local");
            var bytes = srvData.ToArray();
            WriteU16(bw, (ushort)bytes.Length);
            bw.Write(bytes);
        }

        // 3. Additional 2: TXT Record
        WriteDnsName(bw, $"{slug}._esphomelib._tcp.local");
        WriteU16(bw, 16); // Type TXT
        WriteU16(bw, 0x8001); // Class IN, flush
        WriteI32(bw, 4500); // TTL

        using (var txtData = new MemoryStream())
        {
            WriteTxtRecord(txtData, $"mac={macStr}");
            WriteTxtRecord(txtData, $"friendly_name={friendlyName}");
            WriteTxtRecord(txtData, "project_name=foxden.airfoxbt");
            WriteTxtRecord(txtData, "project_version=1.0.0");
            WriteTxtRecord(txtData, "version=2024.12.0");
            WriteTxtRecord(txtData, $"address={localIp}");
            WriteTxtRecord(txtData, "board=airfoxbt");
            WriteTxtRecord(txtData, "platform=ESP32");
            WriteTxtRecord(txtData, "network=ethernet");

            var bytes = txtData.ToArray();
            WriteU16(bw, (ushort)bytes.Length);
            bw.Write(bytes);
        }

        // 4. Additional 3: A Record ({slug}.local -> Local IPv4)
        WriteDnsName(bw, $"{slug}.local");
        WriteU16(bw, 1); // Type A
        WriteU16(bw, 0x8001); // Class IN, flush
        WriteI32(bw, 4500); // TTL
        WriteU16(bw, 4); // Data length: 4 bytes for IPv4
        bw.Write(localIp.GetAddressBytes());

        return ms.ToArray();
    }

    private static void WriteDnsName(BinaryWriter bw, string name)
    {
        var parts = name.Split('.');
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;
            var bytes = Encoding.ASCII.GetBytes(part);
            bw.Write((byte)bytes.Length);
            bw.Write(bytes);
        }
        bw.Write((byte)0); // Terminating zero byte
    }

    private static void WriteTxtRecord(Stream s, string entry)
    {
        var bytes = Encoding.ASCII.GetBytes(entry);
        s.WriteByte((byte)bytes.Length);
        s.Write(bytes, 0, bytes.Length);
    }

    private static void WriteU16(BinaryWriter bw, ushort val)
    {
        var net = (ushort)IPAddress.HostToNetworkOrder((short)val);
        bw.Write(net);
    }

    private static void WriteI32(BinaryWriter bw, int val)
    {
        var net = IPAddress.HostToNetworkOrder(val);
        bw.Write(net);
    }

    public static List<(IPAddress Ip, string Name, string Description)> GetBindableIpv4Addresses()
    {
        var list = new List<(IPAddress Ip, string Name, string Description)>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    continue;

                foreach (var ip in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ip.Address) &&
                        !ip.Address.ToString().StartsWith("169.254"))
                    {
                        list.Add((ip.Address, nic.Name, nic.Description));
                    }
                }
            }
        }
        catch { }
        return list;
    }

    public static IPAddress GetPrimaryIpv4Address()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus == OperationalStatus.Up &&
                    nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                {
                    foreach (var ip in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(ip.Address) &&
                            !ip.Address.ToString().StartsWith("169.254"))
                        {
                            return ip.Address;
                        }
                    }
                }
            }
        }
        catch { }
        return IPAddress.Loopback;
    }

    private static string GetPrimaryMacAddress()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus == OperationalStatus.Up &&
                    nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var bytes = nic.GetPhysicalAddress().GetAddressBytes();
                    if (bytes.Length == 6)
                    {
                        return $"{bytes[0]:X2}:{bytes[1]:X2}:{bytes[2]:X2}:{bytes[3]:X2}:{bytes[4]:X2}:{bytes[5]:X2}";
                    }
                }
            }
        }
        catch { }
        return "AA:00:11:22:33:44";
    }

    public void Dispose()
    {
        _isRunning = false;
        _cts.Cancel();
        _udpClient.Dispose();
    }
}
