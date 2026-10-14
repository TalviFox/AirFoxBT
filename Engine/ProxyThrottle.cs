using System;

namespace AirFoxBT.Engine;

public enum ThrottlePreset
{
    Eco,
    Balanced,
    Realtime,
    Raw
}

public class ThrottleSettings
{
    public ThrottlePreset Preset { get; set; } = ThrottlePreset.Balanced;
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromSeconds(5);
    public bool DeduplicatePayload { get; set; } = true;

    public static ThrottleSettings FromPreset(ThrottlePreset preset) => preset switch
    {
        ThrottlePreset.Eco => new ThrottleSettings
        {
            Preset = ThrottlePreset.Eco,
            Cooldown = TimeSpan.FromSeconds(15),
            DeduplicatePayload = true
        },
        ThrottlePreset.Balanced => new ThrottleSettings
        {
            Preset = ThrottlePreset.Balanced,
            Cooldown = TimeSpan.FromSeconds(5),
            DeduplicatePayload = true
        },
        ThrottlePreset.Realtime => new ThrottleSettings
        {
            Preset = ThrottlePreset.Realtime,
            Cooldown = TimeSpan.FromSeconds(1),
            DeduplicatePayload = true
        },
        ThrottlePreset.Raw => new ThrottleSettings
        {
            Preset = ThrottlePreset.Raw,
            Cooldown = TimeSpan.Zero,
            DeduplicatePayload = false
        },
        _ => FromPreset(ThrottlePreset.Balanced)
    };

    public static ThrottlePreset ParsePreset(string? name)
    {
        if (Enum.TryParse<ThrottlePreset>(name, true, out var preset))
        {
            return preset;
        }
        return ThrottlePreset.Balanced;
    }
}
