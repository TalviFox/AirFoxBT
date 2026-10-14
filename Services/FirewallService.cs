using System;
using System.Diagnostics;
using System.IO;

namespace AirFoxBT.Services;

public static class FirewallService
{
    public const string RuleName = "AirFoxBT Bluetooth Proxy";

    /// <summary>
    /// Checks whether an inbound rule with RuleName already exists in Windows Defender Firewall.
    /// This query operation succeeds even on non-admin accounts.
    /// </summary>
    public static bool IsRuleConfigured(int port = 6053)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall show rule name=\"{RuleName}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var p = Process.Start(psi);
            if (p == null) return false;

            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(1500);

            return p.ExitCode == 0 && output.Contains(RuleName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Generates the exact elevated PowerShell command for a server administrator to run.
    /// </summary>
    public static string GetPowerShellAdminCommand(int esphomePort = 6053, int apiPort = 8766)
    {
        string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "AirFoxBT.exe";
        return $"New-NetFirewallRule -DisplayName \"{RuleName}\" -Direction Inbound -LocalPort {esphomePort},{apiPort} -Protocol TCP -Action Allow -Profile Any -Program \"{exePath}\" -Description \"Inbound telemetry and ESPHome native API for Home Assistant\"; netsh http add urlacl url=http://*:{apiPort}/ user=Everyone";
    }

    /// <summary>
    /// Generates the standard netsh command for command prompt / batch scripts.
    /// </summary>
    public static string GetNetshAdminCommand(int esphomePort = 6053, int apiPort = 8766)
    {
        return $"netsh advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=TCP localport={esphomePort},{apiPort} & netsh http add urlacl url=http://*:{apiPort}/ user=Everyone";
    }

    /// <summary>
    /// Attempts to add the firewall rule directly (works if already running as Administrator).
    /// </summary>
    public static bool AddRuleDirect(int esphomePort = 6053, int apiPort = 8766)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=TCP localport={esphomePort},{apiPort}",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(2000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Prompts for UAC elevation (Administrator credentials) to add the Windows Firewall rules.
    /// Enables non-admin server accounts with admin credentials to configure firewall with one click.
    /// </summary>
    public static bool AddRuleElevated(int esphomePort = 6053, int apiPort = 8766)
    {
        try
        {
            string psScript = $"New-NetFirewallRule -DisplayName '{RuleName}' -Direction Inbound -LocalPort {esphomePort},{apiPort} -Protocol TCP -Action Allow -Profile Any -ErrorAction SilentlyContinue; " +
                              $"netsh advfirewall firewall add rule name='{RuleName}' dir=in action=allow protocol=TCP localport={esphomePort},{apiPort}";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{psScript}\"",
                Verb = "runas", // Triggers Windows UAC elevation prompt
                UseShellExecute = true
            };

            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(5000);
            return IsRuleConfigured(esphomePort);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Warning("Firewall", $"UAC elevation cancelled or failed: {ex.Message}");
            return false;
        }
    }
}
