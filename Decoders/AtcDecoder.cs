using System;
using AirFoxBT.Models;

namespace AirFoxBT.Decoders;

public static class AtcDecoder
{
    public const ushort ServiceUuid = 0x181A;

    public static bool TryDecode(byte[] payload, string mac, string deviceName, short rssi, out BleReading reading)
    {
        reading = new BleReading
        {
            MacAddress = mac,
            DeviceName = string.IsNullOrWhiteSpace(deviceName) ? "ATC/PVVX Sensor" : deviceName,
            Protocol = "ATC/PVVX",
            Rssi = rssi,
            LastSeenUtc = DateTime.UtcNow
        };

        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            var upper = deviceName.ToUpperInvariant();
            if (upper.Contains("C401") || upper.Contains("MHO-C401"))
            {
                reading.HardwareModel = "MHO-C401";
                reading.DisplayType = "E-Ink";
                reading.DeviceName = "Miaomiaoce E-Ink (C401)";
            }
            else if (upper.Contains("LYWSD02"))
            {
                reading.HardwareModel = "LYWSD02MMC";
                reading.DisplayType = "E-Ink";
                reading.DeviceName = "Xiaomi E-Ink Clock";
            }
            else if (upper.Contains("LYWSD03"))
            {
                reading.HardwareModel = "LYWSD03MMC";
                reading.DisplayType = "LCD";
                reading.DeviceName = "Xiaomi Mijia LCD";
            }
            else if (upper.Contains("CGG1") || upper.Contains("CGDK2"))
            {
                reading.HardwareModel = upper.Contains("CGG1") ? "CGG1" : "CGDK2";
                reading.DisplayType = "E-Ink";
                reading.DeviceName = "Qingping E-Ink";
            }
            else if (upper.Contains("EINK") || upper.Contains("E-INK") || upper.Contains("EPD") || upper.Contains("EPAPER"))
            {
                reading.DisplayType = "E-Ink";
            }
            else if (upper.Contains("LCD"))
            {
                reading.DisplayType = "LCD";
            }
        }

        // ATC format needs at least 10 bytes for Temp and Humidity
        if (payload.Length < 10)
            return false;

        // Offset 6: Temperature (sint16 LE)
        short rawTemp = (short)(payload[6] | (payload[7] << 8));
        reading.TemperatureC = (Math.Abs(rawTemp) > 2000)
            ? Math.Round(rawTemp * 0.01, 2)
            : Math.Round(rawTemp * 0.1, 1);

        // Offset 8: Humidity (uint16 LE)
        ushort rawHum = (ushort)(payload[8] | (payload[9] << 8));
        reading.HumidityPercent = (rawHum > 1000)
            ? Math.Round(rawHum * 0.01, 1)
            : Math.Round((double)rawHum, 1);

        // Offset 12: Battery % (if present)
        if (payload.Length >= 13)
        {
            reading.BatteryPercent = payload[12];
        }

        return true;
    }
}
