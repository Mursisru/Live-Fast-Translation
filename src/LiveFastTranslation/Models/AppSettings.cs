namespace LiveFastTranslation.Models;

/// <summary>Everything persisted to %AppData%\LiveFastTranslation\settings.json.</summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public ProviderSettings Provider { get; set; } = new();

    public string TargetLanguage { get; set; } = LanguageCatalog.DefaultTargetCode;

    public ReplacementSettings Replacement { get; set; } = new();

    public HotkeySettings Hotkeys { get; set; } = new();

    public AiModeSettings AiMode { get; set; } = new();

    public AppearanceSettings Appearance { get; set; } = new();

    public GeneralSettings General { get; set; } = new();

    public List<TranslationHistoryEntry> History { get; set; } = new();

    public static AppSettings CreateDefault() => new();
}
