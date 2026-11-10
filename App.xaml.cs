using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace AirFoxBT;

public partial class App : Application
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    private const int ATTACH_PARENT_PROCESS = -1;

    public static NotifyIcon? TrayIcon { get; private set; }
    private static Icon? _generatedIcon;
    private static ToolStripMenuItem? _statusMenuItem;
    private static ToolStripMenuItem? _startupMenuItem;
    private static int _lastKnownSensors = 0;
    private static long _lastKnownPackets = 0;

    private static bool _hasAlertedCrash = false;

    private static System.Threading.Mutex? _singleInstanceMutex;
    private static System.Threading.EventWaitHandle? _showWindowEvent;
    private static System.Threading.RegisteredWaitHandle? _registeredWait;
    private const string MutexName = "Local\\FoxDen_AirFoxBT_SingleInstance_Mutex";
    private const string EventName = "Local\\FoxDen_AirFoxBT_ShowWindowEvent";

    protected override void OnStartup(StartupEventArgs e)
    {
        // Try attaching to the parent console (if launched from PowerShell or cmd)
        AttachConsole(ATTACH_PARENT_PROCESS);

        // Single-instance check: Prevent duplicate instances from launching
        bool createdNew = false;
        try
        {
            _singleInstanceMutex = new System.Threading.Mutex(true, MutexName, out createdNew);
        }
        catch (System.Threading.AbandonedMutexException)
        {
            createdNew = true;
        }

        if (!createdNew)
        {
            // Another instance is already running. Signal it to restore and bring its window to the foreground.
            try
            {
                using var showEvent = System.Threading.EventWaitHandle.OpenExisting(EventName);
                showEvent.Set();
            }
            catch { }

            Shutdown(0);
            return;
        }

        try
        {
            _showWindowEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, EventName);
            _registeredWait = System.Threading.ThreadPool.RegisterWaitForSingleObject(_showWindowEvent, (state, timedOut) =>
            {
                Current?.Dispatcher.InvokeAsync(() =>
                {
                    ShowMainWindow();
                });
            }, null, -1, false);
        }
        catch { }

        DispatcherUnhandledException += (s, args) =>
        {
            var msg = $"Unhandled UI Exception:\n\n{args.Exception}";
            Services.LoggingService.Instance.WriteCrashLog("Unhandled UI Exception", args.Exception);
            if (!_hasAlertedCrash)
            {
                _hasAlertedCrash = true;
                System.Windows.MessageBox.Show(msg, "AirFoxBT Crash", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            var msg = $"Fatal Domain Exception:\n\n{args.ExceptionObject}";
            Services.LoggingService.Instance.WriteCrashLog("Fatal Domain Exception", ex);
            System.Windows.MessageBox.Show(msg, "AirFoxBT Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        base.OnStartup(e);

        InitializeSystemTray();

        bool startMinimized = e.Args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));
        if (!startMinimized)
        {
            ShowMainWindow();
        }
        else
        {
            MainWindow = new MainWindow();
        }
    }

    private void InitializeSystemTray()
    {
        _generatedIcon = CreateFoxDenIcon();

        TrayIcon = new NotifyIcon
        {
            Icon = _generatedIcon,
            Text = _lastKnownSensors > 0 
                ? $"AirFoxBT: {_lastKnownSensors} sensor(s) active" 
                : "AirFoxBT | BLE Gateway",
            Visible = true
        };

        var contextMenu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false
        };

        var showItem = new ToolStripMenuItem("🦊 Open Dashboard", null, (s, e) => ShowMainWindow())
        {
            Font = new System.Drawing.Font(contextMenu.Font, System.Drawing.FontStyle.Bold)
        };

        _statusMenuItem = new ToolStripMenuItem(_lastKnownSensors > 0 
            ? $"📡 Sensors: {_lastKnownSensors} | Packets: {_lastKnownPackets:N0}" 
            : "📡 Status: Listening for beacons...")
        {
            Enabled = false
        };

        _startupMenuItem = new ToolStripMenuItem("⚡ Start with Windows")
        {
            Checked = Services.StartupService.IsStartupEnabled(),
            CheckOnClick = true
        };
        _startupMenuItem.Click += (s, e) =>
        {
            Services.StartupService.SetStartup(_startupMenuItem.Checked);
        };

        var exitItem = new ToolStripMenuItem("❌ Exit AirFoxBT", null, (s, e) => ShutdownApp());

        contextMenu.Items.Add(showItem);
        contextMenu.Items.Add(_statusMenuItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_startupMenuItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        TrayIcon.ContextMenuStrip = contextMenu;

        // Single click or double click brings window to foreground
        TrayIcon.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowMainWindow();
            }
        };
        TrayIcon.DoubleClick += (s, e) => ShowMainWindow();
    }

    public static void UpdateTrayStatus(int sensorCount, long packetCount)
    {
        _lastKnownSensors = sensorCount;
        _lastKnownPackets = packetCount;

        if (TrayIcon == null) return;

        var tip = $"AirFoxBT: {sensorCount} sensor{(sensorCount == 1 ? "" : "s")} active ({packetCount:N0} pkts)";
        if (tip.Length > 63) tip = tip.Substring(0, 63);
        try { TrayIcon.Text = tip; } catch { }

        if (_statusMenuItem != null)
        {
            _statusMenuItem.Text = $"📡 Sensors: {sensorCount} | Packets: {packetCount:N0}";
        }
    }

    public void ShowMainWindow()
    {
        if (MainWindow == null)
        {
            MainWindow = new MainWindow();
        }

        if (MainWindow.WindowState == WindowState.Minimized)
        {
            MainWindow.WindowState = WindowState.Normal;
        }

        MainWindow.Show();
        MainWindow.Activate();
        MainWindow.Topmost = true;
        MainWindow.Topmost = false;
        MainWindow.Focus();
    }

    public void ShutdownApp()
    {
        CleanupSingleInstance();

        if (TrayIcon != null)
        {
            TrayIcon.Visible = false;
            TrayIcon.Dispose();
        }

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CleanupSingleInstance();

        if (TrayIcon != null)
        {
            TrayIcon.Visible = false;
            TrayIcon.Dispose();
        }

        base.OnExit(e);
    }

    private static void CleanupSingleInstance()
    {
        try
        {
            _registeredWait?.Unregister(null);
            _registeredWait = null;
            _showWindowEvent?.Dispose();
            _showWindowEvent = null;

            if (_singleInstanceMutex != null)
            {
                try { _singleInstanceMutex.ReleaseMutex(); } catch { }
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }
        }
        catch { }
    }

    private static Icon CreateFoxDenIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/AirFoxBT;component/assets/logo.png", UriKind.Absolute);
            var streamInfo = Application.GetResourceStream(uri);
            if (streamInfo != null)
            {
                using var stream = streamInfo.Stream;
                using var bmp = new Bitmap(stream);
                using var resized = new Bitmap(bmp, 32, 32);
                IntPtr hIcon = resized.GetHicon();
                var icon = (Icon)Icon.FromHandle(hIcon).Clone();
                DestroyIcon(hIcon);
                return icon;
            }
        }
        catch { }

        return SystemIcons.Application;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

public class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColorTable()) { }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Selected 
            ? System.Drawing.Color.FromArgb(245, 158, 11) // Amber #F59E0B
            : (e.Item.Enabled ? System.Drawing.Color.FromArgb(241, 245, 249) : System.Drawing.Color.FromArgb(100, 116, 139));
        base.OnRenderItemText(e);
    }
}

public class DarkColorTable : ProfessionalColorTable
{
    public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.FromArgb(11, 19, 43); // #0B132B Midnight Navy
    public override System.Drawing.Color ImageMarginGradientBegin => System.Drawing.Color.FromArgb(13, 22, 50);
    public override System.Drawing.Color ImageMarginGradientMiddle => System.Drawing.Color.FromArgb(13, 22, 50);
    public override System.Drawing.Color ImageMarginGradientEnd => System.Drawing.Color.FromArgb(13, 22, 50);
    public override System.Drawing.Color MenuBorder => System.Drawing.Color.FromArgb(30, 45, 72); // #1E2D48
    public override System.Drawing.Color MenuItemBorder => System.Drawing.Color.FromArgb(245, 158, 11); // Amber #F59E0B
    public override System.Drawing.Color MenuItemSelected => System.Drawing.Color.FromArgb(21, 32, 60);
    public override System.Drawing.Color MenuItemSelectedGradientBegin => System.Drawing.Color.FromArgb(21, 32, 60);
    public override System.Drawing.Color MenuItemSelectedGradientEnd => System.Drawing.Color.FromArgb(21, 32, 60);
    public override System.Drawing.Color SeparatorDark => System.Drawing.Color.FromArgb(30, 45, 72);
    public override System.Drawing.Color SeparatorLight => System.Drawing.Color.FromArgb(11, 19, 43);
}
