using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Orientation = System.Windows.Controls.Orientation;
using AirFoxBT.Engine;
using AirFoxBT.Models;
using AirFoxBT.Services;
using AirFoxBT.ViewModels;

namespace AirFoxBT;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtr", SetLastError = true)]
    private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetClassLong", SetLastError = true)]
    private static extern IntPtr SetClassLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8 ? SetClassLongPtr64(hWnd, nIndex, dwNewLong) : SetClassLong32(hWnd, nIndex, dwNewLong);
    }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_DLGMODALFRAME = 0x0001;
    private const uint WM_GETICON = 0x007F;
    private const uint WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const int GCLP_HICON = -14;
    private const int GCLP_HICONSM = -34;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private readonly BleWatcherEngine _engine;
    private readonly SensorStorageService _storageService;
    private readonly LocalApiService _apiService;
    private readonly EsphomeProxyServer _esphomeServer;
    private readonly MdnsService _mdnsService;
    private readonly BatteryAlertService _batteryAlertService;
    private bool _isUpdatingBatteryUi = false;
    private AppSettings _settings;
    private readonly ObservableCollection<SensorCardViewModel> _sensors = new();
    private readonly ConcurrentDictionary<string, SensorCardViewModel> _sensorMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _relativeTimeTimer;
    private readonly DispatcherTimer _saveDebounceTimer;
    private readonly DispatcherTimer _nameDebounceTimer;
    private long _totalPackets = 0;
    private bool _hasShownTrayNotice = false;
    private bool _isExplicitExit = false;
    private bool _isDrawerCollapsed = false;
    private ScrollViewer? _logScrollViewer;
    private ICollectionView? _sensorsView;
    private string _searchText = string.Empty;
    private string _activeGroupFilter = "All";

    private Action? _onModalPrimaryAction;
    private Action? _onModalSecondaryAction;
    private GitHubRelease? _latestAvailableRelease;

    public MainWindow()
    {
        InitializeComponent();

        _storageService = new SensorStorageService();
        _settings = _storageService.LoadSettings();

        _batteryAlertService = new BatteryAlertService(
            _settings.BatteryAlertPreset,
            _settings.BatteryThresholdPercent,
            _settings.BatteryCooldownHours,
            _settings.BatteryDebouncePackets);
        _batteryAlertService.OnLog += msg => AppendLog($"[{DateTime.Now:HH:mm:ss}] {msg}");

        _sensorsView = CollectionViewSource.GetDefaultView(_sensors);
        _sensorsView.Filter = FilterSensorPredicate;
        SensorCardsList.ItemsSource = _sensorsView;

        // Load pre-existing cached sensors from %AppData% so the grid is populated immediately on launch
        LoadCachedSensors();
        UpdateGroupFilterChips();
        UpdateResultsCount();

        _engine = new BleWatcherEngine();
        _engine.OnReadingReceived += OnReadingReceived;
        _engine.OnStatusChanged += OnStatusChanged;
        _engine.OnError += OnError;

        // Native ESPHome Bluetooth Proxy (TCP port 6053) for zero-config Home Assistant integration
        _esphomeServer = new EsphomeProxyServer(6053);
        var (initialSlug, initialFriendly) = DeviceNaming.ResolveNames(_settings.DeviceName);
        _esphomeServer.NodeName = initialSlug;
        _esphomeServer.FriendlyName = initialFriendly;

        var initialPreset = ThrottleSettings.ParsePreset(_settings.ThrottleMode);
        _esphomeServer.Throttle = ThrottleSettings.FromPreset(initialPreset);

        _esphomeServer.OnLog += msg => AppendLog($"[{DateTime.Now:HH:mm:ss}] {msg}");
        _esphomeServer.OnClientCountChanged += count => Dispatcher.InvokeAsync(() =>
        {
            TxtEsphomeStatus.Text = count > 0 ? $":6053 ({count} HA)" : ":6053";
        });
        _esphomeServer.Start();

        // Forward raw BLE advertisement packets directly to Home Assistant
        _engine.OnRawAdvertisementReceived += (mac, rssi, type, data) =>
        {
            _esphomeServer.BroadcastAdvertisement(mac, rssi, type, data);

            // Print full raw hex dump of any captured Time Beacon frames directly to the Ingestion Stream log
            for (int i = 0; i <= data.Length - 5; i++)
            {
                if (data[i] == 0x0D)
                {
                    uint potentialUnix = BitConverter.ToUInt32(data, i + 1);
                    if (potentialUnix >= 1700000000 && potentialUnix <= 1900000000)
                    {
                        var macBytes = BitConverter.GetBytes(mac).Take(6).Reverse().ToArray();
                        string macStr = string.Join(":", macBytes.Select(b => b.ToString("X2")));
                        string hex = BitConverter.ToString(data);
                        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🎯 [RAW TIME BEACON] {macStr} ({data.Length}B) -> {hex}");
                        break;
                    }
                }
            }
        };

        // Outbound zero-collision mDNS announcement for ESPHome discovery (_esphomelib._tcp.local)
        _mdnsService = new MdnsService { DeviceName = _settings.DeviceName };
        _mdnsService.OnLog += msg => AppendLog($"[{DateTime.Now:HH:mm:ss}] {msg}");
        if (!string.IsNullOrEmpty(_settings.SelectedHostIp) && _settings.SelectedHostIp != "Auto")
        {
            _mdnsService.RestartWithIp(_settings.SelectedHostIp, _settings.DeviceName);
        }
        else
        {
            _mdnsService.Start();
        }

        // Initialize Embedded HTTP Telemetry API server (port 8766)
        _apiService = new LocalApiService(
            () => _sensors.Select(s => s.ToConfig()).ToList(),
            () => _totalPackets
        );
        _apiService.OnLog += msg => AppendLog($"[{DateTime.Now:HH:mm:ss}] {msg}");
        _apiService.Start(_settings.SelectedHostIp);

        // Populate device name and network interfaces in Settings
        TxtDeviceName.Text = string.IsNullOrWhiteSpace(_settings.DeviceName) ? Environment.MachineName : _settings.DeviceName;
        _nameDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _nameDebounceTimer.Tick += (s, e) =>
        {
            _nameDebounceTimer.Stop();
            ApplyDeviceNameChange();
        };

        PopulateNetworkInterfaces(reAnnounce: false);
        try
        {
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        }
        catch { }

        // 1-second live ticker for smooth "Xs ago" updates and Tray synchronization
        _relativeTimeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _relativeTimeTimer.Tick += (s, e) =>
        {
            foreach (var sensor in _sensors)
            {
                sensor.RefreshTimeDisplay();
            }
            long ingested = _esphomeServer.TotalIngestedAdv;
            long dropped = _esphomeServer.TotalDroppedAdv;
            if (ingested > 0)
            {
                double dropPct = (dropped * 100.0) / ingested;
                TxtFilterStats.Text = $" • {dropPct:F0}% drop";
            }
            App.UpdateTrayStatus(_sensors.Count, _totalPackets);
        };
        _relativeTimeTimer.Start();

        _saveDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _saveDebounceTimer.Tick += (s, e) => SaveAllSensors();
        _saveDebounceTimer.Start();

        // Bridge LoggingService messages to GUI log terminal
        LoggingService.Instance.OnLog += (level, src, msg) =>
        {
            AppendLog($"[{DateTime.Now:HH:mm:ss}] [{src}] {msg}");
        };

        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            ApplyDarkThemeWindowBar(hwnd);
            ApplyWindowIcon(hwnd);
        }
    }

    private void ApplyWindowIcon(IntPtr hwnd)
    {
        try
        {
            var pngUri = new Uri("pack://application:,,,/AirFoxBT;component/assets/logo.png", UriKind.Absolute);
            var streamInfo = System.Windows.Application.GetResourceStream(pngUri);
            if (streamInfo != null)
            {
                using var stream = streamInfo.Stream;
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                byte[] pngBytes = ms.ToArray();

                // Set WPF Window.Icon
                using (var wpfMs = new MemoryStream(pngBytes))
                {
                    var frame = System.Windows.Media.Imaging.BitmapFrame.Create(
                        wpfMs,
                        System.Windows.Media.Imaging.BitmapCreateOptions.None,
                        System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    Icon = frame;
                }

                // Set Win32 Taskbar and Window Caption HICONs (48x48 big, 16x16 small)
                using var bmpMs = new MemoryStream(pngBytes);
                using var bmp = new System.Drawing.Bitmap(bmpMs);

                using var bigBmp = new System.Drawing.Bitmap(bmp, new System.Drawing.Size(48, 48));
                IntPtr hBig = bigBmp.GetHicon();
                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, hBig);

                using var smallBmp = new System.Drawing.Bitmap(bmp, new System.Drawing.Size(16, 16));
                IntPtr hSmall = smallBmp.GetHicon();
                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, hSmall);
            }
        }
        catch { }
    }

    private static int ToColorRef(byte r, byte g, byte b) => (b << 16) | (g << 8) | r;

    private static void ApplyDarkThemeWindowBar(IntPtr hwnd)
    {
        try
        {
            int useDarkMode = 1;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));
            }

            // Nocturnal Hunter Header Background: #0B132B (Midnight Navy)
            int captionColor = ToColorRef(11, 19, 43);
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));

            // Crisp Titanium White Title Text: #F8FAFC
            int textColor = ToColorRef(248, 250, 252);
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));

            // Nocturnal Slate Border: #1E2D48
            int borderColor = ToColorRef(30, 45, 72);
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
        }
        catch { }
    }

    private void LoadCachedSensors()
    {
        var cached = _storageService.LoadSensors();
        foreach (var kvp in cached)
        {
            var vm = SensorCardViewModel.FromConfig(kvp.Value);
            vm.OnConfigChanged += OnSensorConfigChanged;
            vm.OnDeleteRequested = ConfirmAndDeleteSensor;
            _sensorMap[vm.MacAddress] = vm;
            _sensors.Add(vm);

            if (vm.IsIgnored)
            {
                _esphomeServer.SetMacIgnored(vm.MacAddress, true);
            }
        }

        TxtSensorsCount.Text = $"{_sensors.Count} Sensor{(_sensors.Count == 1 ? "" : "s")}";
        App.UpdateTrayStatus(_sensors.Count, _totalPackets);
    }

    private void OnSensorConfigChanged(SensorCardViewModel vm)
    {
        if (_settings.AutoSyncNamedSensors && vm.HasCustomName && !vm.IsSyncEnabled)
        {
            vm.IsSyncEnabled = true;
        }

        _esphomeServer.SetMacIgnored(vm.MacAddress, vm.IsIgnored);

        SaveAllSensors();

        Dispatcher.InvokeAsync(() =>
        {
            UpdateGroupFilterChips();
            _sensorsView?.Refresh();
            UpdateResultsCount();
        });

        string relayStatus = vm.IsIgnored ? "IGNORED (blocked from :6053)" : "RELAYED (:6053 active)";
        AppendLog($"[{DateTime.Now:HH:mm:ss}] [CONFIG] {vm.MacAddress} (\"{vm.DisplayName}\") [{vm.GroupDisplay}]: {relayStatus}");
    }

    private void SaveAllSensors()
    {
        var configs = _sensors.Select(s => s.ToConfig()).ToList();
        _storageService.SaveSensors(configs);
    }

    #region Sensor Management (Export, Import, Delete)

    private void ConfirmAndDeleteSensor(SensorCardViewModel vm)
    {
        ShowInAppModal(
            title: "Delete Sensor",
            message: $"Are you sure you want to delete \"{vm.DisplayName}\" ({vm.MacAddress})?\n\nThis will remove its stored configuration, custom name, and card.",
            icon: "🗑️",
            primaryButtonText: "Delete",
            onPrimary: () => RemoveSensor(vm),
            secondaryButtonText: "Cancel"
        );
    }

    private void RemoveSensor(SensorCardViewModel vm)
    {
        _sensors.Remove(vm);
        _sensorMap.TryRemove(vm.MacAddress, out _);
        SaveAllSensors();

        TxtSensorsCount.Text = $"{_sensors.Count} Sensor{(_sensors.Count == 1 ? "" : "s")}";
        UpdateGroupFilterChips();
        _sensorsView?.Refresh();
        UpdateResultsCount();
        App.UpdateTrayStatus(_sensors.Count, _totalPackets);

        AppendLog($"[{DateTime.Now:HH:mm:ss}] [STORAGE] Deleted sensor {vm.MacAddress} (\"{vm.DisplayName}\").");
    }

    private void BtnExportSensors_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Sensor Data Backup",
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                FileName = $"AirFoxBT_Sensors_{DateTime.Now:yyyyMMdd_HHmmss}.json"
            };

            if (sfd.ShowDialog() == true)
            {
                var configs = _sensors.Select(s => s.ToConfig()).ToList();
                _storageService.ExportSensors(sfd.FileName, configs);

                AppendLog($"[{DateTime.Now:HH:mm:ss}] [BACKUP] Exported {configs.Count} sensor configurations to {System.IO.Path.GetFileName(sfd.FileName)}");

                ShowInAppModal(
                    title: "Export Complete",
                    message: $"Successfully exported {configs.Count} sensor configuration{(configs.Count == 1 ? "" : "s")}.\n\nBackup file:\n{sfd.FileName}",
                    icon: "✓"
                );
            }
        }
        catch (Exception ex)
        {
            ShowInAppModal(
                title: "Export Failed",
                message: $"Failed to export sensors: {ex.Message}",
                icon: "⚠️"
            );
        }
    }

    private void BtnImportSensors_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import Sensor Data Backup",
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*"
            };

            if (ofd.ShowDialog() == true)
            {
                var imported = _storageService.ImportSensors(ofd.FileName);
                if (imported.Count == 0)
                {
                    ShowInAppModal("Import", "No sensor records were found in the selected file.", icon: "ℹ️");
                    return;
                }

                int addedCount = 0;
                int updatedCount = 0;

                foreach (var cfg in imported)
                {
                    if (string.IsNullOrWhiteSpace(cfg.MacAddress)) continue;

                    if (_sensorMap.TryGetValue(cfg.MacAddress, out var existing))
                    {
                        if (!string.IsNullOrWhiteSpace(cfg.FriendlyName)) existing.FriendlyName = cfg.FriendlyName;
                        if (!string.IsNullOrWhiteSpace(cfg.GroupName)) existing.GroupName = cfg.GroupName;
                        if (!string.IsNullOrWhiteSpace(cfg.DisplayType)) existing.DisplayType = cfg.DisplayType;
                        existing.IsIgnored = cfg.IsIgnored;
                        existing.IsSyncEnabled = cfg.IsSyncEnabled;
                        updatedCount++;
                    }
                    else
                    {
                        var vm = SensorCardViewModel.FromConfig(cfg);
                        vm.OnConfigChanged += OnSensorConfigChanged;
                        vm.OnDeleteRequested = ConfirmAndDeleteSensor;
                        _sensorMap[vm.MacAddress] = vm;
                        _sensors.Add(vm);
                        addedCount++;
                    }
                }

                SaveAllSensors();
                TxtSensorsCount.Text = $"{_sensors.Count} Sensor{(_sensors.Count == 1 ? "" : "s")}";
                UpdateGroupFilterChips();
                _sensorsView?.Refresh();
                UpdateResultsCount();
                App.UpdateTrayStatus(_sensors.Count, _totalPackets);

                AppendLog($"[{DateTime.Now:HH:mm:ss}] [BACKUP] Imported {imported.Count} sensor configurations ({addedCount} new, {updatedCount} updated).");

                ShowInAppModal(
                    title: "Import Complete",
                    message: $"Successfully imported {imported.Count} sensor configurations ({addedCount} added, {updatedCount} updated).",
                    icon: "✓"
                );
            }
        }
        catch (Exception ex)
        {
            ShowInAppModal(
                title: "Import Failed",
                message: $"Failed to import sensors: {ex.Message}",
                icon: "⚠️"
            );
        }
    }

    private void BtnDeleteAllSensors_Click(object sender, RoutedEventArgs e)
    {
        int count = _sensors.Count;
        ShowInAppModal(
            title: "Delete All Sensors",
            message: $"Are you sure you want to delete all {count} cached sensors?\n\nThis will permanently wipe all stored custom names, room groups, and device records from memory and sensors.json. Newly received BLE packets will appear as new unconfigured devices.",
            icon: "⚠️",
            primaryButtonText: "Delete All",
            onPrimary: () => PerformDeleteAllSensors(),
            secondaryButtonText: "Cancel"
        );
    }

    private void PerformDeleteAllSensors()
    {
        int count = _sensors.Count;
        _sensors.Clear();
        _sensorMap.Clear();
        _storageService.DeleteAllSensors();

        TxtSensorsCount.Text = "0 Sensors";
        UpdateGroupFilterChips();
        _sensorsView?.Refresh();
        UpdateResultsCount();
        App.UpdateTrayStatus(0, _totalPackets);

        AppendLog($"[{DateTime.Now:HH:mm:ss}] [STORAGE] Deleted all {count} cached sensors.");
    }

    #endregion

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            ApplyDarkThemeWindowBar(hwnd);
            ApplyWindowIcon(hwnd);
        }

        if (CmbThrottleMode != null)
        {
            foreach (System.Windows.Controls.ComboBoxItem item in CmbThrottleMode.Items)
            {
                if (string.Equals(item.Tag?.ToString(), _settings.ThrottleMode, StringComparison.OrdinalIgnoreCase))
                {
                    item.IsSelected = true;
                    break;
                }
            }
        }

        // Initialize Settings controls & local SHA-256 hash display
        TxtLocalHash.Text = UpdateService.Instance.GetLocalExecutableHash();
        ToggleAutoUpdates.IsChecked = _settings.CheckForUpdates;
        ToggleStartWithWindows.IsChecked = StartupService.IsStartupEnabled();
        RefreshFirewallStatus();
        UpdateBatterySettingsUi();
        _logScrollViewer = FindScrollViewer(LstLogs);

        // Silent background update check once daily (throttled inside UpdateService)
        if (_settings.CheckForUpdates)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(4000);
                var rel = await UpdateService.Instance.CheckForUpdatesAsync(isManual: false);
                if (rel != null)
                {
                    _latestAvailableRelease = rel;
                    await Dispatcher.InvokeAsync(() =>
                    {
                        BadgeUpdateAvailable.Visibility = Visibility.Visible;
                        TxtBadgeUpdateText.Text = $"Update {rel.TagName}";
                        TxtUpdateStatus.Text = $"• Update available: {rel.TagName}";
                        TxtUpdateStatus.Foreground = (Brush)FindResource("PrimaryAccentBrush");
                        StatusDot.Fill = (Brush)FindResource("PrimaryAccentBrush");
                        TxtIntegrityBadge.Text = "Update Available";
                    });
                }
            });
        }

        _engine.Start();
    }

    #region Instant Search & Group Filtering

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = TxtSearch.Text ?? string.Empty;
        TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(_searchText) ? Visibility.Visible : Visibility.Collapsed;
        BtnClearSearch.Visibility = string.IsNullOrEmpty(_searchText) ? Visibility.Collapsed : Visibility.Visible;
        _sensorsView?.Refresh();
        UpdateResultsCount();
    }

    private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
    {
        TxtSearch.Text = string.Empty;
        TxtSearch.Focus();
    }

    private bool FilterSensorPredicate(object item)
    {
        if (item is not SensorCardViewModel vm) return false;

        // 1. Group / Ignored Filter
        if (!string.Equals(_activeGroupFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(_activeGroupFilter, "Ignored", StringComparison.OrdinalIgnoreCase))
            {
                if (!vm.IsIgnored) return false;
            }
            else
            {
                if (!string.Equals(vm.GroupDisplay, _activeGroupFilter, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        // 2. Search Text (MAC, Name, Model, Submodel, Group, Protocol)
        if (string.IsNullOrWhiteSpace(_searchText)) return true;

        string query = _searchText.Trim();

        // Match MAC address (with or without colons/hyphens: e.g. "2a8f", "2A:8F")
        string cleanQuery = query.Replace(":", "").Replace("-", "");
        string cleanMac = vm.MacAddress.Replace(":", "").Replace("-", "");
        if (cleanMac.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase)) return true;
        if (vm.MacAddress.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;

        // Match Names
        if (!string.IsNullOrEmpty(vm.DisplayName) && vm.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrEmpty(vm.FriendlyName) && vm.FriendlyName.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrEmpty(vm.DeviceName) && vm.DeviceName.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;

        // Match Hardware Model & Display Type (e.g. "SBHT-103C", "E-Ink", "Puck")
        if (!string.IsNullOrEmpty(vm.HardwareModel) && vm.HardwareModel.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrEmpty(vm.DisplayType) && vm.DisplayType.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;

        // Match Group
        if (!string.IsNullOrEmpty(vm.GroupDisplay) && vm.GroupDisplay.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;

        // Match Protocol
        if (!string.IsNullOrEmpty(vm.Protocol) && vm.Protocol.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    private void UpdateGroupFilterChips()
    {
        if (GroupChipsPanel == null) return;

        GroupChipsPanel.Children.Clear();

        // 1. "All" Chip
        bool isAllActive = string.Equals(_activeGroupFilter, "All", StringComparison.OrdinalIgnoreCase);
        GroupChipsPanel.Children.Add(CreateChipButton("All", _sensors.Count, isAllActive, isIgnored: false, () =>
        {
            _activeGroupFilter = "All";
            UpdateGroupFilterChips();
            _sensorsView?.Refresh();
            UpdateResultsCount();
        }));

        // 2. Discover distinct groups from existing sensors
        var groups = _sensors
            .Select(s => s.GroupDisplay)
            .Where(g => !string.IsNullOrWhiteSpace(g) && !string.Equals(g, "Other", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g)
            .ToList();

        if (_sensors.Any(s => string.Equals(s.GroupDisplay, "Other", StringComparison.OrdinalIgnoreCase)))
        {
            groups.Add("Other");
        }

        // If the active filter no longer exists among groups and is not "All" or "Ignored", reset to "All"
        if (!_activeGroupFilter.Equals("All", StringComparison.OrdinalIgnoreCase) &&
            !_activeGroupFilter.Equals("Ignored", StringComparison.OrdinalIgnoreCase) &&
            !groups.Contains(_activeGroupFilter, StringComparer.OrdinalIgnoreCase))
        {
            _activeGroupFilter = "All";
        }

        foreach (var grp in groups)
        {
            int count = _sensors.Count(s => string.Equals(s.GroupDisplay, grp, StringComparison.OrdinalIgnoreCase));
            bool isActive = string.Equals(_activeGroupFilter, grp, StringComparison.OrdinalIgnoreCase);
            string capturedGrp = grp;

            GroupChipsPanel.Children.Add(CreateChipButton(grp, count, isActive, isIgnored: false, () =>
            {
                _activeGroupFilter = capturedGrp;
                UpdateGroupFilterChips();
                _sensorsView?.Refresh();
                UpdateResultsCount();
            }));
        }

        // 3. "Ignored" Chip (Amber pill for non-relayed sensors)
        int ignoredCount = _sensors.Count(s => s.IsIgnored);
        if (ignoredCount > 0 || string.Equals(_activeGroupFilter, "Ignored", StringComparison.OrdinalIgnoreCase))
        {
            bool isIgnoredActive = string.Equals(_activeGroupFilter, "Ignored", StringComparison.OrdinalIgnoreCase);
            GroupChipsPanel.Children.Add(CreateChipButton("Ignored", ignoredCount, isIgnoredActive, isIgnored: true, () =>
            {
                _activeGroupFilter = "Ignored";
                UpdateGroupFilterChips();
                _sensorsView?.Refresh();
                UpdateResultsCount();
            }));
        }
    }

    private Button CreateChipButton(string label, int count, bool isActive, bool isIgnored, Action onClick)
    {
        var btn = new Button
        {
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 6, 0),
            Tag = label
        };

        Brush bgBrush, borderBrush, fgBrush, countBrush;
        if (isActive)
        {
            if (isIgnored)
            {
                bgBrush = new SolidColorBrush(Color.FromRgb(46, 30, 5));
                borderBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                fgBrush = new SolidColorBrush(Color.FromRgb(251, 191, 36));
                countBrush = new SolidColorBrush(Color.FromRgb(253, 230, 138));
            }
            else
            {
                bgBrush = new SolidColorBrush(Color.FromRgb(16, 42, 69));
                borderBrush = new SolidColorBrush(Color.FromRgb(0, 130, 252));
                fgBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248));
                countBrush = new SolidColorBrush(Color.FromRgb(125, 211, 252));
            }
        }
        else
        {
            bgBrush = new SolidColorBrush(Color.FromRgb(12, 19, 34));
            borderBrush = new SolidColorBrush(Color.FromRgb(30, 45, 72));
            fgBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            countBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));
        }

        bgBrush.Freeze();
        borderBrush.Freeze();
        fgBrush.Freeze();
        countBrush.Freeze();

        var template = new ControlTemplate(typeof(Button));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        borderFactory.SetValue(Border.PaddingProperty, new Thickness(10, 3, 10, 3));
        borderFactory.SetValue(Border.BackgroundProperty, bgBrush);
        borderFactory.SetValue(Border.BorderBrushProperty, borderBrush);

        var spFactory = new FrameworkElementFactory(typeof(StackPanel));
        spFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

        var textFactory = new FrameworkElementFactory(typeof(TextBlock));
        textFactory.SetValue(TextBlock.TextProperty, label);
        textFactory.SetValue(TextBlock.FontSizeProperty, 11.0);
        textFactory.SetValue(TextBlock.FontWeightProperty, isActive ? FontWeights.Bold : FontWeights.SemiBold);
        textFactory.SetValue(TextBlock.ForegroundProperty, fgBrush);
        spFactory.AppendChild(textFactory);

        var countFactory = new FrameworkElementFactory(typeof(TextBlock));
        countFactory.SetValue(TextBlock.TextProperty, $" ({count})");
        countFactory.SetValue(TextBlock.FontSizeProperty, 10.0);
        countFactory.SetValue(TextBlock.ForegroundProperty, countBrush);
        spFactory.AppendChild(countFactory);

        borderFactory.AppendChild(spFactory);
        template.VisualTree = borderFactory;
        btn.Template = template;
        btn.Click += (s, e) => onClick();

        return btn;
    }

    private void UpdateResultsCount()
    {
        if (TxtResultsCount == null) return;

        int total = _sensors.Count;
        int visible = 0;
        if (_sensorsView != null)
        {
            foreach (var _ in _sensorsView)
            {
                visible++;
            }
        }
        else
        {
            visible = total;
        }

        bool isFiltered = !string.IsNullOrWhiteSpace(_searchText) || !string.Equals(_activeGroupFilter, "All", StringComparison.OrdinalIgnoreCase);

        if (isFiltered)
        {
            TxtResultsCount.Text = $"{visible} of {total} shown";
        }
        else
        {
            TxtResultsCount.Text = $"{total} sensor{(total == 1 ? "" : "s")}";
        }
    }

    #endregion

    #region Navigation Tabs (inFaux Style)

    private void TabAllSensors_Click(object sender, RoutedEventArgs e)
    {
        ViewAllSensors.Visibility = Visibility.Visible;
        ViewSettings.Visibility = Visibility.Collapsed;
    }

    private void TabSettings_Click(object sender, RoutedEventArgs e)
    {
        ViewAllSensors.Visibility = Visibility.Collapsed;
        ViewSettings.Visibility = Visibility.Visible;
        RefreshFirewallStatus();
    }

    private void BadgeUpdateAvailable_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Switch to Settings tab and trigger update modal
        TabSettings.IsChecked = true;
        TabSettings_Click(sender, e);
        OnCheckForUpdatesClick(sender, e);
    }

    #endregion



    #region Windows Firewall Handlers (Non-Admin Server Support)

    private void RefreshFirewallStatus()
    {
        Task.Run(() =>
        {
            bool isConfigured = FirewallService.IsRuleConfigured(6053);
            Dispatcher.InvokeAsync(() =>
            {
                if (isConfigured)
                {
                    DotFirewallStatus.Fill = (Brush)FindResource("EmeraldLiveBrush");
                    TxtFirewallStatus.Text = "Active | Allowed";
                    TxtFirewallStatus.Foreground = (Brush)FindResource("EmeraldLiveBrush");
                    BtnAddFirewall.Content = "✓ Configured";
                    BtnAddFirewall.IsEnabled = false;
                }
                else
                {
                    DotFirewallStatus.Fill = (Brush)FindResource("PrimaryAccentBrush");
                    TxtFirewallStatus.Text = "Not Configured | Non-Admin";
                    TxtFirewallStatus.Foreground = (Brush)FindResource("PrimaryAccentBrush");
                    BtnAddFirewall.Content = "🛡️ Configure (Admin)";
                    BtnAddFirewall.IsEnabled = true;
                }
            });
        });
    }

    private void BadgeFirewallStatus_Click(object sender, MouseButtonEventArgs e)
    {
        RefreshFirewallStatus();
    }

    private void BtnAddFirewall_Click(object sender, RoutedEventArgs e)
    {
        BtnAddFirewall.IsEnabled = false;
        BtnAddFirewall.Content = "Elevating...";

        Task.Run(() =>
        {
            bool success = FirewallService.AddRuleElevated(6053, 8766);
            Dispatcher.InvokeAsync(() =>
            {
                RefreshFirewallStatus();
                if (success)
                {
                    ShowInAppModal(
                        "Firewall Configured",
                        "Inbound Windows Defender Firewall rules for ESPHome (port 6053) and REST API (port 8766) were successfully created.",
                        "✓",
                        "Rule Name: AirFoxBT Bluetooth Proxy\nPorts: TCP 6053, 8766\nAction: Allow Inbound\nProfile: Any"
                    );
                }
                else
                {
                    string script = FirewallService.GetPowerShellAdminCommand(6053, 8766);
                    ShowInAppModal(
                        "Administrator Required",
                        "AirFoxBT could not automatically add the firewall rule because elevation was cancelled or denied on this non-admin server account.\n\nYou can copy the PowerShell command below and run it in an elevated Administrator prompt on this server.",
                        "⚠️",
                        script,
                        "Copy Command",
                        () =>
                        {
                            try { System.Windows.Clipboard.SetText(script); } catch { }
                        }
                    );
                }
            });
        });
    }

    private void BtnCopyFirewallCmd_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string script = FirewallService.GetPowerShellAdminCommand(6053, 8766);
            System.Windows.Clipboard.SetText(script);
            BtnCopyFirewallCmd.Content = "✓ Copied!";
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (s, args) =>
            {
                BtnCopyFirewallCmd.Content = "📋 Copy Script";
                timer.Stop();
            };
            timer.Start();

            LstLogs.Items.Add($"[{DateTime.Now:HH:mm:ss}] [FIREWALL] Copied admin firewall command to clipboard.");
        }
        catch { }
    }

    #endregion

    #region In-App Modal Dialog (Screenshot 2 UX Design)

    public void ShowInAppModal(
        string title,
        string message,
        string icon = "ℹ️",
        string? details = null,
        string primaryButtonText = "OK",
        Action? onPrimary = null,
        string? secondaryButtonText = null,
        Action? onSecondary = null)
    {
        _onModalPrimaryAction = onPrimary;
        _onModalSecondaryAction = onSecondary;

        ModalTitleText.Text = title;
        ModalMessageText.Text = message;
        ModalIconText.Text = icon;

        if (!string.IsNullOrWhiteSpace(details))
        {
            ModalDetailsBox.Text = details;
            ModalDetailsCard.Visibility = Visibility.Visible;
        }
        else
        {
            ModalDetailsBox.Text = string.Empty;
            ModalDetailsCard.Visibility = Visibility.Collapsed;
        }

        ModalPrimaryButton.Content = primaryButtonText;

        if (!string.IsNullOrWhiteSpace(secondaryButtonText))
        {
            ModalSecondaryButton.Content = secondaryButtonText;
            ModalSecondaryButton.Visibility = Visibility.Visible;
        }
        else
        {
            ModalSecondaryButton.Visibility = Visibility.Collapsed;
        }

        InAppModalOverlay.Visibility = Visibility.Visible;
    }

    public void HideInAppModal()
    {
        InAppModalOverlay.Visibility = Visibility.Collapsed;
        _onModalPrimaryAction = null;
        _onModalSecondaryAction = null;
    }

    private void OnModalBackdropMouseDown(object sender, MouseButtonEventArgs e)
    {
        HideInAppModal();
    }

    private void OnModalCloseClick(object sender, RoutedEventArgs e)
    {
        HideInAppModal();
    }

    private void OnModalPrimaryClick(object sender, RoutedEventArgs e)
    {
        var action = _onModalPrimaryAction;
        HideInAppModal();
        action?.Invoke();
    }

    private void OnModalSecondaryClick(object sender, RoutedEventArgs e)
    {
        var action = _onModalSecondaryAction;
        HideInAppModal();
        action?.Invoke();
    }

    private void OnModalCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(ModalDetailsBox.Text))
            {
                System.Windows.Clipboard.SetText(ModalDetailsBox.Text);
                ModalCopyButton.Content = "✓ Copied!";
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (s, args) =>
                {
                    ModalCopyButton.Content = "Copy";
                    timer.Stop();
                };
                timer.Start();
            }
        }
        catch { }
    }

    #endregion

    #region Cryptographic Integrity & Update Handlers (Screenshot 1 & 2)

    private async void OnCheckForUpdatesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnCheckUpdates.IsEnabled = false;
            TxtUpdateStatus.Text = "• Checking for updates...";
            TxtUpdateStatus.Foreground = (Brush)FindResource("TextSecondaryBrush");
            StatusDot.Fill = (Brush)FindResource("TextSecondaryBrush");

            var release = await UpdateService.Instance.CheckForUpdatesAsync(isManual: true);
            if (release != null)
            {
                _latestAvailableRelease = release;
                BadgeUpdateAvailable.Visibility = Visibility.Visible;
                TxtBadgeUpdateText.Text = $"Update {release.TagName}";
                TxtUpdateStatus.Text = $"• Update available: {release.TagName}";
                TxtUpdateStatus.Foreground = (Brush)FindResource("PrimaryAccentBrush");
                StatusDot.Fill = (Brush)FindResource("PrimaryAccentBrush");
                TxtIntegrityBadge.Text = "Update Available";

                ShowInAppModal(
                    title: "AirFoxBT Update Available",
                    message: $"A new official release is available: {release.TagName}\n\nWould you like AirFoxBT to verify the cryptographic SHA-256 hash and update automatically?",
                    icon: "🚀",
                    details: $"Target Tag: {release.TagName}\nPublished: {release.PublishedAt?.ToString("g") ?? "N/A"}\n\nRelease Notes:\n{(string.IsNullOrWhiteSpace(release.Body) ? "Official release from GitHub" : release.Body.Trim())}",
                    primaryButtonText: "Update Now",
                    onPrimary: async () =>
                    {
                        try
                        {
                            var progress = new Progress<string>(msg =>
                            {
                                TxtUpdateStatus.Text = $"• {msg}";
                            });
                            await UpdateService.Instance.ExecuteUpdateAsync(release, progress);
                        }
                        catch (Exception ex)
                        {
                            ShowInAppModal(
                                title: "Update Failed",
                                message: $"Update execution encountered an error:\n{ex.Message}",
                                icon: "⚠️",
                                primaryButtonText: "Close");
                        }
                    },
                    secondaryButtonText: "Later");
            }
            else
            {
                TxtUpdateStatus.Text = "• Up to date";
                TxtUpdateStatus.Foreground = (Brush)FindResource("EmeraldLiveBrush");
                StatusDot.Fill = (Brush)FindResource("EmeraldLiveBrush");
                TxtIntegrityBadge.Text = "SHA-256 Verified";

                ShowInAppModal(
                    title: "Update Check",
                    message: "AirFoxBT is up to date! You are running the latest official build.",
                    icon: "✓",
                    details: $"Current Version: v{UpdateService.Instance.GetCurrentVersionString()}\nSHA-256:\n{UpdateService.Instance.GetLocalExecutableHash()}",
                    primaryButtonText: "OK");
            }
        }
        catch (Exception ex)
        {
            TxtUpdateStatus.Text = $"• Check error: {ex.Message}";
            ShowInAppModal(
                title: "Update Check Failed",
                message: $"Unable to contact GitHub Releases API:\n{ex.Message}",
                icon: "⚠️",
                primaryButtonText: "Close");
        }
        finally
        {
            BtnCheckUpdates.IsEnabled = true;
        }
    }

    private async void OnVerifyHashClick(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnAuditHash.IsEnabled = false;
            TxtUpdateStatus.Text = "• Auditing cryptographic hash against GitHub...";

            var audit = await UpdateService.Instance.AuditAgainstGitHubAsync();
            string icon = audit.Status == IntegrityStatus.OfficialLatest ? "🛡️" : "⚠️";
            string details = $"Local Version: {audit.LocalVersion}\nLocal Hash:\n{audit.LocalHash}\n\nExpected Release Hash:\n{audit.ExpectedHash ?? "N/A"}";

            if (audit.Status == IntegrityStatus.OfficialLatest)
            {
                TxtUpdateStatus.Text = "• Cryptographic SHA-256 integrity verified against official GitHub release.";
                TxtUpdateStatus.Foreground = (Brush)FindResource("EmeraldLiveBrush");
                StatusDot.Fill = (Brush)FindResource("EmeraldLiveBrush");
                TxtIntegrityBadge.Text = "SHA-256 Verified";
            }
            else if (audit.Status == IntegrityStatus.LocalDevelopmentBuild)
            {
                TxtUpdateStatus.Text = "• Local / Self-compiled build (hash not present in GitHub releases).";
                TxtUpdateStatus.Foreground = (Brush)FindResource("BleTextBrush");
                StatusDot.Fill = (Brush)FindResource("BleTextBrush");
                TxtIntegrityBadge.Text = "Local Build";
            }
            else
            {
                TxtUpdateStatus.Text = $"• {audit.Message}";
                TxtUpdateStatus.Foreground = (Brush)FindResource("PrimaryAccentBrush");
                StatusDot.Fill = (Brush)FindResource("PrimaryAccentBrush");
                TxtIntegrityBadge.Text = "Audit Warning";
            }

            ShowInAppModal(
                title: "Cryptographic Integrity Audit",
                message: $"Status: {audit.Status}\n\n{audit.Message}",
                icon: icon,
                details: details,
                primaryButtonText: "OK");
        }
        catch (Exception ex)
        {
            ShowInAppModal(
                title: "Audit Error",
                message: $"Audit failed:\n{ex.Message}",
                icon: "⚠️",
                primaryButtonText: "Close");
        }
        finally
        {
            BtnAuditHash.IsEnabled = true;
        }
    }

    private void OnCopyHashClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string hash = UpdateService.Instance.GetLocalExecutableHash();
            System.Windows.Clipboard.SetText(hash);
            if (sender is System.Windows.Controls.Button btn)
            {
                string oldText = btn.Content?.ToString() ?? "Copy Hash";
                btn.Content = "✓ Copied!";
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (s, args) =>
                {
                    btn.Content = oldText;
                    timer.Stop();
                };
                timer.Start();
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error("UI", $"Failed to copy hash: {ex.Message}");
        }
    }

    private void ToggleAutoUpdates_Click(object sender, RoutedEventArgs e)
    {
        _settings.CheckForUpdates = ToggleAutoUpdates.IsChecked == true;
        _storageService.SaveSettings(_settings);
        LstLogs.Items.Add($"[{DateTime.Now:HH:mm:ss}] [SETTINGS] Automatic update checks: {(_settings.CheckForUpdates ? "Enabled" : "Disabled")}");
    }

    private void ToggleStartWithWindows_Click(object sender, RoutedEventArgs e)
    {
        bool enable = ToggleStartWithWindows.IsChecked == true;
        bool ok = StartupService.SetStartup(enable);
        if (!ok)
        {
            ToggleStartWithWindows.IsChecked = StartupService.IsStartupEnabled();
        }
        LstLogs.Items.Add($"[{DateTime.Now:HH:mm:ss}] [SETTINGS] Start with Windows: {(StartupService.IsStartupEnabled() ? "Enabled" : "Disabled")}");
    }



    #region Network Selection & Dynamic Interface Reload

    private bool _isPopulatingNics = false;

    private void PopulateNetworkInterfaces(bool reAnnounce = false)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _isPopulatingNics = true;
            try
            {
                var bindable = MdnsService.GetBindableIpv4Addresses();
                CmbHostIp.Items.Clear();

                // Option 0: Multi-VLAN Auto Broadcast
                var autoItem = new System.Windows.Controls.ComboBoxItem
                {
                    Content = "All Interfaces (0.0.0.0) | Multi-VLAN",
                    Tag = "Auto"
                };
                CmbHostIp.Items.Add(autoItem);

                System.Windows.Controls.ComboBoxItem? itemToSelect = autoItem;

                foreach (var (ip, name, desc) in bindable)
                {
                    var item = new System.Windows.Controls.ComboBoxItem
                    {
                        Content = $"{ip}  |  {name}",
                        Tag = ip.ToString()
                    };
                    CmbHostIp.Items.Add(item);

                    if (!string.IsNullOrEmpty(_settings.SelectedHostIp) &&
                        string.Equals(_settings.SelectedHostIp, ip.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        itemToSelect = item;
                    }
                }

                if (itemToSelect != null)
                {
                    CmbHostIp.SelectedItem = itemToSelect;
                }
                else
                {
                    CmbHostIp.SelectedIndex = 0;
                }

                UpdateHostIpUi();

                if (reAnnounce)
                {
                    string targetIp = (CmbHostIp.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "Auto";
                    _mdnsService.RestartWithIp(targetIp);
                    _apiService.RestartWithIp(targetIp);
                    LstLogs.Items.Add($"[{DateTime.Now:HH:mm:ss}] [Network] 🔄 Network interfaces refreshed ({bindable.Count} active found). Services reloaded.");
                }
            }
            finally
            {
                _isPopulatingNics = false;
            }
        });
    }

    private void UpdateHostIpUi()
    {
        string chosenTag = (CmbHostIp.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "Auto";
        if (chosenTag == "Auto" || chosenTag == "0.0.0.0")
        {
            var primaryIp = MdnsService.GetPrimaryIpv4Address();
            TxtHostIp.Text = primaryIp.ToString();
        }
        else
        {
            TxtHostIp.Text = chosenTag;
        }
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        PopulateNetworkInterfaces(reAnnounce: true);
    }

    private void CmbHostIp_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isPopulatingNics || _settings == null || _storageService == null) return;

        if (CmbHostIp.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem)
        {
            string chosenIp = selectedItem.Tag?.ToString() ?? "Auto";
            _settings.SelectedHostIp = chosenIp;
            _storageService.SaveSettings(_settings);

            UpdateHostIpUi();

            _mdnsService.RestartWithIp(chosenIp);
            _apiService.RestartWithIp(chosenIp);

            AppendLog($"[{DateTime.Now:HH:mm:ss}] [Network] 🔄 Switched active network to {chosenIp}. Services restarted.");
        }
    }

    private void BtnRefreshNics_Click(object sender, RoutedEventArgs e)
    {
        PopulateNetworkInterfaces(reAnnounce: true);
    }

    private void BtnCopyHostIp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string chosenTag = (CmbHostIp.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "Auto";
            string ipToCopy = (chosenTag == "Auto" || chosenTag == "0.0.0.0")
                ? MdnsService.GetPrimaryIpv4Address().ToString()
                : chosenTag;

            System.Windows.Clipboard.SetText(ipToCopy);
            if (sender is System.Windows.Controls.Button btn)
            {
                string oldText = btn.Content?.ToString() ?? "📋 Copy IP";
                btn.Content = "✓ Copied!";
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (s, args) =>
                {
                    btn.Content = oldText;
                    timer.Stop();
                };
                timer.Start();
            }
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 📋 Copied Host IP ({ipToCopy}) to clipboard.");
        }
        catch { }
    }

    private void TxtDeviceName_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_nameDebounceTimer == null) return;
        _nameDebounceTimer.Stop();
        _nameDebounceTimer.Start();
    }

    private void BtnResetDeviceName_Click(object sender, RoutedEventArgs e)
    {
        TxtDeviceName.Text = Environment.MachineName;
        ApplyDeviceNameChange();
    }

    private void ApplyDeviceNameChange()
    {
        string newName = TxtDeviceName.Text.Trim();
        if (string.Equals(_settings.DeviceName, newName, StringComparison.OrdinalIgnoreCase)) return;

        _settings.DeviceName = newName;
        _storageService.SaveSettings(_settings);

        var (slug, friendly) = DeviceNaming.ResolveNames(newName);
        _esphomeServer.NodeName = slug;
        _esphomeServer.FriendlyName = friendly;
        _mdnsService.DeviceName = newName;

        string currentIp = (CmbHostIp?.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? _settings.SelectedHostIp;
        _mdnsService.RestartWithIp(currentIp, newName);

        AppendLog($"[{DateTime.Now:HH:mm:ss}] [Device] 🏷️ Gateway name updated to '{friendly}' ({slug}).");
    }

    #endregion

    private void OnGitHubClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/TalviFox/AirFoxBT",
                UseShellExecute = true
            });
        }
        catch { }
    }

    #endregion

    private void CmbThrottleMode_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_esphomeServer == null || _storageService == null || _settings == null) return;

        if (CmbThrottleMode.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem)
        {
            var tag = selectedItem.Tag?.ToString() ?? "Balanced";
            var preset = ThrottleSettings.ParsePreset(tag);
            _settings.ThrottleMode = preset.ToString();
            _storageService.SaveSettings(_settings);
            _esphomeServer.Throttle = ThrottleSettings.FromPreset(preset);
        }
    }

    #region Battery Alert Notifications & Debounce Settings

    private void UpdateBatterySettingsUi()
    {
        if (_isUpdatingBatteryUi || _batteryAlertService == null || _settings == null) return;
        _isUpdatingBatteryUi = true;

        try
        {
            // 1. Sync Settings Tab Preset ComboBox
            if (CmbSettingsBatteryPreset != null)
            {
                foreach (System.Windows.Controls.ComboBoxItem item in CmbSettingsBatteryPreset.Items)
                {
                    if (string.Equals(item.Tag?.ToString(), _settings.BatteryAlertPreset, StringComparison.OrdinalIgnoreCase))
                    {
                        item.IsSelected = true;
                        break;
                    }
                }
            }

            // 3. Sync Settings Tab Threshold ComboBox
            if (CmbSettingsBatteryThreshold != null)
            {
                foreach (System.Windows.Controls.ComboBoxItem item in CmbSettingsBatteryThreshold.Items)
                {
                    if (int.TryParse(item.Tag?.ToString(), out int val) && val == _settings.BatteryThresholdPercent)
                    {
                        item.IsSelected = true;
                        break;
                    }
                }
            }

            // 4. Sync Settings Tab Cooldown ComboBox
            if (CmbSettingsBatteryCooldown != null)
            {
                foreach (System.Windows.Controls.ComboBoxItem item in CmbSettingsBatteryCooldown.Items)
                {
                    if (int.TryParse(item.Tag?.ToString(), out int val) && val == _settings.BatteryCooldownHours)
                    {
                        item.IsSelected = true;
                        break;
                    }
                }
            }

            // 5. Sync Settings Tab Packet Debounce ComboBox
            if (CmbSettingsBatteryPackets != null)
            {
                foreach (System.Windows.Controls.ComboBoxItem item in CmbSettingsBatteryPackets.Items)
                {
                    if (int.TryParse(item.Tag?.ToString(), out int val) && val == _settings.BatteryDebouncePackets)
                    {
                        item.IsSelected = true;
                        break;
                    }
                }
            }

            // 6. Update Badge Text in Settings Tab
            if (TxtBatteryAlertBadge != null)
            {
                if (_batteryAlertService.Preset == BatteryAlertPreset.Disabled)
                {
                    TxtBatteryAlertBadge.Text = "Disabled (Off)";
                    TxtBatteryAlertBadge.Foreground = (Brush)FindResource("TextMutedBrush");
                }
                else if (_batteryAlertService.Preset == BatteryAlertPreset.Custom)
                {
                    TxtBatteryAlertBadge.Text = $"Custom (≤ {_batteryAlertService.ThresholdPercent}%)";
                    TxtBatteryAlertBadge.Foreground = (Brush)FindResource("PrimaryAccentBrush");
                }
                else
                {
                    TxtBatteryAlertBadge.Text = $"≤ {_batteryAlertService.ThresholdPercent}% ({_batteryAlertService.Preset})";
                    TxtBatteryAlertBadge.Foreground = (Brush)FindResource("BleTextBrush");
                }
            }
        }
        finally
        {
            _isUpdatingBatteryUi = false;
        }
    }

    private void CmbSettingsBatteryPreset_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isUpdatingBatteryUi || _batteryAlertService == null || _storageService == null || _settings == null) return;

        if (CmbSettingsBatteryPreset.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem)
        {
            string tag = selectedItem.Tag?.ToString() ?? "Balanced";
            if (Enum.TryParse<BatteryAlertPreset>(tag, true, out var preset))
            {
                if (preset == BatteryAlertPreset.Custom)
                {
                    _batteryAlertService.SetCustom(_settings.BatteryThresholdPercent, _settings.BatteryCooldownHours, _settings.BatteryDebouncePackets);
                }
                else
                {
                    _batteryAlertService.SetPreset(preset);
                    _settings.BatteryThresholdPercent = _batteryAlertService.ThresholdPercent;
                    _settings.BatteryCooldownHours = _batteryAlertService.CooldownHours;
                    _settings.BatteryDebouncePackets = _batteryAlertService.DebouncePackets;
                }

                _settings.BatteryAlertPreset = preset.ToString();
                _storageService.SaveSettings(_settings);
                UpdateBatterySettingsUi();
            }
        }
    }

    private void CmbSettingsBatteryThreshold_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isUpdatingBatteryUi || _batteryAlertService == null || _storageService == null || _settings == null) return;

        if (CmbSettingsBatteryThreshold.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem &&
            int.TryParse(selectedItem.Tag?.ToString(), out int threshold))
        {
            _settings.BatteryThresholdPercent = threshold;
            _settings.BatteryAlertPreset = "Custom";
            _batteryAlertService.SetCustom(threshold, _settings.BatteryCooldownHours, _settings.BatteryDebouncePackets);
            _storageService.SaveSettings(_settings);
            UpdateBatterySettingsUi();
        }
    }

    private void CmbSettingsBatteryCooldown_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isUpdatingBatteryUi || _batteryAlertService == null || _storageService == null || _settings == null) return;

        if (CmbSettingsBatteryCooldown.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem &&
            int.TryParse(selectedItem.Tag?.ToString(), out int cooldownHours))
        {
            _settings.BatteryCooldownHours = cooldownHours;
            _settings.BatteryAlertPreset = "Custom";
            _batteryAlertService.SetCustom(_settings.BatteryThresholdPercent, cooldownHours, _settings.BatteryDebouncePackets);
            _storageService.SaveSettings(_settings);
            UpdateBatterySettingsUi();
        }
    }

    private void CmbSettingsBatteryPackets_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isUpdatingBatteryUi || _batteryAlertService == null || _storageService == null || _settings == null) return;

        if (CmbSettingsBatteryPackets.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem &&
            int.TryParse(selectedItem.Tag?.ToString(), out int packets))
        {
            _settings.BatteryDebouncePackets = packets;
            _settings.BatteryAlertPreset = "Custom";
            _batteryAlertService.SetCustom(_settings.BatteryThresholdPercent, _settings.BatteryCooldownHours, packets);
            _storageService.SaveSettings(_settings);
            UpdateBatterySettingsUi();
        }
    }

    private void BtnTestBatteryToast_Click(object sender, RoutedEventArgs e)
    {
        _batteryAlertService?.SendTestNotification();
    }

    #endregion

    private void PillHostIp_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(TxtHostIp.Text);
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 📋 Copied Host IP ({TxtHostIp.Text}) to clipboard.");
        }
        catch { }
    }

    private void BtnSendToTray_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        ShowTrayBalloon("AirFoxBT is in System Tray", "Gateway is running quietly in the background.");
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            ShowTrayBalloon("AirFoxBT is minimized", "Gateway is still passively sniffing 2.4 GHz airwaves in the system tray.");
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isExplicitExit)
        {
            e.Cancel = true;
            Hide();
            ShowTrayBalloon("AirFoxBT is still active", "Running in the background. Double-click the tray icon anytime to reopen.");
        }
        else
        {
            _saveDebounceTimer.Stop();
            _nameDebounceTimer?.Stop();
            _relativeTimeTimer.Stop();
            SaveAllSensors();
            _engine.Stop();
            try { NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged; } catch { }
            _apiService.Dispose();
            _esphomeServer.Dispose();
            _mdnsService.Dispose();
        }
    }

    private void ShowTrayBalloon(string title, string text)
    {
        if (!_hasShownTrayNotice && App.TrayIcon != null)
        {
            App.TrayIcon.ShowBalloonTip(3000, title, text, System.Windows.Forms.ToolTipIcon.None);
            _hasShownTrayNotice = true;
        }
    }

    private void OnReadingReceived(BleReading reading)
    {
        Interlocked.Increment(ref _totalPackets);
        _apiService.BroadcastReading(reading);

        string metricSummary;
        if (reading.IsEncrypted)
        {
            metricSummary = "🔒 Encrypted BTHome -> Forwarded to HA (:6053)";
        }
        else if (reading.IsOpen.HasValue)
        {
            var state = reading.IsOpen.Value ? "OPEN" : "CLOSED";
            var attr = reading.RotationDegrees.HasValue ? $" • {reading.RotationDegrees:F1}° tilt" : (reading.IlluminanceLux.HasValue ? $" • {reading.IlluminanceLux:F0} lux" : "");
            metricSummary = $"State: [{state}]{attr}";
        }
        else if (reading.IsMotion.HasValue)
        {
            var motion = reading.IsMotion.Value ? "MOTION" : "CLEAR";
            var attr = reading.IlluminanceLux.HasValue ? $" • {reading.IlluminanceLux:F0} lux" : "";
            metricSummary = $"PIR: [{motion}]{attr}";
        }
        else if (!string.IsNullOrEmpty(reading.ButtonEventName))
        {
            metricSummary = $"Event: [{reading.ButtonEventName}]";
        }
        else if (reading.TemperatureC.HasValue || reading.HumidityPercent.HasValue)
        {
            var tempPart = reading.TemperatureF.HasValue ? $"{reading.TemperatureF:F1}°F / {reading.TemperatureC:F1}°C" : "--.-";
            var humPart = reading.HumidityPercent.HasValue ? $"{reading.HumidityPercent:F0}% RH" : "--%";
            metricSummary = $"Temp: {tempPart} • Hum: {humPart}";
        }
        else if (reading.IsTimeBeacon && reading.BeaconUnixTime.HasValue)
        {
            var beaconLocal = DateTimeOffset.FromUnixTimeSeconds(reading.BeaconUnixTime.Value)
                .ToOffset(TimeSpan.FromSeconds((reading.BeaconUtcOffsetSeconds ?? 0) + (reading.BeaconDstShiftSeconds ?? 0)));
            metricSummary = $"Time Sync Frame: {beaconLocal:HH:mm:ss} (Unix: {reading.BeaconUnixTime}) • TZ: {reading.BeaconUtcOffsetSeconds ?? 0}s • DST: {reading.BeaconDstShiftSeconds ?? 0}s";
        }
        else
        {
            metricSummary = "Relayed frame -> Forwarded to HA (:6053)";
        }

        var battPart = reading.BatteryPercent.HasValue ? $" • 🔋 {reading.BatteryPercent}%" : "";

        // Reactive Time Beacon Burst: Detect when a Shelly display wakes up or requests time sync
        if (_settings.EnableShellyTimeBeacon)
        {
            bool hasButtonEvent = reading.ButtonEventCode.HasValue && reading.ButtonEventCode.Value > 0;
            bool isKnownShelly = reading.MacAddress.StartsWith("C0:2C:ED", StringComparison.OrdinalIgnoreCase) ||
                                 reading.MacAddress.StartsWith("94:B2:16", StringComparison.OrdinalIgnoreCase) ||
                                 reading.MacAddress.StartsWith("F8:44:77", StringComparison.OrdinalIgnoreCase) ||
                                 reading.MacAddress.StartsWith("B8:D6:1A", StringComparison.OrdinalIgnoreCase) ||
                                 reading.MacAddress.StartsWith("BC:02:6E", StringComparison.OrdinalIgnoreCase) ||
                                 (reading.HardwareModel != null && reading.HardwareModel.StartsWith("SBHT", StringComparison.OrdinalIgnoreCase)) ||
                                 string.Equals(reading.DisplayType, "E-Ink", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(reading.DisplayType, "Display", StringComparison.OrdinalIgnoreCase);


        }

        // Standard CLI output
        try
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"[{DateTime.Now:HH:mm:ss}] 📡 {reading.MacAddress}");
            if (!string.IsNullOrEmpty(reading.DeviceName) && reading.DeviceName != "Unknown")
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write($" ({reading.DeviceName})");
            }
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write($" [{reading.Protocol}]");
            Console.ResetColor();
            Console.WriteLine($" -> {metricSummary}{battPart} • RSSI: {reading.Rssi} dBm");
        }
        catch { }

        // Dispatch to WPF UI
        Dispatcher.InvokeAsync(() =>
        {
            TxtPacketsCount.Text = $"{_totalPackets:N0} Packets";

            if (reading.IsTimeBeacon && !reading.TemperatureC.HasValue && !reading.HumidityPercent.HasValue && !_sensorMap.ContainsKey(reading.MacAddress))
            {
                AppendLog($"[{DateTime.Now:HH:mm:ss}] 📡 {reading.MacAddress} ({reading.DeviceName}) [{reading.Protocol}] -> {metricSummary} • RSSI: {reading.Rssi} dBm");
                return;
            }

            if (!_sensorMap.TryGetValue(reading.MacAddress, out var vm))
            {
                vm = new SensorCardViewModel();
                vm.UpdateFromReading(reading);

                if (_settings.AutoSyncNamedSensors && vm.HasCustomName)
                {
                    vm.IsSyncEnabled = true;
                }

                vm.OnConfigChanged += OnSensorConfigChanged;
                vm.OnDeleteRequested = ConfirmAndDeleteSensor;
                _sensorMap[reading.MacAddress] = vm;
                _sensors.Add(vm);
                TxtSensorsCount.Text = $"{_sensors.Count} Sensor{(_sensors.Count == 1 ? "" : "s")}";
                App.UpdateTrayStatus(_sensors.Count, _totalPackets);
                SaveAllSensors();
                UpdateGroupFilterChips();
                UpdateResultsCount();
            }
            else
            {
                vm.UpdateFromReading(reading);
            }

            // Evaluate battery status with multi-stage debounce, hysteresis, and cooldown
            _batteryAlertService.ProcessReading(reading, vm.DisplayName);

            // Append to rolling log drawer
            var nameStr = string.IsNullOrWhiteSpace(vm.DisplayName) ? "" : $" ({vm.DisplayName})";
            var logLine = $"[{DateTime.Now:HH:mm:ss}] 📡 {reading.MacAddress}{nameStr} [{reading.Protocol}] -> {metricSummary}{battPart} • RSSI: {reading.Rssi} dBm";

            AppendLog(logLine);
        });
    }

    private void OnStatusChanged(string status)
    {
        AppendLog($"[{DateTime.Now:HH:mm:ss}] [STATUS] {status}");
    }

    private void OnError(Exception ex)
    {
        AppendLog($"[{DateTime.Now:HH:mm:ss}] [ERROR] {ex.Message}");
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        LstLogs.Items.Clear();
    }

    private void BtnToggleDrawer_Click(object sender, RoutedEventArgs e)
    {
        _isDrawerCollapsed = !_isDrawerCollapsed;
        if (_isDrawerCollapsed)
        {
            LogDrawerRow.Height = new GridLength(0);
            BtnToggleDrawer.Content = "▲";
        }
        else
        {
            LogDrawerRow.Height = new GridLength(160);
            BtnToggleDrawer.Content = "▼";
            if (ChkAutoScroll.IsChecked == true)
            {
                _logScrollViewer ??= FindScrollViewer(LstLogs);
                _logScrollViewer?.ScrollToEnd();
            }
        }
    }

    private void LstLogs_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange > 0 && ChkAutoScroll?.IsChecked == true)
        {
            if (e.OriginalSource is ScrollViewer sv)
            {
                sv.ScrollToEnd();
            }
        }
    }

    private void ChkAutoScroll_Click(object sender, RoutedEventArgs e)
    {
        if (ChkAutoScroll?.IsChecked == true && LstLogs != null)
        {
            _logScrollViewer ??= FindScrollViewer(LstLogs);
            _logScrollViewer?.ScrollToEnd();
        }
    }

    public void AppendLog(string line)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => AppendLog(line));
            return;
        }

        if (LstLogs == null) return;

        if (LstLogs.Items.Count > 400)
        {
            LstLogs.Items.RemoveAt(0);
        }

        LstLogs.Items.Add(line);

        if (ChkAutoScroll?.IsChecked == true)
        {
            _logScrollViewer ??= FindScrollViewer(LstLogs);
            _logScrollViewer?.ScrollToEnd();
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject? root)
    {
        if (root == null) return null;
        if (root is ScrollViewer sv) return sv;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var result = FindScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }
}
