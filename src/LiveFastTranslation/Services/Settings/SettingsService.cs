using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using LiveFastTranslation.Models;
using LiveFastTranslation.Services.Infrastructure;

namespace LiveFastTranslation.Services.Settings;

/// <summary>Loads, normalizes and atomically persists application settings.</summary>
public sealed class SettingsService
{
    public const string ApplicationDirectoryName = "LiveFastTranslation";
    public const string SettingsFileName = "settings.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) }
    };

    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly string _settingsFilePath;
    private AppSettings _current;

    public SettingsService(string? dataDirectory = null)
    {
        var directory = dataDirectory ?? ResolveDefaultDirectory();

        DirectoryPath = directory;
        _settingsFilePath = Path.Combine(directory, SettingsFileName);
        _current = AppSettings.CreateDefault();
    }

    /// <summary>
    /// Resolves the settings directory. The <c>LFT_DATA_DIR</c> environment variable enables a
    /// portable/test profile without touching the user's real %AppData% folder.
    /// </summary>
    private static string ResolveDefaultDirectory()
    {
        var overrideDirectory = Environment.GetEnvironmentVariable("LFT_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            try
            {
                return Path.GetFullPath(overrideDirectory.Trim());
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                AppLogger.Warn($"Некорректный LFT_DATA_DIR: {exception.Message}");
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ApplicationDirectoryName);
    }

    public string DirectoryPath { get; }

    public string SettingsFilePath => _settingsFilePath;

    public bool SettingsFileExists => File.Exists(_settingsFilePath);

    public AppSettings Current
    {
        get
        {
            lock (_stateLock)
            {
                return _current;
            }
        }
    }

    /// <summary>Raised after a user-visible settings change; runtime components re-apply their state.</summary>
    public event EventHandler? Changed;

    public AppSettings Load()
    {
        AppSettings settings;
        if (!File.Exists(_settingsFilePath))
        {
            settings = AppSettings.CreateDefault();
        }
        else
        {
            try
            {
                var json = File.ReadAllText(_settingsFilePath);
                json = MigrateLegacyProviderNames(json);
                settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions)
                           ?? AppSettings.CreateDefault();
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                AppLogger.Error("Не удалось прочитать settings.json. Используются настройки по умолчанию.", exception);
                BackupBrokenSettings();
                settings = AppSettings.CreateDefault();
            }
        }

        Normalize(settings);

        lock (_stateLock)
        {
            _current = settings;
        }

        return settings;
    }

    public void Save()
    {
        SaveAsync().GetAwaiter().GetResult();
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        AppSettings snapshot;
        lock (_stateLock)
        {
            snapshot = _current;
        }

        string json;
        try
        {
            json = JsonSerializer.Serialize(snapshot, SerializerOptions);
        }
        catch (Exception exception)
        {
            AppLogger.Error("Не удалось сериализовать настройки.", exception);
            throw;
        }

        await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var temporaryPath = _settingsFilePath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, _settingsFilePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLogger.Error("Не удалось сохранить settings.json.", exception);
            throw;
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>Applies a new settings snapshot, notifies listeners, then persists it.</summary>
    public async Task ApplyAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Normalize(settings);
        lock (_stateLock)
        {
            _current = settings;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Updates a single value from the overlay (AI toggle, target language) and persists it.</summary>
    public void Update(Action<AppSettings> mutation)
    {
        lock (_stateLock)
        {
            mutation(_current);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        PersistInBackground();
    }

    /// <summary>Mutates settings without triggering a full runtime re-apply (used by history).</summary>
    public void MutateSilently(Action<AppSettings> mutation)
    {
        lock (_stateLock)
        {
            mutation(_current);
        }
    }

    public void PersistInBackground()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await SaveAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                AppLogger.Error("Фоновая запись настроек не удалась.", exception);
            }
        });
    }

    public static AppSettings Clone(AppSettings source)
    {
        var json = JsonSerializer.Serialize(source, SerializerOptions);
        return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? AppSettings.CreateDefault();
    }

    /// <summary>Repairs missing fields, invalid language codes and out-of-range numeric values.</summary>
    public static AppSettings Normalize(AppSettings settings)
    {
        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        settings.Provider ??= new ProviderSettings();
        settings.Provider.Profiles ??= new Dictionary<ProviderKind, ProviderProfile>();

        var aiProvider = settings.Provider.AiProvider;
        if (!Enum.IsDefined(aiProvider) || aiProvider == ProviderKind.ArgosTranslate)
        {
            aiProvider = Enum.IsDefined(settings.Provider.ActiveProvider) && settings.Provider.ActiveProvider != ProviderKind.ArgosTranslate
                ? settings.Provider.ActiveProvider
                : ProviderKind.OpenAi;
        }
        settings.Provider.AiProvider = ProviderSettings.NormalizeAiProvider(aiProvider);
        settings.Provider.ActiveProvider = settings.Provider.AiProvider;

        foreach (var info in ProviderCatalog.All)
        {
            if (!settings.Provider.Profiles.TryGetValue(info.Kind, out var profile) || profile is null)
            {
                profile = new ProviderProfile
                {
                    Model = info.DefaultModel,
                    BaseUrl = info.DefaultBaseUrl,
                    Temperature = 0.2,
                    SystemPrompt = PromptDefaults.DefaultSystemPrompt
                };
                settings.Provider.Profiles[info.Kind] = profile;
            }

            profile.ApiKey = (profile.ApiKey ?? string.Empty).Trim();
            profile.BaseUrl = (profile.BaseUrl ?? string.Empty).Trim().TrimEnd('/');
            profile.Model = (profile.Model ?? string.Empty).Trim();
            if (profile.Model.Length == 0 && info.Kind != ProviderKind.ArgosTranslate)
            {
                profile.Model = info.DefaultModel;
            }

            if (string.IsNullOrWhiteSpace(profile.SystemPrompt))
            {
                profile.SystemPrompt = PromptDefaults.DefaultSystemPrompt;
            }

            profile.SystemPrompt = profile.SystemPrompt.Trim();
            profile.Temperature = Math.Clamp(profile.Temperature, 0, 2);
        }

        if (!LanguageCatalog.IsSupported(settings.TargetLanguage))
        {
            var fallback = LanguageCatalog.FindAny(settings.TargetLanguage)?.Code;
            if (!string.IsNullOrWhiteSpace(fallback) && LanguageCatalog.IsSupported(fallback))
            {
                settings.TargetLanguage = fallback;
            }
            else
            {
                settings.TargetLanguage = LanguageCatalog.DefaultTargetCode;
            }
        }

        settings.Replacement ??= new ReplacementSettings();
        if (!Enum.IsDefined(settings.Replacement.Strategy))
        {
            settings.Replacement.Strategy = ReplacementStrategy.Auto;
        }

        if (!Enum.IsDefined(settings.Replacement.CursorPlacement))
        {
            settings.Replacement.CursorPlacement = CursorPlacement.End;
        }

        settings.Replacement.DelayBeforeReplaceMs = Math.Clamp(settings.Replacement.DelayBeforeReplaceMs, 0, 5000);

        settings.Hotkeys ??= new HotkeySettings();
        settings.Hotkeys.TranslateSelection = (settings.Hotkeys.TranslateSelection ?? string.Empty).Trim();
        settings.Hotkeys.ToggleAiMode = (settings.Hotkeys.ToggleAiMode ?? string.Empty).Trim();
        settings.Hotkeys.OpenSettings = (settings.Hotkeys.OpenSettings ?? string.Empty).Trim();

        settings.AiMode ??= new AiModeSettings();
        settings.AiMode.DelayMs = Math.Clamp(settings.AiMode.DelayMs, 150, 5000);
        settings.AiMode.MinSentenceLength = Math.Clamp(settings.AiMode.MinSentenceLength, 1, 500);
        settings.AiMode.MaxBufferLength = Math.Clamp(settings.AiMode.MaxBufferLength, 100, 20000);

        settings.Appearance ??= new AppearanceSettings();
        settings.Appearance.Theme = ThemeCatalog.NormalizeId(settings.Appearance.Theme);
        settings.Appearance.Opacity = Math.Clamp(settings.Appearance.Opacity, 0.3, 1.0);
        settings.Appearance.FontFamily = string.IsNullOrWhiteSpace(settings.Appearance.FontFamily)
            ? "Segoe UI"
            : settings.Appearance.FontFamily.Trim();
        settings.Appearance.FontSize = Math.Clamp(settings.Appearance.FontSize, 10, 32);

        settings.General ??= new GeneralSettings();
        settings.General.HistoryLimit = Math.Clamp(settings.General.HistoryLimit, 0, 500);

        settings.History ??= new List<TranslationHistoryEntry>();
        settings.History.RemoveAll(entry => entry is null);
        if (settings.General.HistoryLimit == 0)
        {
            settings.History.Clear();
        }
        else if (settings.History.Count > settings.General.HistoryLimit)
        {
            settings.History.RemoveRange(settings.General.HistoryLimit, settings.History.Count - settings.General.HistoryLimit);
        }

        return settings;
    }

    private static string MigrateLegacyProviderNames(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        // Migrate legacy provider names to Argos Translate in persisted settings.
        return json
            .Replace("\"DeepL\"", "\"ArgosTranslate\"", StringComparison.Ordinal)
            .Replace("\"LibreTranslate\"", "\"ArgosTranslate\"", StringComparison.Ordinal);
    }

    private void BackupBrokenSettings()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return;
            }

            var backupName = $"settings.invalid-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            File.Copy(_settingsFilePath, Path.Combine(DirectoryPath, backupName), overwrite: false);
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"Не удалось создать резервную копию повреждённых настроек: {exception.Message}");
        }
    }
}
