using System.Text;

namespace LiveFastTranslation.Services.Infrastructure;

/// <summary>Minimal thread-safe file logger. It never throws into the caller.</summary>
public static class AppLogger
{
    private static readonly object Gate = new();
    private static string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LiveFastTranslation",
        "logs");

    private static string LogFilePath => Path.Combine(_directory, "app.log");

    public static string Directory => _directory;

    public static void Initialize(string logDirectory)
    {
        lock (Gate)
        {
            _directory = logDirectory;
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null)
    {
        var text = exception is null ? message : $"{message}{Environment.NewLine}{exception}";
        Write("ERROR", text);
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(_directory);
                RotateIfNeeded();
                var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(LogFilePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never break the application.
        }
    }

    private static void RotateIfNeeded()
    {
        var file = new FileInfo(LogFilePath);
        if (!file.Exists || file.Length < 2 * 1024 * 1024)
        {
            return;
        }

        var oldPath = Path.Combine(_directory, "app.old.log");
        File.Delete(oldPath);
        File.Move(LogFilePath, oldPath);
    }
}
