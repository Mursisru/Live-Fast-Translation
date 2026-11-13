using Microsoft.Win32;
using LiveFastTranslation.Services.Infrastructure;

namespace LiveFastTranslation.Services.Settings;

/// <summary>Manages the per-user autostart entry in HKCU\Software\Microsoft\Windows\CurrentVersion\Run.</summary>
public sealed class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LiveFastTranslation";

    public string ExecutablePath
    {
        get
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            }

            return path;
        }
    }

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"Не удалось прочитать автозапуск: {exception.Message}");
            return false;
        }
    }

    public bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return false;
            }

            if (enabled)
            {
                var executable = ExecutablePath;
                if (string.IsNullOrWhiteSpace(executable))
                {
                    return false;
                }

                key.SetValue(ValueName, $"\"{executable}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception exception)
        {
            AppLogger.Error("Не удалось изменить автозапуск.", exception);
            return false;
        }
    }
}
