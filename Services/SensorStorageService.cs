using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AirFoxBT.Models;

namespace AirFoxBT.Services;

public class StoredSensorConfig
{
    public string MacAddress { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public string Protocol { get; set; } = "BTHome v2";
    public double? LastTemperatureC { get; set; }
    public double? LastHumidityPercent { get; set; }
    public int? LastBatteryPercent { get; set; }
    public short LastRssi { get; set; } = -80;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public bool IsSyncEnabled { get; set; } = false;
    public string? HardwareModel { get; set; }
    public bool? HasDisplay { get; set; }
    public string? DisplayType { get; set; }
    public bool? LastIsOpen { get; set; }
    public double? LastIlluminanceLux { get; set; }
    public double? LastRotationDegrees { get; set; }
    public bool? LastIsMotion { get; set; }
    public string? LastButtonEvent { get; set; }
    public bool IsEncrypted { get; set; }
    public string? DeviceCategory { get; set; }
    public bool IsIgnored { get; set; } = false;
    public string? GroupName { get; set; }
}

public class AppSettings
{
    public string HomeAssistantUrl { get; set; } = "http://homeassistant.local:8123";
    public string HomeAssistantToken { get; set; } = string.Empty;
    public bool IsHaSyncEnabled { get; set; } = false;
    public bool AutoSyncNamedSensors { get; set; } = true;
    public int SyncIntervalSeconds { get; set; } = 10;
    public string ThrottleMode { get; set; } = "Balanced";
    public string SelectedHostIp { get; set; } = "Auto";
    public string DeviceName { get; set; } = string.Empty;
    public bool EnableShellyTimeBeacon { get; set; } = true;
    public bool CheckForUpdates { get; set; } = true;
    public DateTime? LastUpdateCheckUtc { get; set; }
    public string BatteryAlertPreset { get; set; } = "Balanced";
    public int BatteryThresholdPercent { get; set; } = 15;
    public int BatteryCooldownHours { get; set; } = 24;
    public int BatteryDebouncePackets { get; set; } = 2;
}

public class SensorStorageService
{
    private static readonly string StorageDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FoxDen",
        "AirFoxBT"
    );

    private static readonly string StorageFile = Path.Combine(StorageDir, "sensors.json");
    private static readonly string SettingsFile = Path.Combine(StorageDir, "settings.json");
    private readonly object _lock = new();

    public SensorStorageService()
    {
        if (!Directory.Exists(StorageDir))
        {
            Directory.CreateDirectory(StorageDir);
        }
    }

    public Dictionary<string, StoredSensorConfig> LoadSensors()
    {
        lock (_lock)
        {
            if (!File.Exists(StorageFile))
            {
                return new Dictionary<string, StoredSensorConfig>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var json = File.ReadAllText(StorageFile);
                var list = JsonSerializer.Deserialize<List<StoredSensorConfig>>(json);
                var dict = new Dictionary<string, StoredSensorConfig>(StringComparer.OrdinalIgnoreCase);
                if (list != null)
                {
                    foreach (var s in list)
                    {
                        dict[s.MacAddress] = s;
                    }
                }
                return dict;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading sensors.json: {ex.Message}");
                return new Dictionary<string, StoredSensorConfig>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public void SaveSensors(IEnumerable<StoredSensorConfig> sensors)
    {
        lock (_lock)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(sensors, options);
                File.WriteAllText(StorageFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving sensors.json: {ex.Message}");
            }
        }
    }

    public void DeleteSensor(string macAddress)
    {
        lock (_lock)
        {
            var dict = LoadSensors();
            if (dict.Remove(macAddress))
            {
                SaveSensors(dict.Values);
            }
        }
    }

    public void DeleteAllSensors()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(StorageFile))
                {
                    File.Delete(StorageFile);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting sensors.json: {ex.Message}");
            }
        }
    }

    public void ExportSensors(string destinationPath, IEnumerable<StoredSensorConfig> sensors)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(sensors, options);
        File.WriteAllText(destinationPath, json);
    }

    public List<StoredSensorConfig> ImportSensors(string sourcePath)
    {
        var json = File.ReadAllText(sourcePath);
        var list = JsonSerializer.Deserialize<List<StoredSensorConfig>>(json);
        return list ?? new List<StoredSensorConfig>();
    }

    public AppSettings LoadSettings()
    {
        lock (_lock)
        {
            if (!File.Exists(SettingsFile))
            {
                return new AppSettings();
            }

            try
            {
                var json = File.ReadAllText(SettingsFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                return settings ?? new AppSettings();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading settings.json: {ex.Message}");
                return new AppSettings();
            }
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        lock (_lock)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(SettingsFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving settings.json: {ex.Message}");
            }
        }
    }
}
