using System;
using AirFoxBT.Models;

namespace AirFoxBT.Decoders;

public static class BTHomeDecoder
{
    public const ushort ServiceUuid = 0xFCD2;

    public static bool TryDecode(byte[] payload, string mac, string deviceName, short rssi, out BleReading reading)
    {
        reading = new BleReading
        {
            MacAddress = mac,
            DeviceName = string.IsNullOrWhiteSpace(deviceName) ? "BTHome Device" : deviceName,
            Protocol = "BTHome v2",
            Rssi = rssi,
            LastSeenUtc = DateTime.UtcNow
        };

        // Submodel & Display Type heuristic inference from over-the-air LocalName
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            var upper = deviceName.ToUpperInvariant();
            if (upper.Contains("103") || upper.Contains("SBHT-103"))
            {
                reading.HardwareModel = "SBHT-103C";
                reading.DisplayType = "E-Ink";
                reading.DeviceName = "Shelly BLU H&T Display";
            }
            else if (upper.Contains("003") || upper.Contains("SBHT-003"))
            {
                reading.HardwareModel = "SBHT-003C";
                reading.DisplayType = "Puck";
                reading.DeviceName = "Shelly BLU H&T Puck";
            }
            else if (upper.Contains("SBDW") || upper.Contains("DW"))
            {
                reading.HardwareModel = "SBDW-002C";
                reading.DeviceName = "Shelly BLU Door/Window";
            }
            else if (upper.Contains("SBBT"))
            {
                reading.HardwareModel = "SBBT-002C";
                reading.DeviceName = "Shelly BLU Button";
            }
            else if (upper.Contains("SBM"))
            {
                reading.HardwareModel = "SBM-002C";
                reading.DeviceName = "Shelly BLU Motion";
            }
            else if (upper.Contains("C401") || upper.Contains("MHO-C401"))
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

        if (payload.Length < 3)
            return false;

        // Byte 0: Header (or direct 0x0D Time and Timezone block ID on Shelly Time Beacons)
        byte header = payload[0];
        bool isDirectTimeBeacon = header == 0x0D && payload.Length >= 17;
        bool isEncrypted = !isDirectTimeBeacon && (header & 0x01) != 0;
        if (isEncrypted)
        {
            reading.IsEncrypted = true;
            reading.Protocol = "BTHome v2 (Encrypted)";
            reading.DeviceCategory = "Encrypted";
            if (reading.DeviceName == "BTHome Device")
            {
                if (mac.StartsWith("94:02:16", StringComparison.OrdinalIgnoreCase) ||
                    mac.StartsWith("B8:D6:1A", StringComparison.OrdinalIgnoreCase) ||
                    mac.StartsWith("BC:02:6E", StringComparison.OrdinalIgnoreCase))
                {
                    reading.DeviceName = "Shelly BLU (Encrypted)";
                }
                else
                {
                    reading.DeviceName = "BTHome (Encrypted)";
                }
            }
            return true;
        }

        int index = isDirectTimeBeacon ? 0 : 1;
        bool foundAnyData = false;

        while (index < payload.Length)
        {
            byte objectId = payload[index++];
            if (index >= payload.Length) break;

            switch (objectId)
            {
                case 0x00: // Packet ID (1 byte uint8)
                    index += 1;
                    break;

                case 0x01: // Battery % (1 byte uint8)
                    reading.BatteryPercent = payload[index++];
                    foundAnyData = true;
                    break;

                case 0x02: // Temperature 0.01°C (2 bytes sint16 LE)
                    if (index + 2 <= payload.Length)
                    {
                        short raw = (short)(payload[index] | (payload[index + 1] << 8));
                        reading.TemperatureC = Math.Round(raw * 0.01, 2);
                        foundAnyData = true;
                        index += 2;
                    }
                    break;

                case 0x03: // Humidity 0.01% (2 bytes uint16 LE)
                    if (index + 2 <= payload.Length)
                    {
                        ushort raw = (ushort)(payload[index] | (payload[index + 1] << 8));
                        reading.HumidityPercent = Math.Round(raw * 0.01, 1);
                        foundAnyData = true;
                        index += 2;
                    }
                    break;

                case 0x05: // Illuminance 0.01 lux (3 bytes uint24 LE)
                    if (index + 3 <= payload.Length)
                    {
                        uint raw = (uint)(payload[index] | (payload[index + 1] << 8) | (payload[index + 2] << 16));
                        reading.IlluminanceLux = Math.Round(raw * 0.01, 1);
                        foundAnyData = true;
                        index += 3;
                    }
                    break;

                case 0x0D: // Time and Timezone block (16-18 bytes: uint32 unixTime, int32 tzOffset, uint32 dstStart, uint32 dstEnd, int16 dstShift)
                    if (index + 16 <= payload.Length)
                    {
                        uint unixTime = BitConverter.ToUInt32(payload, index);
                        int tzOffset = BitConverter.ToInt32(payload, index + 4);
                        uint dstStart = BitConverter.ToUInt32(payload, index + 8);
                        uint dstEnd = BitConverter.ToUInt32(payload, index + 12);
                        index += 16;

                        int dstShift = 0;
                        if (index + 4 <= payload.Length)
                        {
                            dstShift = BitConverter.ToInt32(payload, index);
                            index += 4;
                        }
                        else if (index + 2 <= payload.Length)
                        {
                            dstShift = BitConverter.ToInt16(payload, index);
                            index += 2;
                        }

                        reading.BeaconUnixTime = unixTime;
                        reading.BeaconUtcOffsetSeconds = tzOffset;
                        reading.BeaconDstShiftSeconds = dstShift;
                        reading.DeviceCategory = "TimeBeacon";
                        reading.Protocol = "BTHome Time Beacon";
                        if (reading.DeviceName == "BTHome Device")
                        {
                            reading.DeviceName = "Shelly Time Beacon";
                        }
                        foundAnyData = true;
                    }
                    else
                    {
                        index = payload.Length;
                    }
                    break;

                case 0x11: // Opening binary sensor (1 byte uint8: 0 = closed, 1 = open)
                case 0x1A: // Door binary sensor (1 byte uint8: 0 = closed, 1 = open)
                case 0x2D: // Window binary sensor (1 byte uint8: 0 = closed, 1 = open)
                    reading.IsOpen = (payload[index++] != 0);
                    reading.DeviceCategory = "Contact";
                    foundAnyData = true;
                    if (reading.DeviceName == "BTHome Device")
                    {
                        reading.DeviceName = "Shelly BLU Door/Window";
                    }
                    break;

                case 0x21: // Motion binary sensor (1 byte uint8: 0 = clear, 1 = motion)
                case 0x22: // Moving binary sensor
                    reading.IsMotion = (payload[index++] != 0);
                    reading.DeviceCategory = "Motion";
                    foundAnyData = true;
                    if (reading.DeviceName == "BTHome Device")
                    {
                        reading.DeviceName = "Shelly BLU Motion";
                    }
                    break;

                case 0x3A: // Button event (1 byte uint8: 0=none, 1=press, 2=double, 3=triple, 4=long, 0xFE=hold)
                    byte btnCode = payload[index++];
                    reading.ButtonEventCode = btnCode;
                    reading.ButtonEventName = btnCode switch
                    {
                        1 => "Single Press",
                        2 => "Double Press",
                        3 => "Triple Press",
                        4 => "Long Press",
                        0xFE => "Hold",
                        _ => "Press"
                    };
                    foundAnyData = true;
                    bool isKnownShellyHt = (!string.IsNullOrEmpty(reading.HardwareModel) && reading.HardwareModel.StartsWith("SBHT", StringComparison.OrdinalIgnoreCase)) ||
                                           reading.DisplayType == "E-Ink" || reading.DisplayType == "Puck";

                    if (!reading.TemperatureC.HasValue && !reading.HumidityPercent.HasValue && !reading.IsOpen.HasValue && !isKnownShellyHt)
                    {
                        reading.DeviceCategory = "Button";
                        if (reading.DeviceName == "BTHome Device")
                        {
                            reading.DeviceName = "Shelly BLU Button";
                        }
                    }
                    break;

                case 0x18: // Cold sensor (1 byte uint8)
                case 0x1E: // Light state / event (1 byte uint8)
                    index += 1;
                    break;

                case 0x2E: // Humidity coarse 1% (1 byte uint8)
                    reading.HumidityPercent = payload[index++];
                    foundAnyData = true;
                    break;

                case 0x39: // Rotation / Tilt angle 0.1° (2 bytes sint16 LE) - Shelly BLU Door/Window
                    if (index + 2 <= payload.Length)
                    {
                        short raw = (short)(payload[index] | (payload[index + 1] << 8));
                        reading.RotationDegrees = Math.Round(raw * 0.1, 1);
                        foundAnyData = true;
                        index += 2;
                    }
                    break;

                case 0x45: // Temperature coarse 0.1°C (2 bytes sint16 LE)
                    if (index + 2 <= payload.Length)
                    {
                        short raw = (short)(payload[index] | (payload[index + 1] << 8));
                        reading.TemperatureC = Math.Round(raw * 0.1, 1);
                        foundAnyData = true;
                        index += 2;
                    }
                    break;

                default:
                    // Unknown TLV id, advance index safely
                    index++;
                    break;
            }
        }

        // Final category deduction
        if (reading.IsOpen.HasValue)
        {
            reading.DeviceCategory = "Contact";
        }
        else if (reading.IsMotion.HasValue)
        {
            reading.DeviceCategory = "Motion";
        }
        else if (!string.IsNullOrEmpty(reading.ButtonEventName))
        {
            reading.DeviceCategory = "Button";
        }
        else if (reading.TemperatureC.HasValue || reading.HumidityPercent.HasValue)
        {
            reading.DeviceCategory = "Environmental";
            if (reading.DeviceName == "BTHome Device")
            {
                if (reading.DisplayType == "E-Ink" || reading.HardwareModel == "SBHT-103C")
                {
                    reading.DeviceName = "Shelly BLU H&T Display";
                }
                else if (reading.DisplayType == "Puck" || reading.HardwareModel == "SBHT-003C")
                {
                    reading.DeviceName = "Shelly BLU H&T Puck";
                }
                else
                {
                    reading.DeviceName = "Shelly BLU H&T";
                }
            }
        }
        else if (reading.DeviceCategory == "TimeBeacon")
        {
            // Preserve TimeBeacon category
        }
        else if (foundAnyData)
        {
            reading.DeviceCategory = "PassThrough";
        }

        return foundAnyData;
    }
}
