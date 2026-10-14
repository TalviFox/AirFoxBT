using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using AirFoxBT.Models;
using AirFoxBT.Services;

namespace AirFoxBT.ViewModels;

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}

public class SensorCardViewModel : INotifyPropertyChanged
{
    private string _macAddress = string.Empty;
    private string _deviceName = "BTHome Device";
    private string _friendlyName = string.Empty;
    private string _protocol = "BTHome v2";
    private double? _temperatureC;
    private double? _humidityPercent;
    private int? _batteryPercent;
    private short _rssi = -100;
    private DateTime _lastSeenUtc = DateTime.UtcNow;
    private bool _isEditing = false;
    private string _editNameText = string.Empty;
    private bool _isSyncEnabled = false;
    private bool _isAwaitingBeacon = false;
    private string? _hardwareModel;
    private bool? _hasDisplay;
    private string? _displayType;
    private string _editDisplayType = "Auto";
    private bool _isIgnored = false;
    private string? _groupName;
    private string _editGroupName = string.Empty;
    private bool? _isOpen;
    private double? _illuminanceLux;
    private double? _rotationDegrees;
    private string _deviceCategory = "Environmental";
    private bool _isEncrypted = false;
    private bool? _isMotion;
    private string? _buttonEventName;

    public event Action<SensorCardViewModel>? OnConfigChanged;

    public bool IsSyncEnabled
    {
        get => _isSyncEnabled;
        set
        {
            if (SetField(ref _isSyncEnabled, value))
            {
                OnConfigChanged?.Invoke(this);
            }
        }
    }

    public string? HardwareModel
    {
        get => _hardwareModel;
        set
        {
            if (SetField(ref _hardwareModel, value))
            {
                OnPropertyChanged(nameof(FooterTimestampAndProtocolDisplay));
                OnPropertyChanged(nameof(DisplayTypeTooltip));
                OnPropertyChanged(nameof(HasDisplayTypeBadge));
                OnPropertyChanged(nameof(DisplayTypeBadgeText));
            }
        }
    }

    public bool? HasDisplay
    {
        get => _hasDisplay;
        set
        {
            if (SetField(ref _hasDisplay, value))
            {
                if (value == true && string.IsNullOrEmpty(_displayType))
                {
                    DisplayType = "E-Ink";
                }
                else if (value == false && string.IsNullOrEmpty(_displayType))
                {
                    DisplayType = "Puck";
                }
                OnPropertyChanged(nameof(DisplayTypeTooltip));
                OnPropertyChanged(nameof(HasDisplayTypeBadge));
                OnPropertyChanged(nameof(DisplayTypeBadgeText));
            }
        }
    }

    public string? DisplayType
    {
        get
        {
            if (!string.IsNullOrEmpty(_displayType)) return _displayType;
            if (HardwareModel == "SBHT-103C" || HasDisplay == true) return "E-Ink";
            if (HardwareModel == "SBHT-003C" || HasDisplay == false) return "Puck";
            if (HardwareModel == "LYWSD03MMC" || HardwareModel == "MJWSD05MMC") return "LCD";
            if (HardwareModel == "MHO-C401" || HardwareModel == "LYWSD02MMC" || HardwareModel == "CGG1" || HardwareModel == "CGDK2" || HardwareModel == "XMWSDJ04MMC" || HardwareModel == "MHO-C122") return "E-Ink";
            return null;
        }
        set
        {
            if (SetField(ref _displayType, value))
            {
                OnPropertyChanged(nameof(HasDisplayTypeBadge));
                OnPropertyChanged(nameof(DisplayTypeBadgeText));
                OnPropertyChanged(nameof(DisplayTypeTooltip));
                OnPropertyChanged(nameof(DisplayTypeBackgroundBrush));
                OnPropertyChanged(nameof(DisplayTypeBorderBrush));
                OnPropertyChanged(nameof(DisplayTypeForegroundBrush));
            }
        }
    }

    public bool HasDisplayTypeBadge => IsEnvironmentalMode && !string.IsNullOrEmpty(DisplayType);

    public string DisplayTypeBadgeText => DisplayType switch
    {
        "E-Ink" => "E-INK",
        "LCD" => "LCD",
        "Puck" or "Screenless" => "PUCK",
        _ => DisplayType?.ToUpperInvariant() ?? string.Empty
    };

    public string DisplayTypeTooltip => DisplayType switch
    {
        "E-Ink" => $"E-Paper Display{(string.IsNullOrEmpty(HardwareModel) ? "" : $" ({HardwareModel})")}",
        "LCD" => $"Segment LCD Display{(string.IsNullOrEmpty(HardwareModel) ? "" : $" ({HardwareModel})")}",
        "Puck" or "Screenless" => $"Screenless Sensor Puck{(string.IsNullOrEmpty(HardwareModel) ? "" : $" ({HardwareModel})")}",
        _ => $"{DisplayType} Display"
    };

    public Brush DisplayTypeBackgroundBrush => DisplayType switch
    {
        "E-Ink" => new SolidColorBrush(Color.FromArgb(28, 14, 165, 233)), // #1C0EA5E9
        "LCD" => new SolidColorBrush(Color.FromArgb(24, 99, 102, 241)),   // #186366F1
        "Puck" or "Screenless" => new SolidColorBrush(Color.FromArgb(22, 100, 116, 139)), // #1664748B
        _ => new SolidColorBrush(Color.FromArgb(24, 56, 189, 248))
    };

    public Brush DisplayTypeBorderBrush => DisplayType switch
    {
        "E-Ink" => new SolidColorBrush(Color.FromArgb(80, 56, 189, 248)),  // #5038BDF8
        "LCD" => new SolidColorBrush(Color.FromArgb(80, 129, 140, 248)),   // #50818CF8
        "Puck" or "Screenless" => new SolidColorBrush(Color.FromArgb(60, 148, 163, 184)), // #3C94A3B8
        _ => new SolidColorBrush(Color.FromArgb(60, 56, 189, 248))
    };

    public Brush DisplayTypeForegroundBrush => DisplayType switch
    {
        "E-Ink" => new SolidColorBrush(Color.FromRgb(56, 189, 248)),   // Crisp Sky #38BDF8
        "LCD" => new SolidColorBrush(Color.FromRgb(165, 180, 252)),   // Indigo-Lavender #A5B4FC
        "Puck" or "Screenless" => new SolidColorBrush(Color.FromRgb(148, 163, 184)), // Slate #94A3B8
        _ => new SolidColorBrush(Color.FromRgb(56, 189, 248))
    };

    public bool IsIgnored
    {
        get => _isIgnored;
        set
        {
            if (SetField(ref _isIgnored, value))
            {
                OnPropertyChanged(nameof(IsRelayed));
                OnPropertyChanged(nameof(RelayStatusBadgeText));
                OnPropertyChanged(nameof(RelayStatusTooltip));
                OnPropertyChanged(nameof(RelayStatusBackgroundBrush));
                OnPropertyChanged(nameof(RelayStatusBorderBrush));
                OnPropertyChanged(nameof(RelayStatusForegroundBrush));
                OnPropertyChanged(nameof(RelayStatusDotBrush));
                OnConfigChanged?.Invoke(this);
            }
        }
    }

    public bool IsRelayed => !_isIgnored;

    public string RelayStatusBadgeText => _isIgnored ? "Ignored" : "HA Relayed";

    public string RelayStatusTooltip => _isIgnored
        ? "Blocked from Home Assistant (:6053). Monitored locally in AirFoxBT only. Click to allow relay."
        : "Active ESPHome Bluetooth Proxy: Relayed directly to Home Assistant on port 6053. Click to block / ignore.";

    public Brush RelayStatusBackgroundBrush => _isIgnored
        ? new SolidColorBrush(Color.FromArgb(24, 245, 158, 11)) // #18F59E0B Fox Amber
        : new SolidColorBrush(Color.FromArgb(18, 16, 185, 129)); // #1210B981 Emerald

    public Brush RelayStatusBorderBrush => _isIgnored
        ? new SolidColorBrush(Color.FromArgb(64, 245, 158, 11)) // #40F59E0B
        : new SolidColorBrush(Color.FromArgb(53, 16, 185, 129)); // #3510B981

    public Brush RelayStatusForegroundBrush => _isIgnored
        ? new SolidColorBrush(Color.FromRgb(245, 158, 11)) // #F59E0B
        : new SolidColorBrush(Color.FromRgb(52, 211, 153)); // #34D399

    public Brush RelayStatusDotBrush => _isIgnored
        ? new SolidColorBrush(Color.FromRgb(245, 158, 11))
        : new SolidColorBrush(Color.FromRgb(52, 211, 153));

    public ICommand ToggleRelayCommand => new RelayCommand(_ =>
    {
        IsIgnored = !IsIgnored;
    });

    public string? GroupName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_groupName)) return _groupName;
            var name = !string.IsNullOrWhiteSpace(_friendlyName) ? _friendlyName : _deviceName;
            if (name.Contains("Outside", StringComparison.OrdinalIgnoreCase)) return "Outside";
            if (name.Contains("Upstairs", StringComparison.OrdinalIgnoreCase)) return "Upstairs";
            if (name.Contains("Downstairs", StringComparison.OrdinalIgnoreCase)) return "Downstairs";
            if (name.Contains("PC", StringComparison.OrdinalIgnoreCase) || name.Contains("Hokkyokugitsune", StringComparison.OrdinalIgnoreCase)) return "PC";
            if (DeviceCategory == "Contact" || name.Contains("Door", StringComparison.OrdinalIgnoreCase) || name.Contains("Window", StringComparison.OrdinalIgnoreCase)) return "Doors";
            return "Other";
        }
        set
        {
            if (SetField(ref _groupName, value))
            {
                OnPropertyChanged(nameof(GroupDisplay));
            }
        }
    }

    public string GroupDisplay => GroupName ?? "Other";

    public string EditGroupName
    {
        get => _editGroupName;
        set => SetField(ref _editGroupName, value);
    }

    public string DeviceCategory
    {
        get => _deviceCategory;
        set
        {
            if (SetField(ref _deviceCategory, value))
            {
                NotifyModeProperties();
            }
        }
    }

    public bool IsEncrypted
    {
        get => _isEncrypted;
        set
        {
            if (SetField(ref _isEncrypted, value))
            {
                NotifyModeProperties();
            }
        }
    }

    public bool? IsMotion
    {
        get => _isMotion;
        set
        {
            if (SetField(ref _isMotion, value))
            {
                NotifyModeProperties();
                OnPropertyChanged(nameof(MotionStateDisplay));
                OnPropertyChanged(nameof(MotionStateBrush));
                OnPropertyChanged(nameof(MotionStateBackgroundBrush));
                OnPropertyChanged(nameof(MotionStateBorderBrush));
            }
        }
    }

    public string? ButtonEventName
    {
        get => _buttonEventName;
        set
        {
            if (SetField(ref _buttonEventName, value))
            {
                NotifyModeProperties();
                OnPropertyChanged(nameof(ButtonEventDisplay));
            }
        }
    }

    public string CardMode
    {
        get
        {
            if (_isEncrypted || _deviceCategory == "Encrypted") return "Encrypted";

            // Contact sensors (reed switch open/closed or Window/Door classification) without environmental metrics
            if ((_isOpen.HasValue || _deviceCategory == "Contact" || _groupName == "Window" || _groupName == "Door") &&
                !_temperatureC.HasValue && !_humidityPercent.HasValue)
            {
                return "Contact";
            }

            // Motion sensors (PIR state) without environmental metrics
            if ((_isMotion.HasValue || _deviceCategory == "Motion") &&
                !_temperatureC.HasValue && !_humidityPercent.HasValue)
            {
                return "Motion";
            }

            // Environmental sensors ALWAYS stay in Environmental mode!
            // Temperature, Humidity, and display telemetry must NEVER be hidden by an auxiliary button press.
            if (_temperatureC.HasValue || _humidityPercent.HasValue || _deviceCategory == "Environmental" ||
                (!string.IsNullOrEmpty(_hardwareModel) && _hardwareModel.StartsWith("SBHT", StringComparison.OrdinalIgnoreCase)))
            {
                return "Environmental";
            }

            if (_displayType == "E-Ink" || _displayType == "Puck")
            {
                return "Environmental";
            }

            if (_isOpen.HasValue || _deviceCategory == "Contact") return "Contact";
            if (_isMotion.HasValue || _deviceCategory == "Motion") return "Motion";
            if (!string.IsNullOrEmpty(_buttonEventName) || _deviceCategory == "Button") return "Button";
            if (_deviceCategory == "PassThrough") return "PassThrough";
            if (!_temperatureC.HasValue && !_humidityPercent.HasValue && !string.IsNullOrEmpty(_deviceCategory) && _deviceCategory != "Environmental") return "PassThrough";
            return "Environmental";
        }
    }

    public bool IsEnvironmentalMode => CardMode == "Environmental";
    public bool IsContactMode => CardMode == "Contact";
    public bool IsButtonMode => CardMode == "Button";
    public bool IsMotionMode => CardMode == "Motion";
    public bool IsEncryptedMode => CardMode == "Encrypted";
    public bool IsPassThroughMode => CardMode == "PassThrough";

    public bool IsContactSensor => IsContactMode;

    private void NotifyModeProperties()
    {
        OnPropertyChanged(nameof(CardMode));
        OnPropertyChanged(nameof(IsEnvironmentalMode));
        OnPropertyChanged(nameof(IsContactMode));
        OnPropertyChanged(nameof(IsButtonMode));
        OnPropertyChanged(nameof(IsMotionMode));
        OnPropertyChanged(nameof(IsEncryptedMode));
        OnPropertyChanged(nameof(IsPassThroughMode));
        OnPropertyChanged(nameof(IsContactSensor));
        OnPropertyChanged(nameof(CategoryBadgeText));
        OnPropertyChanged(nameof(StateIndicatorBrush));
    }

    public string CategoryBadgeText => CardMode switch
    {
        "Contact" => "Door / Window",
        "Button" => "Button Remote",
        "Motion" => "Motion Sensor",
        "Encrypted" => "Encrypted Relay",
        "PassThrough" => "Pass-Through Relay",
        _ => "Thermo-Hygro"
    };

    public bool? IsOpen
    {
        get => _isOpen;
        set
        {
            if (SetField(ref _isOpen, value))
            {
                NotifyModeProperties();
                OnPropertyChanged(nameof(ContactStateDisplay));
                OnPropertyChanged(nameof(ContactStateBrush));
                OnPropertyChanged(nameof(ContactStateBackgroundBrush));
                OnPropertyChanged(nameof(ContactStateBorderBrush));
            }
        }
    }

    public double? IlluminanceLux
    {
        get => _illuminanceLux;
        set
        {
            if (SetField(ref _illuminanceLux, value))
            {
                OnPropertyChanged(nameof(ContactDetailsDisplay));
                OnPropertyChanged(nameof(MotionDetailsDisplay));
            }
        }
    }

    public double? RotationDegrees
    {
        get => _rotationDegrees;
        set
        {
            if (SetField(ref _rotationDegrees, value))
            {
                OnPropertyChanged(nameof(ContactDetailsDisplay));
            }
        }
    }

    // Contact Display Properties
    public string ContactStateDisplay => _isOpen switch
    {
        true => "OPEN",
        false => "CLOSED",
        null => "UNKNOWN"
    };

    public Brush ContactStateBrush => _isOpen switch
    {
        true => new SolidColorBrush(Color.FromRgb(248, 113, 113)), // Soft Coral
        false => new SolidColorBrush(Color.FromRgb(52, 211, 153)), // Emerald Green
        null => new SolidColorBrush(Color.FromRgb(148, 163, 184))
    };

    public Brush ContactStateBackgroundBrush => _isOpen switch
    {
        true => new SolidColorBrush(Color.FromArgb(40, 248, 113, 113)),
        false => new SolidColorBrush(Color.FromArgb(30, 52, 211, 153)),
        null => new SolidColorBrush(Color.FromArgb(20, 148, 163, 184))
    };

    public Brush ContactStateBorderBrush => _isOpen switch
    {
        true => new SolidColorBrush(Color.FromArgb(120, 248, 113, 113)),
        false => new SolidColorBrush(Color.FromArgb(100, 52, 211, 153)),
        null => new SolidColorBrush(Color.FromArgb(60, 148, 163, 184))
    };

    public string ContactDetailsDisplay
    {
        get
        {
            if (_rotationDegrees.HasValue && Math.Abs(_rotationDegrees.Value) > 0.05)
                return $"{_rotationDegrees:F1}° tilt";
            if (_illuminanceLux.HasValue)
                return $"{_illuminanceLux:F0} lux";
            return "Door / Window";
        }
    }

    // Button Remote Properties
    public string ButtonEventDisplay => !string.IsNullOrWhiteSpace(_buttonEventName) ? _buttonEventName.ToUpperInvariant() : "STANDBY";
    public string ButtonDetailsDisplay => "Shelly BLU Button";
    public Brush ButtonStateBrush => new SolidColorBrush(Color.FromRgb(56, 189, 248)); // Sky Blue #38BDF8
    public Brush ButtonStateBackgroundBrush => new SolidColorBrush(Color.FromArgb(35, 56, 189, 248));
    public Brush ButtonStateBorderBrush => new SolidColorBrush(Color.FromArgb(100, 56, 189, 248));

    // Motion Sensor Properties
    public string MotionStateDisplay => _isMotion == true ? "MOTION" : "CLEAR";
    public string MotionDetailsDisplay => _illuminanceLux.HasValue ? $"{_illuminanceLux:F0} lux" : "PIR Motion Sensor";
    public Brush MotionStateBrush => _isMotion == true
        ? new SolidColorBrush(Color.FromRgb(245, 158, 11)) // Fox Amber
        : new SolidColorBrush(Color.FromRgb(52, 211, 153)); // Emerald Green
    public Brush MotionStateBackgroundBrush => _isMotion == true
        ? new SolidColorBrush(Color.FromArgb(40, 245, 158, 11))
        : new SolidColorBrush(Color.FromArgb(25, 52, 211, 153));
    public Brush MotionStateBorderBrush => _isMotion == true
        ? new SolidColorBrush(Color.FromArgb(120, 245, 158, 11))
        : new SolidColorBrush(Color.FromArgb(80, 52, 211, 153));

    // Encrypted Beacon Properties
    public string EncryptedStateDisplay => "ENCRYPTED";
    public string EncryptedDetailsDisplay => "Decrypted in HA";
    public Brush EncryptedStateBrush => new SolidColorBrush(Color.FromRgb(129, 140, 248)); // Indigo #818CF8
    public Brush EncryptedStateBackgroundBrush => new SolidColorBrush(Color.FromArgb(35, 129, 140, 248));
    public Brush EncryptedStateBorderBrush => new SolidColorBrush(Color.FromArgb(100, 129, 140, 248));

    // Pass-Through Relay Properties
    public string PassThroughStateDisplay => "RELAYED";
    public string PassThroughDetailsDisplay => "ESPHome Native API";
    public Brush PassThroughStateBrush => new SolidColorBrush(Color.FromRgb(52, 211, 153)); // Emerald Green
    public Brush PassThroughStateBackgroundBrush => new SolidColorBrush(Color.FromArgb(30, 52, 211, 153));
    public Brush PassThroughStateBorderBrush => new SolidColorBrush(Color.FromArgb(80, 52, 211, 153));

    // 2px Sleek Indicator Meter Line Brush
    public Brush StateIndicatorBrush => CardMode switch
    {
        "Contact" => ContactStateBrush,
        "Button" => ButtonStateBrush,
        "Motion" => MotionStateBrush,
        "Encrypted" => EncryptedStateBrush,
        "PassThrough" => PassThroughStateBrush,
        _ => TemperatureBrush
    };

    public ICommand BeginEditCommand { get; }
    public ICommand SaveEditCommand { get; }
    public ICommand CancelEditCommand { get; }
    public ICommand CopyMacCommand { get; }
    public ICommand DeleteSensorCommand { get; }

    public Action<SensorCardViewModel>? OnDeleteRequested;

    public SensorCardViewModel()
    {
        BeginEditCommand = new RelayCommand(_ => BeginEdit());
        SaveEditCommand = new RelayCommand(_ => SaveEdit());
        CancelEditCommand = new RelayCommand(_ => CancelEdit());
        CopyMacCommand = new RelayCommand(_ => CopyMacToClipboard());
        DeleteSensorCommand = new RelayCommand(_ => OnDeleteRequested?.Invoke(this));
    }

    public string ShortMacAddress
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_macAddress)) return "";
            if (_macAddress.Length >= 8)
            {
                return _macAddress[^8..]; // Returns last 6 hex chars e.g. "2A:93:65"
            }
            return _macAddress;
        }
    }

    private string _macTooltipText = string.Empty;
    public string MacTooltipText
    {
        get => !string.IsNullOrEmpty(_macTooltipText) ? _macTooltipText : $"Click to copy full MAC ({_macAddress})";
        set => SetField(ref _macTooltipText, value);
    }

    private async void CopyMacToClipboard()
    {
        try
        {
            if (!string.IsNullOrEmpty(_macAddress))
            {
                System.Windows.Clipboard.SetText(_macAddress);
                MacTooltipText = "✓ Copied to clipboard!";
                await System.Threading.Tasks.Task.Delay(1800);
                MacTooltipText = $"Click to copy full MAC ({_macAddress})";
            }
        }
        catch { }
    }

    public string MacAddress
    {
        get => _macAddress;
        set
        {
            if (SetField(ref _macAddress, value))
            {
                OnPropertyChanged(nameof(ShortMacAddress));
                OnPropertyChanged(nameof(MacTooltipText));
            }
        }
    }

    public string DeviceName
    {
        get => _deviceName;
        set
        {
            if (SetField(ref _deviceName, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string FriendlyName
    {
        get => _friendlyName;
        set
        {
            if (SetField(ref _friendlyName, value))
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(HasCustomName));
            }
        }
    }

    public string DisplayName
    {
        get => !string.IsNullOrWhiteSpace(_friendlyName) ? _friendlyName : _deviceName;
        set { }
    }
    public bool HasCustomName
    {
        get => !string.IsNullOrWhiteSpace(_friendlyName);
        set { }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set => SetField(ref _isEditing, value);
    }

    public string EditNameText
    {
        get => _editNameText;
        set => SetField(ref _editNameText, value);
    }

    public bool IsAwaitingBeacon
    {
        get => _isAwaitingBeacon;
        set => SetField(ref _isAwaitingBeacon, value);
    }

    public string EditDisplayType
    {
        get => _editDisplayType;
        set
        {
            if (SetField(ref _editDisplayType, value))
            {
                OnPropertyChanged(nameof(IsAutoDisplayTypeSelected));
                OnPropertyChanged(nameof(IsEInkDisplayTypeSelected));
                OnPropertyChanged(nameof(IsLcdDisplayTypeSelected));
                OnPropertyChanged(nameof(IsPuckDisplayTypeSelected));
            }
        }
    }

    public bool IsAutoDisplayTypeSelected => string.Equals(_editDisplayType, "Auto", StringComparison.OrdinalIgnoreCase);
    public bool IsEInkDisplayTypeSelected => string.Equals(_editDisplayType, "E-Ink", StringComparison.OrdinalIgnoreCase);
    public bool IsLcdDisplayTypeSelected => string.Equals(_editDisplayType, "LCD", StringComparison.OrdinalIgnoreCase);
    public bool IsPuckDisplayTypeSelected => string.Equals(_editDisplayType, "Puck", StringComparison.OrdinalIgnoreCase);

    public ICommand SetDisplayTypeCommand => new RelayCommand(p =>
    {
        if (p is string type)
        {
            EditDisplayType = type;
        }
    });

    public void BeginEdit()
    {
        EditNameText = !string.IsNullOrWhiteSpace(_friendlyName) ? _friendlyName : _deviceName;
        EditDisplayType = !string.IsNullOrWhiteSpace(_displayType) ? _displayType : "Auto";
        EditGroupName = !string.IsNullOrWhiteSpace(_groupName) ? _groupName : (GroupName ?? "");
        IsEditing = true;
    }

    public void SaveEdit()
    {
        FriendlyName = EditNameText.Trim();
        DisplayType = string.Equals(EditDisplayType, "Auto", StringComparison.OrdinalIgnoreCase) ? null : EditDisplayType;
        GroupName = string.IsNullOrWhiteSpace(EditGroupName) || string.Equals(EditGroupName.Trim(), "Other", StringComparison.OrdinalIgnoreCase) ? null : EditGroupName.Trim();
        IsEditing = false;
        OnConfigChanged?.Invoke(this);
    }

    public void CancelEdit()
    {
        EditNameText = string.Empty;
        EditDisplayType = "Auto";
        EditGroupName = string.Empty;
        IsEditing = false;
    }

    public string Protocol
    {
        get => _protocol;
        set
        {
            if (SetField(ref _protocol, value))
            {
                OnPropertyChanged(nameof(FooterTimestampAndProtocolDisplay));
            }
        }
    }

    public double? TemperatureC
    {
        get => _temperatureC;
        set
        {
            if (SetField(ref _temperatureC, value))
            {
                OnPropertyChanged(nameof(TemperatureF));
                OnPropertyChanged(nameof(TemperatureDisplay));
                OnPropertyChanged(nameof(TemperatureAltDisplay));
                OnPropertyChanged(nameof(TemperatureBrush));
                OnPropertyChanged(nameof(TemperatureGlowBrush));
            }
        }
    }

    public double? TemperatureF => _temperatureC.HasValue ? Math.Round(_temperatureC.Value * 9.0 / 5.0 + 32.0, 1) : null;

    public string TemperatureDisplay
    {
        get => TemperatureF.HasValue ? $"{TemperatureF:F1}°F" : "--.-°F";
        set { }
    }
    public string TemperatureAltDisplay
    {
        get => TemperatureC.HasValue ? $"{TemperatureC:F1}°C" : "--.-°C";
        set { }
    }

    public Brush TemperatureBrush
    {
        get
        {
            if (!_temperatureC.HasValue) return new SolidColorBrush(Color.FromRgb(148, 163, 184)); // Slate #94A3B8
            var f = TemperatureF ?? 70.0;
            if (f >= 78.0) return new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Soft Fox Amber (Hot)
            if (f <= 62.0) return new SolidColorBrush(Color.FromRgb(56, 189, 248)); // Soft Glacial Sky (Cold)
            return new SolidColorBrush(Color.FromRgb(248, 250, 252)); // Crisp Titanium White #F8FAFC (Comfort)
        }
    }

    public Brush TemperatureGlowBrush
    {
        get
        {
            var f = TemperatureF ?? 70.0;
            if (f >= 78.0) return new SolidColorBrush(Color.FromArgb(30, 245, 158, 11));
            if (f <= 62.0) return new SolidColorBrush(Color.FromArgb(30, 56, 189, 248));
            return new SolidColorBrush(Color.FromArgb(20, 248, 250, 252));
        }
    }

    public double? HumidityPercent
    {
        get => _humidityPercent;
        set
        {
            if (SetField(ref _humidityPercent, value))
            {
                OnPropertyChanged(nameof(HumidityDisplay));
                OnPropertyChanged(nameof(HumidityProgress));
            }
        }
    }

    public string HumidityDisplay
    {
        get => _humidityPercent.HasValue ? $"{_humidityPercent:F0}%" : "--%";
        set { }
    }
    public double HumidityProgress
    {
        get => _humidityPercent ?? 0.0;
        set { }
    }

    public int? BatteryPercent
    {
        get => _batteryPercent;
        set
        {
            if (SetField(ref _batteryPercent, value))
            {
                OnPropertyChanged(nameof(BatteryDisplay));
                OnPropertyChanged(nameof(BatteryBrush));
                OnPropertyChanged(nameof(BatteryFillWidth));
            }
        }
    }

    public string BatteryDisplay
    {
        get => _batteryPercent.HasValue ? $"{_batteryPercent}%" : "N/A";
        set { }
    }

    public double BatteryFillWidth
    {
        get
        {
            if (!_batteryPercent.HasValue || _batteryPercent.Value <= 0) return 0.0;
            double pct = Math.Clamp(_batteryPercent.Value, 0, 100) / 100.0;
            return Math.Max(2.0, pct * 12.0);
        }
        set { }
    }

    public Brush BatteryBrush
    {
        get
        {
            if (!_batteryPercent.HasValue) return new SolidColorBrush(Color.FromRgb(148, 163, 184));
            if (_batteryPercent.Value > 30) return new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Soft Sage #10B981
            if (_batteryPercent.Value > 15) return new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Fox Amber #F59E0B
            return new SolidColorBrush(Color.FromRgb(248, 113, 113)); // Soft Coral Red
        }
    }

    public string BatteryHealthNote
    {
        get
        {
            if (!_batteryPercent.HasValue) return "Awaiting state";
            if (_batteryPercent.Value > 70) return "Healthy level";
            if (_batteryPercent.Value > 25) return "Normal";
            return "Low Battery";
        }
        set { }
    }

    public short Rssi
    {
        get => _rssi;
        set
        {
            if (SetField(ref _rssi, value))
            {
                OnPropertyChanged(nameof(RssiDisplay));
                OnPropertyChanged(nameof(SignalQuality));
                OnPropertyChanged(nameof(SignalBars));
                OnPropertyChanged(nameof(SignalColorBrush));
                OnPropertyChanged(nameof(SignalBar1Brush));
                OnPropertyChanged(nameof(SignalBar2Brush));
                OnPropertyChanged(nameof(SignalBar3Brush));
                OnPropertyChanged(nameof(SignalBar4Brush));
            }
        }
    }

    public string RssiDisplay
    {
        get => $"{_rssi} dBm";
        set { }
    }

    public string SignalQuality
    {
        get => _rssi switch
        {
            >= -60 => "Excellent",
            >= -75 => "Good",
            >= -85 => "Fair",
            _ => "Weak"
        };
        set { }
    }

    public string SignalBars
    {
        get => _rssi switch
        {
            >= -60 => "■■■■",
            >= -75 => "■■■□",
            >= -85 => "■■□□",
            _ => "■□□□"
        };
        set { }
    }

    public Brush SignalColorBrush => _rssi switch
    {
        >= -65 => new SolidColorBrush(Color.FromRgb(52, 211, 153)), // Green
        >= -80 => new SolidColorBrush(Color.FromRgb(56, 189, 248)), // Cyan
        >= -90 => new SolidColorBrush(Color.FromRgb(251, 191, 36)), // Yellow
        _ => new SolidColorBrush(Color.FromRgb(248, 113, 113))       // Red
    };

    private static readonly Brush DimSignalBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));

    public Brush SignalBar1Brush => _rssi switch
    {
        >= -115 => SignalColorBrush,
        _ => DimSignalBrush
    };

    public Brush SignalBar2Brush => _rssi switch
    {
        >= -90 => SignalColorBrush,
        _ => DimSignalBrush
    };

    public Brush SignalBar3Brush => _rssi switch
    {
        >= -75 => SignalColorBrush,
        _ => DimSignalBrush
    };

    public Brush SignalBar4Brush => _rssi switch
    {
        >= -60 => SignalColorBrush,
        _ => DimSignalBrush
    };

    public DateTime LastSeenUtc
    {
        get => _lastSeenUtc;
        set
        {
            if (SetField(ref _lastSeenUtc, value))
            {
                OnPropertyChanged(nameof(LastSeenDisplay));
                OnPropertyChanged(nameof(LastUpdatedTimeDisplay));
                OnPropertyChanged(nameof(LastUpdatedFullDisplay));
                OnPropertyChanged(nameof(FooterTimestampAndProtocolDisplay));
                OnPropertyChanged(nameof(IsLiveIndicator));
            }
        }
    }

    public bool IsLiveIndicator => !_isAwaitingBeacon && (DateTime.UtcNow - _lastSeenUtc).TotalSeconds < 15;

    public string LastUpdatedTimeDisplay
    {
        get
        {
            if (_lastSeenUtc == DateTime.MinValue) return "--:--:--";
            return _lastSeenUtc.ToLocalTime().ToString("h:mm:ss tt");
        }
        set { }
    }

    public string LastSeenDisplay
    {
        get
        {
            if (_isAwaitingBeacon) return "Cached (idle)";
            var diff = DateTime.UtcNow - _lastSeenUtc;
            if (diff.TotalSeconds < 3) return "Just now";
            if (diff.TotalSeconds < 60) return $"{Math.Floor(diff.TotalSeconds)}s ago";
            if (diff.TotalMinutes < 60) return $"{Math.Floor(diff.TotalMinutes)}m ago";
            return $"{Math.Floor(diff.TotalHours)}h ago";
        }
        set { }
    }

    public string LastUpdatedFullDisplay
    {
        get
        {
            if (_lastSeenUtc == DateTime.MinValue) return "Awaiting beacon";
            if (_isAwaitingBeacon) return $"{LastUpdatedTimeDisplay} • Cached";
            return $"{LastUpdatedTimeDisplay} ({LastSeenDisplay})";
        }
        set { }
    }

    public string FooterTimestampAndProtocolDisplay
    {
        get
        {
            string proto = !string.IsNullOrWhiteSpace(_protocol) ? _protocol : "BTHome v2";
            string modelPart = !string.IsNullOrWhiteSpace(_hardwareModel) ? $" • {_hardwareModel}" : "";
            if (_lastSeenUtc == DateTime.MinValue) return $"Awaiting beacon • {proto}{modelPart}";
            if (_isAwaitingBeacon) return $"{LastUpdatedTimeDisplay} • Cached • {proto}{modelPart}";
            return $"{LastUpdatedTimeDisplay} • {proto}{modelPart}";
        }
        set { }
    }

    public void RefreshTimeDisplay()
    {
        OnPropertyChanged(nameof(LastSeenDisplay));
        OnPropertyChanged(nameof(LastUpdatedFullDisplay));
        OnPropertyChanged(nameof(FooterTimestampAndProtocolDisplay));
        OnPropertyChanged(nameof(IsLiveIndicator));
    }

    private static bool IsGenericName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        return name is "Unknown" or "BTHome Device" or "ATC/PVVX Sensor" or "Xiaomi Sensor" or "Bluetooth BLE Relay" or "Shelly BLU H&T" or "Door / Window Sensor";
    }

    public void UpdateFromReading(BleReading reading)
    {
        MacAddress = reading.MacAddress;
        if (!string.IsNullOrWhiteSpace(reading.HardwareModel))
        {
            HardwareModel = reading.HardwareModel;
        }
        if (!string.IsNullOrWhiteSpace(reading.DisplayType))
        {
            DisplayType = reading.DisplayType;
        }
        bool isEnvironmental = TemperatureC.HasValue || HumidityPercent.HasValue || DeviceCategory == "Environmental" ||
            (!string.IsNullOrEmpty(HardwareModel) && HardwareModel.StartsWith("SBHT", StringComparison.OrdinalIgnoreCase)) ||
            DisplayType == "E-Ink" || DisplayType == "Puck";

        // Prevent contact sensors from holding a stray display type
        if ((reading.IsOpen.HasValue || reading.DeviceCategory == "Contact") &&
            !reading.TemperatureC.HasValue && !reading.HumidityPercent.HasValue &&
            !TemperatureC.HasValue && !HumidityPercent.HasValue)
        {
            isEnvironmental = false;
            DisplayType = null;
            _displayType = null;
            DeviceCategory = "Contact";
        }

        if (!string.IsNullOrWhiteSpace(reading.DeviceName) && reading.DeviceName != "Unknown")
        {
            // Do NOT let generic button names hijack an environmental sensor
            if (!(isEnvironmental && (reading.DeviceName == "Shelly BLU Button" || reading.DeviceName == "Button")))
            {
                if (!IsGenericName(reading.DeviceName) || IsGenericName(DeviceName))
                {
                    DeviceName = reading.DeviceName;
                }
            }
        }
        Protocol = reading.Protocol;
        if (reading.TemperatureC.HasValue) TemperatureC = reading.TemperatureC;
        if (reading.HumidityPercent.HasValue) HumidityPercent = reading.HumidityPercent;
        if (reading.BatteryPercent.HasValue) BatteryPercent = reading.BatteryPercent;
        if (reading.IsOpen.HasValue) IsOpen = reading.IsOpen;
        if (reading.IlluminanceLux.HasValue) IlluminanceLux = reading.IlluminanceLux;
        if (reading.RotationDegrees.HasValue) RotationDegrees = reading.RotationDegrees;
        if (reading.IsMotion.HasValue) IsMotion = reading.IsMotion;
        if (!string.IsNullOrEmpty(reading.ButtonEventName)) ButtonEventName = reading.ButtonEventName;
        if (reading.IsEncrypted) IsEncrypted = true;

        if (!string.IsNullOrEmpty(reading.DeviceCategory))
        {
            // Do NOT let an auxiliary button packet demote an environmental sensor to a button!
            if (!(isEnvironmental && reading.DeviceCategory == "Button"))
            {
                DeviceCategory = reading.DeviceCategory;
            }
        }
        Rssi = reading.Rssi;
        LastSeenUtc = reading.LastSeenUtc;
        IsAwaitingBeacon = false;
        NotifyModeProperties();
    }

    public StoredSensorConfig ToConfig()
    {
        return new StoredSensorConfig
        {
            MacAddress = MacAddress,
            FriendlyName = FriendlyName,
            Protocol = Protocol,
            LastTemperatureC = TemperatureC,
            LastHumidityPercent = HumidityPercent,
            LastBatteryPercent = BatteryPercent,
            LastRssi = Rssi,
            LastSeenUtc = LastSeenUtc,
            IsSyncEnabled = IsSyncEnabled,
            HardwareModel = HardwareModel,
            HasDisplay = HasDisplay,
            DisplayType = DisplayType,
            IsIgnored = IsIgnored,
            GroupName = _groupName,
            LastIsOpen = IsOpen,
            LastIlluminanceLux = IlluminanceLux,
            LastRotationDegrees = RotationDegrees,
            LastIsMotion = IsMotion,
            LastButtonEvent = ButtonEventName,
            IsEncrypted = IsEncrypted,
            DeviceCategory = DeviceCategory
        };
    }

    public static SensorCardViewModel FromConfig(StoredSensorConfig config)
    {
        string cat = config.DeviceCategory ?? string.Empty;
        bool isEncrypted = config.IsEncrypted || config.Protocol.Contains("Encrypted", StringComparison.OrdinalIgnoreCase);

        // Explicit contact sensor check (open/closed, lux, Window/Door group, without temp/humidity)
        bool isExplicitContact = (config.LastIsOpen.HasValue || cat == "Contact" || config.GroupName == "Window" || config.GroupName == "Door") &&
                                 !config.LastTemperatureC.HasValue && !config.LastHumidityPercent.HasValue;

        bool isKnownEnvironmental = !isExplicitContact && (config.LastTemperatureC.HasValue || config.LastHumidityPercent.HasValue ||
            config.HardwareModel == "SBHT-103C" || config.HardwareModel == "SBHT-003C" ||
            config.DisplayType == "E-Ink" || config.DisplayType == "Puck" || cat == "Environmental");

        bool isLikelyContact = isExplicitContact || (!isKnownEnvironmental && (config.LastIsOpen.HasValue ||
            cat == "Contact" ||
            (!config.LastTemperatureC.HasValue && !config.LastHumidityPercent.HasValue && config.MacAddress.StartsWith("94:02:16", StringComparison.OrdinalIgnoreCase))));

        bool isLikelyMotion = !isKnownEnvironmental && !isLikelyContact && (config.LastIsMotion.HasValue || cat == "Motion");
        bool isLikelyButton = !isKnownEnvironmental && !isLikelyContact && (!string.IsNullOrEmpty(config.LastButtonEvent) || cat == "Button");

        bool isShellyMac = config.MacAddress.StartsWith("94:B2:16", StringComparison.OrdinalIgnoreCase) || config.MacAddress.StartsWith("94:02:16", StringComparison.OrdinalIgnoreCase);
        bool isEInk = isKnownEnvironmental && (config.DisplayType == "E-Ink" || config.HardwareModel == "SBHT-103C" || config.HasDisplay == true);
        bool isPuck = isKnownEnvironmental && (config.DisplayType == "Puck" || config.HardwareModel == "SBHT-003C" || config.HasDisplay == false);

        string defaultName;
        if (isEncrypted) defaultName = "Shelly BLU (Encrypted)";
        else if (isEInk && isShellyMac) defaultName = "Shelly BLU H&T Display";
        else if (isPuck && isShellyMac) defaultName = "Shelly BLU H&T Puck";
        else if (config.HardwareModel == "SBHT-103C") defaultName = "Shelly BLU H&T Display";
        else if (config.HardwareModel == "SBHT-003C") defaultName = "Shelly BLU H&T Puck";
        else if (isLikelyContact) defaultName = "Door / Window Sensor";
        else if (isLikelyMotion) defaultName = "Shelly BLU Motion";
        else if (isLikelyButton) defaultName = "Shelly BLU Button";
        else if (config.HardwareModel == "MHO-C401") defaultName = "Miaomiaoce E-Ink (C401)";
        else if (config.HardwareModel == "LYWSD02MMC") defaultName = "Xiaomi E-Ink Clock";
        else if (config.HardwareModel == "LYWSD03MMC") defaultName = "Xiaomi Mijia LCD";
        else if (!config.LastTemperatureC.HasValue && !config.LastHumidityPercent.HasValue) defaultName = "Bluetooth BLE Relay";
        else defaultName = "BTHome Device";

        // Auto-heal corrupted names if an H&T sensor was mistakenly named "Shelly BLU Button"
        string resolvedName = config.FriendlyName;
        if (isKnownEnvironmental && (resolvedName == "Shelly BLU Button" || resolvedName == "Button"))
        {
            resolvedName = defaultName;
        }
        else if (string.IsNullOrWhiteSpace(resolvedName))
        {
            resolvedName = defaultName;
        }

        if (string.IsNullOrEmpty(cat) || (isKnownEnvironmental && cat == "Button") || (isLikelyContact && cat != "Contact"))
        {
            if (isEncrypted) cat = "Encrypted";
            else if (isLikelyContact) cat = "Contact";
            else if (isKnownEnvironmental) cat = "Environmental";
            else if (isLikelyMotion) cat = "Motion";
            else if (isLikelyButton) cat = "Button";
            else cat = "PassThrough";
        }

        // Auto-heal dirty display type for contact/motion/button sensors
        string? resolvedDisplayType = isLikelyContact || isLikelyMotion || isLikelyButton ? null : config.DisplayType;

        var vm = new SensorCardViewModel
        {
            MacAddress = config.MacAddress,
            DeviceName = resolvedName,
            FriendlyName = resolvedName == defaultName ? string.Empty : resolvedName,
            Protocol = config.Protocol,
            TemperatureC = config.LastTemperatureC,
            HumidityPercent = config.LastHumidityPercent,
            BatteryPercent = config.LastBatteryPercent,
            IsOpen = config.LastIsOpen ?? (isLikelyContact ? (bool?)false : null),
            IlluminanceLux = config.LastIlluminanceLux,
            RotationDegrees = config.LastRotationDegrees,
            IsMotion = config.LastIsMotion,
            ButtonEventName = config.LastButtonEvent,
            IsEncrypted = isEncrypted,
            DeviceCategory = cat,
            Rssi = config.LastRssi,
            LastSeenUtc = config.LastSeenUtc,
            IsSyncEnabled = config.IsSyncEnabled,
            HardwareModel = isLikelyContact ? null : config.HardwareModel,
            HasDisplay = isLikelyContact ? null : config.HasDisplay,
            DisplayType = resolvedDisplayType,
            IsIgnored = config.IsIgnored,
            GroupName = config.GroupName,
            IsAwaitingBeacon = true
        };
        return vm;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
