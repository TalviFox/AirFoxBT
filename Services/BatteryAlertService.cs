using System;
using System.Collections.Concurrent;
using System.Windows.Forms;
using AirFoxBT.Models;

namespace AirFoxBT.Services;

public enum BatteryAlertPreset
{
    Balanced,   // <= 15% threshold, max once per 24 hours (Recommended)
    Critical,   // <= 10% threshold, max once per 24 hours
    Early,      // <= 20% threshold, max once per 24 hours
    Urgent,     // <= 5% threshold, max once per 24 hours
    SingleShot, // <= 15% threshold, exactly once until battery is replaced
    Custom,     // Custom threshold, cooldown, and debounce
    Disabled    // Zero notifications
}

public class BatteryAlertService
{
    private BatteryAlertPreset _preset = BatteryAlertPreset.Balanced;
    private int _thresholdPercent = 15;
    private TimeSpan _cooldown = TimeSpan.FromHours(24);
    private int _debouncePackets = 2;
    private int _hysteresisPercent = 5;

    // Startup silence window: ignore alerts for the first 20 seconds of app life
    private readonly DateTime _startupSilenceUntilUtc;

    // MAC -> Device Tracking State
    private class DeviceBatteryState
    {
        public int ConsecutiveLowCount { get; set; }
        public DateTime LastNotifiedUtc { get; set; } = DateTime.MinValue;
        public bool IsArmed { get; set; } = true;
        public int LastBatterySeen { get; set; } = 100;
    }

    private readonly ConcurrentDictionary<string, DeviceBatteryState> _deviceStates = new();

    public event Action<string>? OnLog;

    public BatteryAlertPreset Preset => _preset;
    public int ThresholdPercent => _thresholdPercent;
    public int CooldownHours => (int)_cooldown.TotalHours;
    public int DebouncePackets => _debouncePackets;
    public int HysteresisPercent => _hysteresisPercent;
    public bool IsEnabled => _preset != BatteryAlertPreset.Disabled && _thresholdPercent > 0;

    public BatteryAlertService(string presetName = "Balanced", int customThreshold = 15, int customCooldownHours = 24, int debouncePackets = 2)
    {
        _startupSilenceUntilUtc = DateTime.UtcNow.AddSeconds(20);

        if (Enum.TryParse<BatteryAlertPreset>(presetName, true, out var p) && p != BatteryAlertPreset.Custom)
        {
            SetPreset(p);
        }
        else
        {
            SetCustom(customThreshold, customCooldownHours, debouncePackets);
        }
    }

    public void SetPreset(BatteryAlertPreset preset)
    {
        _preset = preset;

        switch (_preset)
        {
            case BatteryAlertPreset.Critical:
                _thresholdPercent = 10;
                _cooldown = TimeSpan.FromHours(24);
                _debouncePackets = 2;
                break;
            case BatteryAlertPreset.Early:
                _thresholdPercent = 20;
                _cooldown = TimeSpan.FromHours(24);
                _debouncePackets = 2;
                break;
            case BatteryAlertPreset.Urgent:
                _thresholdPercent = 5;
                _cooldown = TimeSpan.FromHours(24);
                _debouncePackets = 2;
                break;
            case BatteryAlertPreset.SingleShot:
                _thresholdPercent = 15;
                _cooldown = TimeSpan.FromDays(365); // Only re-arms after battery replacement
                _debouncePackets = 2;
                break;
            case BatteryAlertPreset.Disabled:
                _thresholdPercent = 0;
                _cooldown = TimeSpan.Zero;
                _debouncePackets = 0;
                break;
            case BatteryAlertPreset.Custom:
                // Retains currently configured custom settings
                break;
            case BatteryAlertPreset.Balanced:
            default:
                _preset = BatteryAlertPreset.Balanced;
                _thresholdPercent = 15;
                _cooldown = TimeSpan.FromHours(24);
                _debouncePackets = 2;
                break;
        }

        OnLog?.Invoke($"[BatteryAlert] Alert preset configured to {_preset} (Threshold: {_thresholdPercent}%, Cooldown: {_cooldown.TotalHours:F0}h, Debounce: {_debouncePackets} pkts)");
    }

    public void SetCustom(int thresholdPercent, int cooldownHours, int debouncePackets, int hysteresis = 5)
    {
        _preset = BatteryAlertPreset.Custom;
        _thresholdPercent = Math.Clamp(thresholdPercent, 1, 99);
        _cooldown = cooldownHours >= 8000 ? TimeSpan.FromDays(365) : TimeSpan.FromHours(Math.Max(1, cooldownHours));
        _debouncePackets = Math.Clamp(debouncePackets, 1, 10);
        _hysteresisPercent = Math.Clamp(hysteresis, 1, 20);

        OnLog?.Invoke($"[BatteryAlert] Custom alert configured (Threshold: {_thresholdPercent}%, Cooldown: {_cooldown.TotalHours:F0}h, Debounce: {_debouncePackets} pkts, Hysteresis: +{_hysteresisPercent}%)");
    }

    /// <summary>
    /// Evaluates a live reading with multi-stage debounce and hysteresis.
    /// </summary>
    public void ProcessReading(BleReading reading, string displayName)
    {
        if (_preset == BatteryAlertPreset.Disabled || !reading.BatteryPercent.HasValue)
        {
            return;
        }

        int battery = reading.BatteryPercent.Value;
        string mac = reading.MacAddress;

        var state = _deviceStates.GetOrAdd(mac, _ => new DeviceBatteryState
        {
            LastBatterySeen = battery,
            IsArmed = battery > (_thresholdPercent + _hysteresisPercent)
        });

        state.LastBatterySeen = battery;

        // Hysteresis Reset: If battery was replaced and rose above (threshold + hysteresis), re-arm alerts
        if (battery > (_thresholdPercent + _hysteresisPercent))
        {
            if (!state.IsArmed)
            {
                state.IsArmed = true;
                state.ConsecutiveLowCount = 0;
                OnLog?.Invoke($"[BatteryAlert] Battery restored on {displayName} ({battery}%). Alert re-armed.");
            }
            return;
        }

        // Check if below threshold
        if (battery <= _thresholdPercent)
        {
            state.ConsecutiveLowCount++;

            // Guardrail 1: Startup silence window
            if (DateTime.UtcNow < _startupSilenceUntilUtc)
            {
                return;
            }

            // Guardrail 2: Packet Debounce - require at least _debouncePackets consecutive low packets to filter out telemetry noise/voltage sags
            if (state.ConsecutiveLowCount < _debouncePackets)
            {
                return;
            }

            // Guardrail 3: Cooldown & Armed check
            var now = DateTime.UtcNow;
            if (state.IsArmed || (now - state.LastNotifiedUtc) >= _cooldown)
            {
                state.LastNotifiedUtc = now;
                state.IsArmed = false; // Disarmed until cooldown elapses or battery is replaced

                FireLowBatteryNotification(displayName, battery, reading.Protocol);
            }
        }
        else
        {
            // Transient reading above threshold (e.g. 17% when threshold is 15%)
            state.ConsecutiveLowCount = 0;
        }
    }

    public void SendTestNotification()
    {
        int testPercent = _thresholdPercent > 0 ? Math.Min(_thresholdPercent, 14) : 12;
        FireLowBatteryNotification("Demo Living Room Sensor", testPercent, "BTHome v2");
    }

    private void FireLowBatteryNotification(string name, int batteryPercent, string protocol)
    {
        string title = $"🔋 Low Battery Alert: {name}";
        string message = $"Battery level is {batteryPercent}% (≤ {_thresholdPercent}% threshold). Consider replacing the battery soon ({protocol}).";

        OnLog?.Invoke($"[BatteryAlert] ⚠️ {title} -> {message}");

        try
        {
            if (App.TrayIcon != null)
            {
                // Routes directly into native Windows 10/11 Action Center
                App.TrayIcon.ShowBalloonTip(6000, title, message, ToolTipIcon.Warning);
            }
        }
        catch { }
    }
}
