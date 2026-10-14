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

    protected override void OnStartup(StartupEventArgs e)
    {
        // Try attaching to the parent console (if launched from PowerShell or cmd)
        AttachConsole(ATTACH_PARENT_PROCESS);

        DispatcherUnhandledException += (s, args) =>
        {
            var msg = $"Unhandled UI Exception:\n\n{args.Exception}";
            System.IO.File.AppendAllText("crash.log", $"[{DateTime.UtcNow:O}] {msg}\n\n");
            if (!_hasAlertedCrash)
            {
                _hasAlertedCrash = true;
                System.Windows.MessageBox.Show(msg, "AirFoxBT Crash", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var msg = $"Fatal Domain Exception:\n\n{args.ExceptionObject}";
            System.IO.File.AppendAllText("crash.log", $"[{DateTime.UtcNow:O}] {msg}\n\n");
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

        MainWindow.Show();
        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
        MainWindow.Focus();
    }

    public void ShutdownApp()
    {
        if (TrayIcon != null)
        {
            TrayIcon.Visible = false;
            TrayIcon.Dispose();
        }

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (TrayIcon != null)
        {
            TrayIcon.Visible = false;
            TrayIcon.Dispose();
        }

        base.OnExit(e);
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
