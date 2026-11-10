using System;
using System.IO;

namespace AirFoxBT.Services;

public class LoggingService
{
    private static readonly Lazy<LoggingService> _instance = new(() => new LoggingService());
    public static LoggingService Instance => _instance.Value;

    private readonly string _logFilePath;
    private readonly string _crashFilePath;
    private readonly object _lock = new();

    public string LogDirectory { get; }
    public string LogFilePath => _logFilePath;
    public string CrashFilePath => _crashFilePath;

    public event Action<string, string, string>? OnLog;

    public LoggingService()
    {
        LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FoxDen",
            "AirFoxBT",
            "logs"
        );

        Directory.CreateDirectory(LogDirectory);
        _logFilePath = Path.Combine(LogDirectory, "airfoxbt.log");
        _crashFilePath = Path.Combine(LogDirectory, "crash.log");
    }

    public void Info(string source, string message) => Log("INFO", source, message);
    public void Warning(string source, string message) => Log("WARN", source, message);
    public void Error(string source, string message, Exception? ex = null)
    {
        string fullMessage = ex != null ? $"{message}: {ex.Message}\n{ex.StackTrace}" : message;
        Log("ERROR", source, fullMessage);
    }

    public void WriteCrashLog(string message, Exception? ex = null)
    {
        string timestamp = DateTime.UtcNow.ToString("O");
        string detail = ex != null ? $"{message}\n{ex}\n" : $"{message}\n";
        string entry = $"[{timestamp}] {detail}\n";

        Error("Crash", message, ex);

        lock (_lock)
        {
            try
            {
                File.AppendAllText(_crashFilePath, entry);
            }
            catch { }
        }
    }

    public void LogLine(string line)
    {
        lock (_lock)
        {
            try
            {
                var fi = new FileInfo(_logFilePath);
                if (fi.Exists && fi.Length > 5 * 1024 * 1024)
                {
                    string oldLog = Path.Combine(fi.DirectoryName!, "airfoxbt.old.log");
                    File.Copy(_logFilePath, oldLog, overwrite: true);
                    string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    File.WriteAllText(_logFilePath, $"[{ts}] [INFO] [Logger] Log file rotated.\n");
                }

                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
            catch { }
        }
    }

    private void Log(string level, string source, string message)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string line = $"[{timestamp}] [{level}] [{source}] {message}";

        try
        {
            Console.WriteLine(line);
        }
        catch { }

        lock (_lock)
        {
            try
            {
                // Auto-truncate if log exceeds 5MB
                var fi = new FileInfo(_logFilePath);
                if (fi.Exists && fi.Length > 5 * 1024 * 1024)
                {
                    string oldLog = Path.Combine(fi.DirectoryName!, "airfoxbt.old.log");
                    File.Copy(_logFilePath, oldLog, overwrite: true);
                    File.WriteAllText(_logFilePath, $"[{timestamp}] [INFO] [Logger] Log file rotated.\n");
                }

                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
            catch { }
        }

        try
        {
            OnLog?.Invoke(level, source, message);
        }
        catch { }
    }
}
