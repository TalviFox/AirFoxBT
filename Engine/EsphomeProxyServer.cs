using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AirFoxBT.Services;

namespace AirFoxBT.Engine;

/// <summary>
/// Native ESPHome Bluetooth Proxy protocol implementation (TCP port 6053).
/// Allows Home Assistant to connect directly to AirFoxBT as a native Bluetooth Proxy
/// with zero tokens, zero YAML, and zero hardware emulation.
/// </summary>
public class EsphomeProxyServer : IDisposable
{
    public const int DefaultPort = 6053;
    public int Port { get; }

    private readonly TcpListener _listener;
    private readonly ConcurrentDictionary<Guid, ClientSession> _clients = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _isRunning;

    private ThrottleSettings _throttle = ThrottleSettings.FromPreset(ThrottlePreset.Balanced);
    private readonly ConcurrentDictionary<ulong, (uint Hash, long TimestampMs)> _deviceHistory = new();
    private long _totalIngestedAdv = 0;
    private long _totalForwardedAdv = 0;
    private long _totalDroppedAdv = 0;
    private readonly ConcurrentDictionary<string, bool> _ignoredMacs = new(StringComparer.OrdinalIgnoreCase);

    public void SetMacIgnored(string formattedMac, bool isIgnored)
    {
        if (string.IsNullOrWhiteSpace(formattedMac)) return;
        if (isIgnored)
        {
            _ignoredMacs[formattedMac] = true;
        }
        else
        {
            _ignoredMacs.TryRemove(formattedMac, out _);
        }
    }

    public bool IsMacIgnored(string formattedMac) => !string.IsNullOrWhiteSpace(formattedMac) && _ignoredMacs.ContainsKey(formattedMac);

    public event Action<string>? OnLog;
    public event Action<int>? OnClientCountChanged;

    public int ConnectedClients => _clients.Count;
    public long TotalIngestedAdv => Interlocked.Read(ref _totalIngestedAdv);
    public long TotalForwardedAdv => Interlocked.Read(ref _totalForwardedAdv);
    public long TotalDroppedAdv => Interlocked.Read(ref _totalDroppedAdv);

    public string NodeName { get; set; } = "airfoxbt";
    public string FriendlyName { get; set; } = "AirFoxBT Proxy";

    public ThrottleSettings Throttle
    {
        get => _throttle;
        set
        {
            _throttle = value;
            _deviceHistory.Clear();
            OnLog?.Invoke($"[ESPHome] Throttling mode set to {_throttle.Preset} (Cooldown: {_throttle.Cooldown.TotalSeconds}s, Deduplicate: {_throttle.DeduplicatePayload})");
        }
    }

    private static uint FastFnv1a(byte[] data)
    {
        uint hash = 2166136261;
        for (int i = 0; i < data.Length; i++)
        {
            hash = (hash ^ data[i]) * 16777619;
        }
        return hash;
    }

    public EsphomeProxyServer(int port = DefaultPort)
    {
        Port = port;
        _listener = new TcpListener(IPAddress.Any, Port);
    }

    public void Start()
    {
        if (_isRunning) return;

        EnsureFirewallRule();

        try
        {
            _listener.Start();
            _isRunning = true;
            OnLog?.Invoke($"[ESPHome] 🌐 Bluetooth Proxy listening on 0.0.0.0:{Port}");
            _ = AcceptClientsAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"[ESPHome] ❌ Failed to bind port {Port}: {ex.Message}");
        }
    }

    private void EnsureFirewallRule()
    {
        try
        {
            if (FirewallService.IsRuleConfigured(Port))
            {
                OnLog?.Invoke($"[Firewall] Inbound firewall rule active for port {Port}");
                return;
            }

            if (FirewallService.AddRuleDirect(Port, 8766))
            {
                OnLog?.Invoke($"[Firewall] Successfully added inbound firewall rule for port {Port}");
            }
            else
            {
                OnLog?.Invoke($"[Firewall] Inbound TCP port {Port} not allowed in Windows Firewall (Non-admin account). Please configure firewall in Settings or run admin script.");
            }
        }
        catch { }
    }

    public void BroadcastAdvertisement(ulong mac, short rssi, uint addressType, byte[] data)
    {
        if (_clients.IsEmpty) return;

        // Block ignored / non-relayed sensors from ever reaching Home Assistant
        string formattedMac = BleWatcherEngine.FormatMacAddress(mac);
        if (_ignoredMacs.ContainsKey(formattedMac)) return;

        Interlocked.Increment(ref _totalIngestedAdv);

        // Apply throttle / deduplication rules if not in Raw firehose mode
        if (_throttle.Preset != ThrottlePreset.Raw)
        {
            // Content-aware payload deduplication & cooldown (zero RSSI floor: hears full radio sensitivity)
            long nowMs = Environment.TickCount64;
            uint hash = _throttle.DeduplicatePayload ? FastFnv1a(data) : 0;

            if (_deviceHistory.TryGetValue(mac, out var lastState))
            {
                bool payloadChanged = !_throttle.DeduplicatePayload || (lastState.Hash != hash);
                long elapsedMs = nowMs - lastState.TimestampMs;

                // Drop if payload is identical AND within cooldown window
                if (!payloadChanged && elapsedMs < _throttle.Cooldown.TotalMilliseconds)
                {
                    Interlocked.Increment(ref _totalDroppedAdv);
                    return;
                }
            }

            _deviceHistory[mac] = (hash, nowMs);
        }

        Interlocked.Increment(ref _totalForwardedAdv);

        // Submessage: BluetoothLERawAdvertisement
        // 1: mac (uint64)
        // 2: rssi (sint32 zigzag)
        // 3: address_type (uint32)
        // 4: data (bytes)
        var advWriter = new ProtoWriter();
        advWriter.WriteFieldVarInt(1, mac);
        advWriter.WriteFieldZigZag32(2, rssi);
        advWriter.WriteFieldVarInt(3, addressType);
        advWriter.WriteFieldBytes(4, data);

        // Message: BluetoothLERawAdvertisementsResponse (Type 93)
        // 1: repeated BluetoothLERawAdvertisement
        var msgWriter = new ProtoWriter();
        msgWriter.WriteFieldSubMessage(1, advWriter);

        var frame = FrameMessage(93, msgWriter.ToArray());

        foreach (var kvp in _clients)
        {
            var session = kvp.Value;
            if (session.IsSubscribedToBle)
            {
                session.Send(frame);
            }
        }
    }

    private async Task AcceptClientsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _isRunning)
        {
            try
            {
                var tcpClient = await _listener.AcceptTcpClientAsync(ct);
                var sessionId = Guid.NewGuid();
                var endpoint = tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";

                var session = new ClientSession(sessionId, tcpClient, OnLog, () =>
                {
                    _clients.TryRemove(sessionId, out _);
                    OnClientCountChanged?.Invoke(_clients.Count);
                    OnLog?.Invoke($"[ESPHome] 🔌 Home Assistant disconnected ({endpoint})");
                }, this);

                _clients[sessionId] = session;
                OnClientCountChanged?.Invoke(_clients.Count);
                OnLog?.Invoke($"[ESPHome] ⚡ Home Assistant client connected from {endpoint}!");

                _ = session.RunAsync(ct);
            }
            catch when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[ESPHome] Error accepting client: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Frame format per ESPHome specification:
    /// - Preamble: 0x00 (1 byte)
    /// - Length: VarInt of payload length ONLY (type is not part of this!)
    /// - Type: VarInt of message type
    /// - Payload: raw Protobuf message bytes
    /// </summary>
    public static byte[] FrameMessage(uint messageType, byte[] payload)
    {
        var lenVarInt = ProtoWriter.EncodeVarInt((uint)payload.Length);
        var typeVarInt = ProtoWriter.EncodeVarInt(messageType);

        var ms = new MemoryStream(1 + lenVarInt.Length + typeVarInt.Length + payload.Length);
        ms.WriteByte(0x00); // Preamble
        ms.Write(lenVarInt, 0, lenVarInt.Length);
        ms.Write(typeVarInt, 0, typeVarInt.Length);
        if (payload.Length > 0)
        {
            ms.Write(payload, 0, payload.Length);
        }
        return ms.ToArray();
    }

    public void Dispose()
    {
        _isRunning = false;
        _cts.Cancel();
        try { _listener.Stop(); } catch { }

        foreach (var kvp in _clients)
        {
            kvp.Value.Dispose();
        }
        _clients.Clear();
    }

    private class ClientSession : IDisposable
    {
        public Guid Id { get; }
        public bool IsSubscribedToBle { get; set; }

        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly Action<string>? _log;
        private readonly Action _onDisconnected;
        private readonly EsphomeProxyServer _server;
        private bool _isDisposed;

        public ClientSession(Guid id, TcpClient client, Action<string>? log, Action onDisconnected, EsphomeProxyServer server)
        {
            Id = id;
            _client = client;
            _stream = client.GetStream();
            _log = log;
            _onDisconnected = onDisconnected;
            _server = server;
        }

        public void Send(byte[] frame)
        {
            if (_isDisposed) return;
            try
            {
                _stream.Write(frame, 0, frame.Length);
            }
            catch
            {
                Dispose();
            }
        }

        public async Task RunAsync(CancellationToken ct)
        {
            var headerBuf = new byte[1];

            try
            {
                while (!ct.IsCancellationRequested && _client.Connected)
                {
                    // 1. Read preamble (must be 0x00)
                    int read = await _stream.ReadAsync(headerBuf.AsMemory(0, 1), ct);
                    if (read == 0) break;
                    if (headerBuf[0] != 0x00) continue;

                    // 2. Read length varint (size of message payload object ONLY)
                    uint length = await ReadVarIntAsync(_stream, ct);
                    if (length > 65536) continue;

                    // 3. Read type varint
                    uint type = await ReadVarIntAsync(_stream, ct);

                    // 4. Read payload (exactly length bytes)
                    byte[] payload = length > 0 ? new byte[length] : Array.Empty<byte>();
                    if (length > 0)
                    {
                        int received = 0;
                        while (received < length)
                        {
                            int chunk = await _stream.ReadAsync(payload.AsMemory(received, (int)length - received), ct);
                            if (chunk == 0) break;
                            received += chunk;
                        }
                    }

                    HandleMessage(type, payload);
                }
            }
            catch { }
            finally
            {
                Dispose();
            }
        }

        private void HandleMessage(uint type, byte[] payload)
        {
            switch (type)
            {
                case 1: // HelloRequest
                {
                    var res = new ProtoWriter();
                    res.WriteFieldVarInt(1, 1);  // api_version_major = 1
                    res.WriteFieldVarInt(2, 10); // api_version_minor = 10
                    res.WriteFieldString(3, "2024.12.0");
                    res.WriteFieldString(4, _server.NodeName); // Node name (must match mDNS hostname!)
                    Send(FrameMessage(2, res.ToArray())); // HelloResponse
                    break;
                }

                case 3: // AuthenticationRequest (deprecated in HA 2024+, but answered for compatibility)
                {
                    var res = new ProtoWriter();
                    res.WriteFieldVarInt(1, 0); // invalid_password = false
                    Send(FrameMessage(4, res.ToArray())); // AuthenticationResponse
                    break;
                }

                case 5: // DisconnectRequest
                {
                    Send(FrameMessage(6, Array.Empty<byte>())); // DisconnectResponse
                    Dispose();
                    break;
                }

                case 7: // PingRequest
                {
                    Send(FrameMessage(8, Array.Empty<byte>())); // PingResponse
                    break;
                }

                case 9: // DeviceInfoRequest
                {
                    var res = new ProtoWriter();
                    res.WriteFieldVarInt(1, 0); // uses_password = false
                    res.WriteFieldString(2, _server.NodeName);
                    res.WriteFieldString(3, GetLocalMacAddress());
                    res.WriteFieldString(4, "2024.12.0");
                    res.WriteFieldString(5, "Oct 06 2026");
                    res.WriteFieldString(6, _server.FriendlyName);
                    res.WriteFieldString(8, "foxden.airfoxbt"); // Custom project branding -> "airfoxbt by foxden"
                    res.WriteFieldString(9, "1.0.0");
                    res.WriteFieldVarInt(10, 8766); // webserver_port = 8766 (adds "Visit Device" button to Home Assistant)
                    res.WriteFieldString(12, "FoxDen");
                    res.WriteFieldString(13, "AirFoxBT");
                    res.WriteFieldVarInt(15, 97); // bluetooth_proxy_feature_flags: 97 (PASSIVE_SCAN | RAW_ADVERTISEMENTS | FEATURE_STATE_AND_MODE)
                    res.WriteFieldString(18, GetLocalMacAddress());
                    Send(FrameMessage(10, res.ToArray())); // DeviceInfoResponse
                    break;
                }

                case 11: // ListEntitiesRequest
                {
                    Send(FrameMessage(19, Array.Empty<byte>())); // ListEntitiesDoneResponse
                    break;
                }

                case 20: // SubscribeStatesRequest
                {
                    // No entities to send state for
                    break;
                }

                case 66: // SubscribeBluetoothLEAdvertisementsRequest
                {
                    IsSubscribedToBle = true;
                    _log?.Invoke("[ESPHome] 🎯 Home Assistant subscribed to Bluetooth LE advertisement stream!");
                    SendScannerState(2, 1); // state=RUNNING (2), mode=ACTIVE (1)
                    break;
                }

                case 80: // SubscribeBluetoothConnectionsFreeRequest
                {
                    var res = new ProtoWriter();
                    res.WriteFieldVarInt(1, 0); // free = 0
                    res.WriteFieldVarInt(2, 0); // limit = 0
                    Send(FrameMessage(81, res.ToArray())); // BluetoothConnectionsFreeResponse
                    break;
                }

                case 87: // UnsubscribeBluetoothLEAdvertisementsRequest
                {
                    IsSubscribedToBle = false;
                    SendScannerState(5, 0); // state=STOPPED (5)
                    break;
                }

                case 127: // BluetoothScannerSetModeRequest
                {
                    uint requestedMode = 0;
                    if (payload.Length >= 2 && (payload[0] >> 3) == 1)
                    {
                        requestedMode = payload[1];
                    }
                    SendScannerState(2, requestedMode);
                    break;
                }
            }
        }

        private void SendScannerState(uint state, uint mode)
        {
            var res = new ProtoWriter();
            res.WriteFieldVarInt(1, state); // 2 = RUNNING
            res.WriteFieldVarInt(2, mode);  // 1 = ACTIVE
            res.WriteFieldVarInt(3, mode);  // configured_mode
            Send(FrameMessage(126, res.ToArray())); // BluetoothScannerStateResponse (Type 126)
        }

        private static string GetLocalMacAddress()
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

        private static async Task<uint> ReadVarIntAsync(Stream stream, CancellationToken ct)
        {
            uint result = 0;
            int shift = 0;
            var b = new byte[1];

            while (true)
            {
                int r = await stream.ReadAsync(b.AsMemory(0, 1), ct);
                if (r == 0) return 0;

                byte val = b[0];
                result |= (uint)(val & 0x7F) << shift;
                if ((val & 0x80) == 0) break;
                shift += 7;
            }

            return result;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            try { _stream.Dispose(); } catch { }
            try { _client.Dispose(); } catch { }
            _onDisconnected();
        }
    }
}

public class ProtoWriter
{
    private readonly MemoryStream _ms = new();

    public void WriteFieldVarInt(int fieldNumber, ulong val)
    {
        WriteTag(fieldNumber, 0); // WireType 0 = VarInt
        WriteVarInt(val);
    }

    public void WriteFieldZigZag32(int fieldNumber, int val)
    {
        uint encoded = (uint)((val << 1) ^ (val >> 31));
        WriteFieldVarInt(fieldNumber, encoded);
    }

    public void WriteFieldBytes(int fieldNumber, byte[] bytes)
    {
        WriteTag(fieldNumber, 2); // WireType 2 = Length-delimited
        WriteVarInt((uint)bytes.Length);
        _ms.Write(bytes, 0, bytes.Length);
    }

    public void WriteFieldString(int fieldNumber, string str)
    {
        var bytes = Encoding.UTF8.GetBytes(str);
        WriteFieldBytes(fieldNumber, bytes);
    }

    public void WriteFieldSubMessage(int fieldNumber, ProtoWriter subMessage)
    {
        WriteFieldBytes(fieldNumber, subMessage.ToArray());
    }

    private void WriteTag(int fieldNumber, int wireType)
    {
        uint tag = (uint)((fieldNumber << 3) | wireType);
        WriteVarInt(tag);
    }

    private void WriteVarInt(ulong val)
    {
        while (val > 0x7F)
        {
            _ms.WriteByte((byte)((val & 0x7F) | 0x80));
            val >>= 7;
        }
        _ms.WriteByte((byte)(val & 0x7F));
    }

    public static byte[] EncodeVarInt(uint val)
    {
        using var ms = new MemoryStream();
        while (val > 0x7F)
        {
            ms.WriteByte((byte)((val & 0x7F) | 0x80));
            val >>= 7;
        }
        ms.WriteByte((byte)(val & 0x7F));
        return ms.ToArray();
    }

    public byte[] ToArray() => _ms.ToArray();
}
