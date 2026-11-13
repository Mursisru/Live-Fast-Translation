using LiveFastTranslation.Models;
using LiveFastTranslation.Services.Settings;
using LiveFastTranslation.Tests.Fakes;
using Xunit;

namespace LiveFastTranslation.Tests.Services;

public sealed class SettingsServiceTests
{
    [Fact]
    public void Load_WithoutFile_ReturnsNormalizedDefaults()
    {
        using var directory = new TempDirectory();
        var service = new SettingsService(directory.Path);

        var settings = service.Load();

        Assert.False(service.SettingsFileExists);
        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal(ProviderKind.OpenAi, settings.Provider.AiProvider);
        Assert.Equal(ProviderKind.OpenAi, settings.Provider.ActiveProvider);
        Assert.NotEmpty(settings.Provider.Profiles);
        Assert.All(ProviderCatalog.All, info => Assert.True(settings.Provider.Profiles.ContainsKey(info.Kind)));
        Assert.True(LanguageCatalog.IsSupported(settings.TargetLanguage));
        Assert.Equal(new HotkeySettings().TranslateSelection, settings.Hotkeys.TranslateSelection);
        Assert.Equal(ThemeCatalog.DefaultThemeId, settings.Appearance.Theme);
        Assert.NotNull(settings.History);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsAllSections()
    {
        using var directory = new TempDirectory();
        var service = new SettingsService(directory.Path);
        var settings = service.Load();

        settings.Provider.AiProvider = ProviderKind.Claude;
        var profile = settings.Provider.GetProfile(ProviderKind.Claude);
        profile.ApiKey = "sk-ant-roundtrip";
        profile.Model = "claude-test";
        profile.BaseUrl = "https://claude.example/v1";
        profile.Temperature = 0.7;
        profile.SystemPrompt = "Translate into {target_language} only.";

        settings.TargetLanguage = "de";
        settings.Hotkeys.TranslateSelection = "Ctrl+Shift+F9";
        settings.Hotkeys.ToggleAiMode = "Alt+F10";
        settings.Hotkeys.OpenSettings = "Ctrl+Alt+O";
        settings.Replacement.Strategy = ReplacementStrategy.SendKeys;
        settings.Replacement.CursorPlacement = CursorPlacement.Start;
        settings.Replacement.DelayBeforeReplaceMs = 400;
        settings.Replacement.RestoreClipboardAfterPaste = false;
        settings.AiMode.Enabled = true;
        settings.AiMode.DelayMs = 850;
        settings.AiMode.MinSentenceLength = 6;
        settings.AiMode.MaxBufferLength = 5000;
        settings.Appearance.Theme = "dark-violet";
        settings.Appearance.Opacity = 0.8;
        settings.Appearance.FontFamily = "Consolas";
        settings.Appearance.FontSize = 16;
        settings.Appearance.ShowLanguageFlags = false;
        settings.Appearance.EnableAnimations = false;
        settings.General.AutoStart = true;
        settings.General.SaveHistory = true;
        settings.General.HistoryLimit = 20;
        settings.General.AllowClipboardCapture = false;
        settings.General.ShowTrayNotifications = false;
        settings.History.Insert(0, new TranslationHistoryEntry
        {
            SourceText = "Hello",
            TranslatedText = "Hallo",
            SourceLanguage = "en",
            TargetLanguage = "de",
            Provider = "Anthropic Claude"
        });

        await service.SaveAsync();

        var loaded = new SettingsService(directory.Path).Load();
        Assert.Equal(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(ProviderKind.Claude, loaded.Provider.AiProvider);
        Assert.Equal(ProviderKind.Claude, loaded.Provider.ActiveProvider);
        Assert.Equal("sk-ant-roundtrip", loaded.Provider.GetProfile(ProviderKind.Claude).ApiKey);
        Assert.Equal("claude-test", loaded.Provider.GetProfile(ProviderKind.Claude).Model);
        Assert.Equal(0.7, loaded.Provider.GetProfile(ProviderKind.Claude).Temperature);
        Assert.Equal("de", loaded.TargetLanguage);
        Assert.Equal("Ctrl+Shift+F9", loaded.Hotkeys.TranslateSelection);
        Assert.Equal(ReplacementStrategy.SendKeys, loaded.Replacement.Strategy);
        Assert.Equal(CursorPlacement.Start, loaded.Replacement.CursorPlacement);
        Assert.Equal(400, loaded.Replacement.DelayBeforeReplaceMs);
        Assert.False(loaded.Replacement.RestoreClipboardAfterPaste);
        Assert.True(loaded.AiMode.Enabled);
        Assert.Equal(850, loaded.AiMode.DelayMs);
        Assert.Equal(6, loaded.AiMode.MinSentenceLength);
        Assert.Equal(5000, loaded.AiMode.MaxBufferLength);
        Assert.Equal("dark-violet", loaded.Appearance.Theme);
        Assert.Equal(0.8, loaded.Appearance.Opacity);
        Assert.Equal("Consolas", loaded.Appearance.FontFamily);
        Assert.Equal(16, loaded.Appearance.FontSize);
        Assert.False(loaded.Appearance.ShowLanguageFlags);
        Assert.False(loaded.Appearance.EnableAnimations);
        Assert.True(loaded.General.AutoStart);
        Assert.Equal(20, loaded.General.HistoryLimit);
        Assert.False(loaded.General.AllowClipboardCapture);
        Assert.False(loaded.General.ShowTrayNotifications);
        var history = Assert.Single(loaded.History);
        Assert.Equal("Hello", history.SourceText);
        Assert.Equal("Hallo", history.TranslatedText);
    }

    [Fact]
    public async Task ApplyAsync_RaisesChangedAndPersists()
    {
        using var directory = new TempDirectory();
        var service = new SettingsService(directory.Path);
        var settings = service.Load();
        var changes = 0;
        service.Changed += (_, _) => changes++;

        settings.TargetLanguage = "fr";
        await service.ApplyAsync(settings);

        Assert.Equal(1, changes);
        Assert.True(service.SettingsFileExists);
        Assert.Equal("fr", new SettingsService(directory.Path).Load().TargetLanguage);
    }

    [Fact]
    public void Load_CorruptFile_BacksUpAndUsesDefaults()
    {
        using var directory = new TempDirectory();
        var settingsPath = Path.Combine(directory.Path, SettingsService.SettingsFileName);
        File.WriteAllText(settingsPath, "{ this is not valid json");

        var service = new SettingsService(directory.Path);
        var settings = service.Load();

        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.True(LanguageCatalog.IsSupported(settings.TargetLanguage));
        var backups = Directory.GetFiles(directory.Path, "settings.invalid-*.json");
        Assert.Single(backups);
        Assert.Contains("this is not valid json", File.ReadAllText(backups[0]), StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_RepairsNullsInvalidValuesAndOutOfRangeNumbers()
    {
        var invalidEntry = new TranslationHistoryEntry
        {
            SourceText = "one",
            TranslatedText = "один",
            TargetLanguage = "ru"
        };
        var secondEntry = new TranslationHistoryEntry
        {
            SourceText = "two",
            TranslatedText = "два",
            TargetLanguage = "ru"
        };

        var settings = new AppSettings
        {
            Provider = new ProviderSettings
            {
                ActiveProvider = (ProviderKind)999,
                Profiles = null!
            },
            TargetLanguage = "xx-unknown",
            Replacement = new ReplacementSettings
            {
                Strategy = (ReplacementStrategy)999,
                CursorPlacement = (CursorPlacement)999,
                DelayBeforeReplaceMs = -50
            },
            Hotkeys = new HotkeySettings
            {
                TranslateSelection = null!,
                ToggleAiMode = "  Ctrl+Alt+A  ",
                OpenSettings = null!
            },
            AiMode = new AiModeSettings
            {
                DelayMs = -10,
                MinSentenceLength = 0,
                MaxBufferLength = 5
            },
            Appearance = new AppearanceSettings
            {
                Theme = "invalid-theme",
                Opacity = 5,
                FontFamily = "   ",
                FontSize = 99
            },
            General = new GeneralSettings
            {
                HistoryLimit = 2
            },
            History = new List<TranslationHistoryEntry> { null!, invalidEntry, secondEntry },
            SchemaVersion = 0
        };

        var normalized = SettingsService.Normalize(settings);

        Assert.Equal(AppSettings.CurrentSchemaVersion, normalized.SchemaVersion);
        Assert.Equal(ProviderKind.OpenAi, normalized.Provider.ActiveProvider);
        Assert.All(ProviderCatalog.All, info =>
        {
            Assert.True(normalized.Provider.Profiles.ContainsKey(info.Kind));
            Assert.False(string.IsNullOrWhiteSpace(normalized.Provider.Profiles[info.Kind].Model)
                         && info.Kind != ProviderKind.ArgosTranslate);
            Assert.False(string.IsNullOrWhiteSpace(normalized.Provider.Profiles[info.Kind].SystemPrompt));
        });
        Assert.Equal(LanguageCatalog.DefaultTargetCode, normalized.TargetLanguage);
        Assert.Equal(ReplacementStrategy.Auto, normalized.Replacement.Strategy);
        Assert.Equal(CursorPlacement.End, normalized.Replacement.CursorPlacement);
        Assert.Equal(0, normalized.Replacement.DelayBeforeReplaceMs);
        Assert.Equal(string.Empty, normalized.Hotkeys.TranslateSelection);
        Assert.Equal("Ctrl+Alt+A", normalized.Hotkeys.ToggleAiMode);
        Assert.Equal(string.Empty, normalized.Hotkeys.OpenSettings);
        Assert.Equal(150, normalized.AiMode.DelayMs);
        Assert.Equal(1, normalized.AiMode.MinSentenceLength);
        Assert.Equal(100, normalized.AiMode.MaxBufferLength);
        Assert.Equal(ThemeCatalog.DefaultThemeId, normalized.Appearance.Theme);
        Assert.Equal(1.0, normalized.Appearance.Opacity);
        Assert.Equal("Segoe UI", normalized.Appearance.FontFamily);
        Assert.Equal(32, normalized.Appearance.FontSize);
        Assert.Equal(2, normalized.History.Count);
        Assert.All(normalized.History, entry => Assert.NotNull(entry));
    }

    [Fact]
    public void Normalize_ClampsOpacityAndHistoryToLowerBounds()
    {
        var settings = new AppSettings
        {
            Appearance = new AppearanceSettings { Opacity = 0.01, FontSize = 1 },
            General = new GeneralSettings { HistoryLimit = 0 },
            History = new List<TranslationHistoryEntry>
            {
                new() { SourceText = "x", TranslatedText = "y" }
            }
        };

        var normalized = SettingsService.Normalize(settings);

        Assert.Equal(0.3, normalized.Appearance.Opacity);
        Assert.Equal(10, normalized.Appearance.FontSize);
        Assert.Empty(normalized.History);
    }

    [Fact]
    public void Clone_IsDeepAndIndependent()
    {
        var original = AppSettings.CreateDefault();
        original.Provider.GetProfile(ProviderKind.OpenAi).ApiKey = "original-key";
        original.TargetLanguage = "de";
        original.History.Add(new TranslationHistoryEntry { SourceText = "Hi", TranslatedText = "Hallo" });

        var clone = SettingsService.Clone(original);
        clone.Provider.GetProfile(ProviderKind.OpenAi).ApiKey = "clone-key";
        clone.TargetLanguage = "fr";
        clone.History.Clear();

        Assert.Equal("original-key", original.Provider.GetProfile(ProviderKind.OpenAi).ApiKey);
        Assert.Equal("de", original.TargetLanguage);
        Assert.Single(original.History);
        Assert.NotSame(original.Provider, clone.Provider);
        Assert.NotSame(original.History, clone.History);
    }
}
