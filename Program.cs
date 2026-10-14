using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using AirFoxBT.Engine;
using AirFoxBT.Models;
using AirFoxBT.Services;

namespace AirFoxBT;

public static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);
    private const int ATTACH_PARENT_PROCESS = -1;

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appID);

    private static readonly ConcurrentDictionary<string, BleReading> ActiveSensors = new();
    private static long _packetCount = 0;

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            // Cache-bust Windows Notification icon cache by establishing an explicit AppUserModelID
            try { SetCurrentProcessExplicitAppUserModelID("AirFoxBT"); } catch { }

            // Attach to calling shell console if CLI arguments are provided
            if (args.Length > 0 && !args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase) || a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)))
            {
                AttachConsole(ATTACH_PARENT_PROCESS);
            }

            // CLI utility: Show help and available commands
            if (args.Any(a => a.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                              a.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                              a.Equals("/?", StringComparison.OrdinalIgnoreCase) ||
                              a.Equals("-?", StringComparison.OrdinalIgnoreCase) ||
                              a.Equals("help", StringComparison.OrdinalIgnoreCase)))
            {
                PrintHelp();
                return;
            }

            // CLI utility: Configure Windows Firewall rules
            if (args.Any(a => a.Equals("--firewall", StringComparison.OrdinalIgnoreCase) ||
                              a.Equals("--add-firewall", StringComparison.OrdinalIgnoreCase)))
            {
                RunFirewallCli(elevate: true);
                return;
            }

            if (args.Any(a => a.Equals("--copy-firewall", StringComparison.OrdinalIgnoreCase) ||
                              a.Equals("--firewall-script", StringComparison.OrdinalIgnoreCase)))
            {
                RunFirewallCli(elevate: false);
                return;
            }
            // If explicitly requested CLI or headless mode
            if (args.Contains("--cli", StringComparer.OrdinalIgnoreCase) ||
                args.Contains("-c", StringComparer.OrdinalIgnoreCase) ||
                args.Contains("--headless", StringComparer.OrdinalIgnoreCase))
            {
                RunCliModeAsync(args).GetAwaiter().GetResult();
                return;
            }

            // Default: Run full FoxDen WPF Desktop UI + System Tray
            var app = new App();
            app.InitializeComponent();

            var mainWindow = new MainWindow();
            app.MainWindow = mainWindow;

            bool startMinimized = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                                               a.Equals("--tray", StringComparison.OrdinalIgnoreCase) ||
                                               a.Equals("-tray", StringComparison.OrdinalIgnoreCase));
            app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            if (!startMinimized)
            {
                mainWindow.Show();
                mainWindow.Activate();
            }

            app.Run();
        }
        catch (Exception ex)
        {
            try
            {
                System.IO.File.AppendAllText("crash.log", $"[{DateTime.UtcNow:O}] Startup Failure:\n{ex}\n\n");
                System.Windows.MessageBox.Show($"AirFoxBT could not start:\n\n{ex.Message}\n\nCheck crash.log for details.", "AirFoxBT Startup Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            catch { }
        }
    }


    private static void RunFirewallCli(bool elevate)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n  🦊 AirFoxBT | Windows Firewall Assistant");
        Console.WriteLine("=============================================");
        Console.ResetColor();

        bool isConfigured = FirewallService.IsRuleConfigured();
        if (isConfigured)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[+] Inbound firewall rule is ALREADY active for AirFoxBT (Port 6053 & 8766).");
            Console.ResetColor();
            return;
        }

        if (elevate)
        {
            Console.WriteLine("[-] Requesting Administrator elevation (UAC) to create inbound rules...");
            bool success = FirewallService.AddRuleElevated();
            if (success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[+] Successfully created inbound firewall rules!");
                Console.ResetColor();
                return;
            }
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[-] Elevation was not completed. Below is the manual script for server administrators:\n");
            Console.ResetColor();
        }

        string psCmd = FirewallService.GetPowerShellAdminCommand();
        string netshCmd = FirewallService.GetNetshAdminCommand();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("# PowerShell (Run as Administrator):");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(psCmd + "\n");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("# Or Command Prompt (netsh):");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(netshCmd + "\n");
        Console.ResetColor();
    }

    private static async Task RunCliModeAsync(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
   /\_/\  
  ( o.o )  AirFoxBT | BTHome Relay [CLI Mode]
   > ^ <   FoxDen Software
==================================================================");
        Console.ResetColor();

        var engine = new BleWatcherEngine();

        engine.OnStatusChanged += status =>
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [STATUS] {status}");
            Console.ResetColor();
        };

        engine.OnError += ex =>
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [ERROR] {ex.Message}");
            Console.ResetColor();
        };

        engine.OnReadingReceived += reading =>
        {
            Interlocked.Increment(ref _packetCount);
            ActiveSensors[reading.MacAddress] = reading;

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

            Console.Write(" -> ");

            if (reading.TemperatureC.HasValue)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"{reading.TemperatureF:F1}°F / {reading.TemperatureC:F1}°C");
                Console.ResetColor();
            }

            if (reading.HumidityPercent.HasValue)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($" • {reading.HumidityPercent:F0}% RH");
                Console.ResetColor();
            }

            if (reading.BatteryPercent.HasValue)
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.Write($" • 🔋 {reading.BatteryPercent}%");
                Console.ResetColor();
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($" • RSSI: {reading.Rssi} dBm");
            Console.ResetColor();
        };

        string modeArg = "Balanced";
        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i].Equals("--mode", StringComparison.OrdinalIgnoreCase) ||
                 args[i].Equals("-m", StringComparison.OrdinalIgnoreCase)) &&
                i + 1 < args.Length)
            {
                modeArg = args[i + 1];
            }
        }
        var preset = ThrottleSettings.ParsePreset(modeArg);

        var esphomeServer = new EsphomeProxyServer(6053);
        esphomeServer.Throttle = ThrottleSettings.FromPreset(preset);
        esphomeServer.OnLog += msg =>
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
            Console.ResetColor();
        };
        esphomeServer.Start();

        var mdnsService = new MdnsService();
        mdnsService.OnLog += msg =>
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
            Console.ResetColor();
        };
        mdnsService.Start();

        engine.OnRawAdvertisementReceived += (mac, rssi, type, data) =>
        {
            esphomeServer.BroadcastAdvertisement(mac, rssi, type, data);
        };

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\n[!] Shutting down AirFoxBT gateway...");
            engine.Stop();
            esphomeServer.Dispose();
            mdnsService.Dispose();
            cts.Cancel();
        };

        engine.Start();

        if (!engine.IsRunning)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Bluetooth engine could not be started. Check if Bluetooth is turned ON in Windows.");
            Console.ResetColor();
            esphomeServer.Dispose();
            mdnsService.Dispose();
            return;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($">> Listening passively on 2.4 GHz airwaves. Proxy Mode: {preset} ({esphomeServer.Throttle.Cooldown.TotalSeconds}s cooldown, full sensitivity, zero RSSI floor).");
        Console.WriteLine(">> Press Ctrl+C to exit.\n");
        Console.ResetColor();

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                await Task.Delay(10000, cts.Token);

                if (ActiveSensors.Count > 0)
                {
                    long ingested = esphomeServer.TotalIngestedAdv;
                    long forwarded = esphomeServer.TotalForwardedAdv;
                    long dropped = esphomeServer.TotalDroppedAdv;
                    double dropPct = ingested > 0 ? (dropped * 100.0 / ingested) : 0;

                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine($"--- Heartbeat: {ActiveSensors.Count} sensors | ESPHome [{esphomeServer.Throttle.Preset}]: Forwarded {forwarded:N0} / Ingested {ingested:N0} ({dropPct:F1}% dupes dropped) | {esphomeServer.ConnectedClients} HA client(s) ---");
                    Console.ResetColor();
                }
            }
        }
        catch (TaskCanceledException) { }
        finally
        {
            esphomeServer.Dispose();
            mdnsService.Dispose();
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("[*] AirFoxBT stopped. Goodbye!");
        Console.ResetColor();
    }

    private static void PrintHelp()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
   /\_/\  
  ( o.o )  AirFoxBT | BTHome Relay
   > ^ <   FoxDen Software
==================================================================");
        Console.ResetColor();

        Console.WriteLine("\nUSAGE:");
        Console.WriteLine("  AirFoxBT.exe [options]\n");

        Console.WriteLine("GENERAL OPTIONS:");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("  (no args)           ");
        Console.ResetColor();
        Console.WriteLine("Launch the full FoxDen WPF desktop UI.");

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("  --tray, --minimized ");
        Console.ResetColor();
        Console.WriteLine("Start minimized directly to the Windows system tray.");

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("  --help, -h, /?      ");
        Console.ResetColor();
        Console.WriteLine("Show this command reference.");

        Console.WriteLine("\nHEADLESS GATEWAY MODE:");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("  --cli, --headless   ");
        Console.ResetColor();
        Console.WriteLine("Run as a headless terminal daemon (no GUI, low memory).");
        Console.WriteLine("                      Serves ESPHome Proxy (port 6053) & mDNS discovery.");

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("  --mode <preset>     ");
        Console.ResetColor();
        Console.WriteLine("Throttle preset in CLI mode: Realtime, Balanced, Eco, UltraEco.");

        Console.WriteLine("\nFIREWALL UTILITIES:");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("  --firewall          ");
        Console.ResetColor();
        Console.WriteLine("Prompt UAC elevation to configure Windows Firewall for ports 6053 & 8766.");

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("  --firewall-script   ");
        Console.ResetColor();
        Console.WriteLine("Print manual PowerShell / netsh firewall commands without elevating.");

        Console.WriteLine("\nEXAMPLES:");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  AirFoxBT.exe --minimized");
        Console.WriteLine("  AirFoxBT.exe --cli --mode Balanced");
        Console.WriteLine("  AirFoxBT.exe --firewall");
        Console.ResetColor();
        Console.WriteLine();
    }
}
