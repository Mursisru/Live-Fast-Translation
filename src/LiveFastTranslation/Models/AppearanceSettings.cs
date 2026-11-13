namespace LiveFastTranslation.Models;

public sealed class AppearanceSettings
{
    /// <summary>Theme identifier from ThemeCatalog (64 themes: 32 light + 32 dark).</summary>
    public string Theme { get; set; } = ThemeCatalog.DefaultThemeId;

    public double Opacity { get; set; } = 0.96;

    public string FontFamily { get; set; } = "Segoe UI";

    public double FontSize { get; set; } = 13.5;

    public bool ShowLanguageFlags { get; set; } = true;

    public bool ShowLanguageCodes { get; set; } = true;

    public bool EnableAnimations { get; set; } = true;
}
