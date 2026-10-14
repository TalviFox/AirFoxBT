using System;
using System.IO;
using System.Linq;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;
using AirFoxBT.Decoders;
using AirFoxBT.Models;

namespace AirFoxBT.Engine;

public class BleWatcherEngine
{
    private BluetoothLEAdvertisementWatcher? _watcher;
    public event Action<BleReading>? OnReadingReceived;
    public event Action<ulong, short, uint, byte[]>? OnRawAdvertisementReceived;
    public event Action<string>? OnStatusChanged;
    public event Action<Exception>? OnError;

    public bool IsRunning { get; private set; }

    public void Start()
    {
        if (IsRunning) return;

        try
        {
            _watcher = new BluetoothLEAdvertisementWatcher
            {
                // Passive mode listens purely without transmitting SCAN_REQ frames, eliminating radio TX contention
                ScanningMode = BluetoothLEScanningMode.Passive
            };

            _watcher.Received += HandleAdvertisementReceived;
            _watcher.Stopped += (s, e) =>
            {
                IsRunning = false;
                OnStatusChanged?.Invoke($"Watcher stopped: {e.Error}");
            };

            _watcher.Start();
            IsRunning = true;
            OnStatusChanged?.Invoke("Bluetooth LE watcher active. Listening for BTHome / ATC beacons...");
        }
        catch (Exception ex)
        {
            IsRunning = false;
            OnError?.Invoke(ex);
        }
    }

    public void Stop()
    {
        if (!IsRunning || _watcher == null) return;

        try
        {
            _watcher.Stop();
            IsRunning = false;
            OnStatusChanged?.Invoke("Bluetooth LE watcher stopped.");
        }
        catch (Exception ex)
        {
            OnError?.Invoke(ex);
        }
    }

    private void HandleAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        string mac = FormatMacAddress(args.BluetoothAddress);
        string localName = args.Advertisement.LocalName ?? string.Empty;
        short rssi = args.RawSignalStrengthInDBm;

        // Reconstruct raw BLE advertisement payload for ESPHome Bluetooth Proxy (Home Assistant)
        using var rawMs = new MemoryStream();

        // Do NOT inject synthetic flags (0x02, 0x01, 0x06).
        // For Scan Responses, flags violate Bluetooth Core Spec and cause HA's parser to silently drop packets.


        foreach (var section in args.Advertisement.DataSections)
        {
            var sData = ReadBuffer(section.Data);
            rawMs.WriteByte((byte)(sData.Length + 1));
            rawMs.WriteByte(section.DataType);
            rawMs.Write(sData, 0, sData.Length);
        }

        // Include ManufacturerData if not already in DataSections (Windows BLE API sometimes separates them)
        if (!args.Advertisement.DataSections.Any(s => s.DataType == 0xFF))
        {
            foreach (var mfg in args.Advertisement.ManufacturerData)
            {
                var mfgData = ReadBuffer(mfg.Data);
                rawMs.WriteByte((byte)(mfgData.Length + 3));
                rawMs.WriteByte(0xFF);
                rawMs.WriteByte((byte)(mfg.CompanyId & 0xFF));
                rawMs.WriteByte((byte)((mfg.CompanyId >> 8) & 0xFF));
                rawMs.Write(mfgData, 0, mfgData.Length);
            }
        }

        // Include LocalName if present and not in DataSections
        if (!string.IsNullOrEmpty(localName) && !args.Advertisement.DataSections.Any(s => s.DataType == 0x08 || s.DataType == 0x09))
        {
            var nameBytes = System.Text.Encoding.UTF8.GetBytes(localName);
            rawMs.WriteByte((byte)(nameBytes.Length + 1));
            rawMs.WriteByte(0x09);
            rawMs.Write(nameBytes, 0, nameBytes.Length);
        }

        var rawBytes = rawMs.ToArray();
        OnRawAdvertisementReceived?.Invoke(args.BluetoothAddress, rssi, (uint)args.BluetoothAddressType, rawBytes);

        // 1. Service Data UUIDs (DataType 0x16)
        foreach (var section in args.Advertisement.DataSections)
        {
            byte[] data = ReadBuffer(section.Data);

            if (section.DataType == 0x16 && data.Length >= 2)
            {
                ushort uuid = (ushort)(data[0] | (data[1] << 8));
                byte[] payload = data.Skip(2).ToArray();

                if (uuid == BTHomeDecoder.ServiceUuid)
                {
                    if (BTHomeDecoder.TryDecode(payload, mac, localName, rssi, out var reading))
                    {
                        OnReadingReceived?.Invoke(reading);
                        return;
                    }
                }
                else if (uuid == AtcDecoder.ServiceUuid)
                {
                    if (AtcDecoder.TryDecode(payload, mac, localName, rssi, out var reading))
                    {
                        OnReadingReceived?.Invoke(reading);
                        return;
                    }
                }
                else if (uuid == MiBeaconDecoder.ServiceUuid)
                {
                    if (MiBeaconDecoder.TryDecode(payload, mac, localName, rssi, out var reading))
                    {
                        OnReadingReceived?.Invoke(reading);
                        return;
                    }
                }
            }
        }

        // 2. Manufacturer Data (some sensors broadcast BTHome or ATC under CompanyId 0xFCD2 or 0x181A)
        foreach (var mfg in args.Advertisement.ManufacturerData)
        {
            if (mfg.CompanyId == BTHomeDecoder.ServiceUuid)
            {
                byte[] data = ReadBuffer(mfg.Data);
                if (BTHomeDecoder.TryDecode(data, mac, localName, rssi, out var reading))
                {
                    OnReadingReceived?.Invoke(reading);
                    return;
                }
            }
            else if (mfg.CompanyId == AtcDecoder.ServiceUuid)
            {
                byte[] data = ReadBuffer(mfg.Data);
                if (AtcDecoder.TryDecode(data, mac, localName, rssi, out var reading))
                {
                    OnReadingReceived?.Invoke(reading);
                    return;
                }
            }
        }
    }

    public static string FormatMacAddress(ulong address)
    {
        byte[] bytes = BitConverter.GetBytes(address);
        return $"{bytes[5]:X2}:{bytes[4]:X2}:{bytes[3]:X2}:{bytes[2]:X2}:{bytes[1]:X2}:{bytes[0]:X2}";
    }

    private static byte[] ReadBuffer(IBuffer buffer)
    {
        using var reader = DataReader.FromBuffer(buffer);
        byte[] bytes = new byte[buffer.Length];
        reader.ReadBytes(bytes);
        return bytes;
    }
}
