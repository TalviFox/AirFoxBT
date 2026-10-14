using System;

namespace AirFoxBT.Models;

public class BleReading
{
    public string MacAddress { get; set; } = string.Empty;
    public string DeviceName { get; set; } = "Unknown";
    public string Protocol { get; set; } = string.Empty;
    public double? TemperatureC { get; set; }
    public double? TemperatureF => TemperatureC.HasValue ? Math.Round(TemperatureC.Value * 9.0 / 5.0 + 32.0, 1) : null;
    public double? HumidityPercent { get; set; }
    public int? BatteryPercent { get; set; }
    public short Rssi { get; set; }
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

    // Contact & Environmental Extensions (Shelly BLU Door/Window, etc.)
    public bool? IsOpen { get; set; }
    public double? IlluminanceLux { get; set; }
    public double? RotationDegrees { get; set; }

    // Motion & Button Extensions (Shelly BLU Motion, Button1, etc.)
    public bool? IsMotion { get; set; }
    public byte? ButtonEventCode { get; set; }
    public string? ButtonEventName { get; set; }

    // Encryption & Category Flags
    public bool IsEncrypted { get; set; }
    public string DeviceCategory { get; set; } = "Environmental"; // "Environmental", "Contact", "Button", "Motion", "Encrypted", "PassThrough", "TimeBeacon"
    public string? HardwareModel { get; set; }
    public string? DisplayType { get; set; } // "E-Ink", "LCD", "Puck", "Screenless", etc.

    // Time Beacon Extensions (BTHome 0xFCD2 / 0x0D)
    public uint? BeaconUnixTime { get; set; }
    public int? BeaconUtcOffsetSeconds { get; set; }
    public int? BeaconDstShiftSeconds { get; set; }

    public bool IsContactSensor => IsOpen.HasValue || DeviceCategory == "Contact";
    public bool IsMotionSensor => IsMotion.HasValue || DeviceCategory == "Motion";
    public bool IsButtonSensor => !string.IsNullOrEmpty(ButtonEventName) || DeviceCategory == "Button";
    public bool IsEncryptedRelay => IsEncrypted || DeviceCategory == "Encrypted";
    public bool IsPassThroughRelay => DeviceCategory == "PassThrough";
    public bool IsTimeBeacon => DeviceCategory == "TimeBeacon";

    public override string ToString()
    {
        var namePart = string.IsNullOrEmpty(DeviceName) || DeviceName == "Unknown" ? "" : $" ({DeviceName})";
        var statePart = IsOpen.HasValue ? (IsOpen.Value ? " [OPEN]" : " [CLOSED]") : "";
        if (IsMotion.HasValue) statePart = IsMotion.Value ? " [MOTION]" : " [CLEAR]";
        if (!string.IsNullOrEmpty(ButtonEventName)) statePart = $" [{ButtonEventName}]";
        if (IsEncrypted) statePart = " [ENCRYPTED]";
        if (IsTimeBeacon && BeaconUnixTime.HasValue)
        {
            return $"📡 {MacAddress}{namePart} [{Protocol}] -> Unix: {BeaconUnixTime} • Offset: {BeaconUtcOffsetSeconds ?? 0}s • Shift: {BeaconDstShiftSeconds ?? 0}s • RSSI: {Rssi} dBm";
        }

        var tempPart = TemperatureC.HasValue ? $"{TemperatureF:F1}°F / {TemperatureC:F1}°C" : "N/A";
        var humPart = HumidityPercent.HasValue ? $"{HumidityPercent:F0}% RH" : "N/A";
        var battPart = BatteryPercent.HasValue ? $"{BatteryPercent}%" : "N/A";

        return $"📡 {MacAddress}{namePart}{statePart} [{Protocol}] -> Temp: {tempPart} • Hum: {humPart} • Batt: {battPart} • RSSI: {Rssi} dBm";
    }
}
