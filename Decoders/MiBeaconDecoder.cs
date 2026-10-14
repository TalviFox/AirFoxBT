using System;
using System.Collections.Generic;
using AirFoxBT.Models;

namespace AirFoxBT.Decoders;

public record MiBeaconProductInfo(string Model, string DisplayType, string FriendlyName);

public static class MiBeaconDecoder
{
    public const ushort ServiceUuid = 0xFE95;

    public static readonly Dictionary<ushort, MiBeaconProductInfo> KnownProducts = new()
    {
        [0x055B] = new("LYWSD03MMC", "LCD", "Xiaomi Mijia LCD (LYWSD03)"),
        [0x0387] = new("MHO-C401", "E-Ink", "Miaomiaoce E-Ink (C401)"),
        [0x01AA] = new("LYWSD02MMC", "E-Ink", "Xiaomi E-Ink Clock (LYWSD02)"),
        [0x045C] = new("CGG1", "E-Ink", "Qingping E-Ink (CGG1)"),
        [0x066F] = new("CGDK2", "E-Ink", "Qingping Temp Lite (CGDK2)"),
        [0x0A83] = new("XMWSDJ04MMC", "E-Ink", "Xiaomi E-Ink (XMWSDJ04)"),
        [0x0C3C] = new("MJWSD05MMC", "LCD", "Xiaomi Mijia 3 (MJWSD05)"),
        [0x0863] = new("MHO-C122", "E-Ink", "Miaomiaoce Mini E-Ink"),
        [0x0098] = new("HHCCJCY01", "Screenless", "Mi Flora Plant Monitor"),
        [0x015D] = new("HHCCPOT002", "Screenless", "RoPot Smart Flowerpot"),
        [0x040A] = new("WX08ZM", "Screenless", "Mijia Mosquito Repellent"),
        [0x07F6] = new("MCCGQ02HL", "Door/Window", "Mijia Door/Window 2"),
        [0x0A8D] = new("RTCGQ02LM", "Motion", "Mijia Motion 2"),
    };

    public static bool TryDecode(byte[] payload, string mac, string deviceName, short rssi, out BleReading reading)
    {
        reading = new BleReading
        {
            MacAddress = mac,
            DeviceName = string.IsNullOrWhiteSpace(deviceName) ? "Xiaomi Sensor" : deviceName,
            Protocol = "Xiaomi MiBeacon",
            Rssi = rssi,
            LastSeenUtc = DateTime.UtcNow
        };

        // Minimum MiBeacon header is 5 bytes: Frame Control (2B) + Product ID (2B) + Frame Counter (1B)
        if (payload.Length < 5) return false;

        ushort frameControl = (ushort)(payload[0] | (payload[1] << 8));
        ushort productId = (ushort)(payload[2] | (payload[3] << 8));

        bool isEncrypted = (frameControl & (1 << 3)) != 0;
        bool hasMac = (frameControl & (1 << 4)) != 0;
        bool hasCapability = (frameControl & (1 << 5)) != 0;
        bool hasObject = (frameControl & (1 << 6)) != 0;

        // Auto-detect hardware model and display type from Product ID
        if (KnownProducts.TryGetValue(productId, out var prodInfo))
        {
            reading.HardwareModel = prodInfo.Model;
            reading.DisplayType = prodInfo.DisplayType;
            if (string.IsNullOrWhiteSpace(deviceName) || deviceName == "Xiaomi Sensor" || deviceName == "Unknown")
            {
                reading.DeviceName = prodInfo.FriendlyName;
            }
        }
        else
        {
            reading.HardwareModel = $"PID 0x{productId:X4}";
        }

        int index = 5;
        if (hasMac)
        {
            if (payload.Length < index + 6) return false;
            // MAC in MiBeacon is in reverse order
            byte[] macBytes = new byte[6];
            Array.Copy(payload, index, macBytes, 0, 6);
            reading.MacAddress = $"{macBytes[5]:X2}:{macBytes[4]:X2}:{macBytes[3]:X2}:{macBytes[2]:X2}:{macBytes[1]:X2}:{macBytes[0]:X2}";
            index += 6;
        }

        if (hasCapability)
        {
            if (index < payload.Length)
            {
                byte cap = payload[index++];
                if ((cap & (1 << 5)) != 0 && index < payload.Length) // IO capability
                {
                    index += 1;
                }
            }
        }

        if (isEncrypted)
        {
            reading.IsEncrypted = true;
            reading.Protocol = "Xiaomi MiBeacon (Encrypted)";
            reading.DeviceCategory = "Encrypted";
            return true;
        }

        if (!hasObject || index >= payload.Length)
        {
            // Even without event metrics, successfully return model reading so hardware model is registered
            return true;
        }

        // Parse TLV Event Objects
        while (index + 3 <= payload.Length)
        {
            ushort eventId = (ushort)(payload[index] | (payload[index + 1] << 8));
            byte eventLen = payload[index + 2];
            index += 3;

            if (index + eventLen > payload.Length) break;

            switch (eventId)
            {
                case 0x1004: // Temperature (2 bytes, sint16 LE, 0.1°C)
                    if (eventLen >= 2)
                    {
                        short rawTemp = (short)(payload[index] | (payload[index + 1] << 8));
                        reading.TemperatureC = Math.Round(rawTemp * 0.1, 1);
                        reading.DeviceCategory = "Environmental";
                    }
                    break;

                case 0x1006: // Humidity (2 bytes, uint16 LE, 0.1%)
                    if (eventLen >= 2)
                    {
                        ushort rawHum = (ushort)(payload[index] | (payload[index + 1] << 8));
                        reading.HumidityPercent = Math.Round(rawHum * 0.1, 1);
                        reading.DeviceCategory = "Environmental";
                    }
                    break;

                case 0x100A: // Battery (1 byte, uint8, %)
                    if (eventLen >= 1)
                    {
                        reading.BatteryPercent = payload[index];
                    }
                    break;

                case 0x100D: // Combined Temperature & Humidity (4 bytes: 2B temp + 2B hum)
                    if (eventLen >= 4)
                    {
                        short rawTemp = (short)(payload[index] | (payload[index + 1] << 8));
                        ushort rawHum = (ushort)(payload[index + 2] | (payload[index + 3] << 8));
                        reading.TemperatureC = Math.Round(rawTemp * 0.1, 1);
                        reading.HumidityPercent = Math.Round(rawHum * 0.1, 1);
                        reading.DeviceCategory = "Environmental";
                    }
                    break;

                case 0x1007: // Illuminance (3 bytes, uint24 LE, lux)
                    if (eventLen >= 3)
                    {
                        uint rawLux = (uint)(payload[index] | (payload[index + 1] << 8) | (payload[index + 2] << 16));
                        reading.IlluminanceLux = rawLux;
                    }
                    break;
            }

            index += eventLen;
        }

        return true;
    }
}
