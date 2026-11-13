using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LiveFastTranslation.Models;
using LiveFastTranslation.Services.History;
using LiveFastTranslation.Services.Infrastructure;
using LiveFastTranslation.Services.Input;
using LiveFastTranslation.Services.Settings;
using LiveFastTranslation.Services.Translation;
using Brush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;

namespace LiveFastTranslation.ViewModels;

public enum SettingsSection
{
    Provider,
    Replacement,
    Hotkeys,
    AiMode,
    Appearance,
    Advanced
}

public enum TestStatusKind
{
    None,
    Pending,
    Success,
    Error
}

public sealed record SettingsSectionOption(SettingsSection Value, string IconPath, string Title, string Description, string Keywords);

public sealed record NamedOption<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Editable copy of AppSettings with validation, provider testing and saving.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private static readonly IReadOnlyList<ProviderInfo> AiProvidersList = ProviderCatalog.All
        .Where(provider => provider.Kind != ProviderKind.ArgosTranslate)
        .ToList();

    private static readonly IReadOnlyList<NamedOption<ReplacementStrategy>> StrategyOptionsList = new[]
    {
        new NamedOption<ReplacementStrategy>(ReplacementStrategy.Auto, "Автоматически (рекомендуется)"),
        new NamedOption<ReplacementStrategy>(ReplacementStrategy.UiAutomation, "UI Automation (ValuePattern)"),
        new NamedOption<ReplacementStrategy>(ReplacementStrategy.Win32Message, "Win32 (EM_REPLACESEL)"),
        new NamedOption<ReplacementStrategy>(ReplacementStrategy.SendKeys, "Эмуляция клавиатуры (SendKeys)"),
        new NamedOption<ReplacementStrategy>(ReplacementStrategy.Clipboard, "Буфер обмена (Ctrl+V)")
    };

    private static readonly IReadOnlyList<NamedOption<CursorPlacement>> CursorPlacementOptionsList = new[]
    {
        new NamedOption<CursorPlacement>(CursorPlacement.End, "В конце вставленного текста"),
        new NamedOption<CursorPlacement>(CursorPlacement.Start, "В начале вставленного текста"),
        new NamedOption<CursorPlacement>(CursorPlacement.SelectAll, "Выделить вставленный текст")
    };

    private static readonly IReadOnlyList<SettingsSectionOption> SectionsList = new[]
    {
        new SettingsSectionOption(
            SettingsSection.Provider,
            "M17.5 19H9a7 7 0 1 1 6.71-9h1.79a4.5 4.5 0 1 1 0 9Z",
            "Провайдер",
            "Argos Translate, ИИ-сервис, модель и язык перевода",
            "провайдер перевод argos ai openai claude gemini opencode модель api ключ язык"),
        new SettingsSectionOption(
            SettingsSection.Replacement,
            "M8 3 4 7l4 4 M4 7h16 M16 21l4-4-4-4 M20 17H4",
            "Замена текста",
            "Способ вставки перевода и буфер обмена",
            "замена текст вставка буфер обмена курсор задержка стратегия"),
        new SettingsSectionOption(
            SettingsSection.Hotkeys,
            "M3 6h18v12H3z M7 10h.01 M11 10h.01 M15 10h.01 M7 14h10",
            "Горячие клавиши",
            "Глобальные сочетания клавиш",
            "горячие клавиши хоткей сочетание клавиатура"),
        new SettingsSectionOption(
            SettingsSection.AiMode,
            "M12 3l1.9 5.1L19 10l-5.1 1.9L12 17l-1.9-5.1L5 10l5.1-1.9L12 3Z M19 3v3 M20.5 4.5h-3",
            "AI-режим",
            "Перевод предложений во время набора",
            "ai режим задержка предложение буфер набор"),
        new SettingsSectionOption(
            SettingsSection.Appearance,
            "M12 3a9 9 0 1 0 9 9 7 7 0 0 1-9-9Z",
            "Оформление",
            "64 темы, шрифт и прозрачность",
            "тема оформление шрифт прозрачность размер язык флаги анимации"),
        new SettingsSectionOption(
            SettingsSection.Advanced,
            "M4 6h10 M18 6h2 M4 12h4 M12 12h8 M4 18h12 M20 18h0 M16 4a2 2 0 1 0 0 4 2 2 0 0 0 0-4Z M10 10a2 2 0 1 0 0 4 2 2 0 0 0 0-4Z M18 16a2 2 0 1 0 0 4 2 2 0 0 0 0-4Z",
            "Дополнительно",
            "Автозапуск, трей, история и файлы",
            "автозапуск трей история журнал папка версия служебные файлы")
    };

    private static readonly IReadOnlyList<string> FontFamiliesList = new[]
    {
        "Segoe UI",
        "Segoe UI Variable Text",
        "Calibri",
        "Arial",
        "Verdana",
        "Tahoma",
        "Georgia",
        "Trebuchet MS",
        "Consolas",
        "Cascadia Mono",
        "Times New Roman"
    };

    private readonly SettingsService _settings;
    private readonly HistoryService _historyService;
    private readonly AutostartService _autostartService;
    private readonly HttpClient _httpClient;
    private readonly AppSettings _draft;
    private readonly AsyncRelayCommand _saveCommand;
    private readonly AsyncRelayCommand _testArgosTranslateCommand;
    private readonly AsyncRelayCommand _testAiProviderCommand;
    private readonly AsyncRelayCommand _fetchAiModelsCommand;

    private readonly ObservableCollection<SettingsSectionOption> _visibleSections = new();
    private CancellationTokenSource? _aiModelsCts;

    private SettingsSection _selectedSection = SettingsSection.Provider;
    private string _settingsSearchText = string.Empty;
    private ProviderInfo _selectedAiProvider;
    private LanguageOption _targetLanguage;
    private ThemeDefinition _selectedTheme;
    private NamedOption<ReplacementStrategy> _selectedStrategy;
    private NamedOption<CursorPlacement> _selectedCursorPlacement;
    private string _statusMessage = string.Empty;
    private bool _statusIsSuccess;
    private string _hotkeyValidationMessage = string.Empty;
    private string _argosTranslateTestStatus = string.Empty;
    private Brush _argosTranslateTestStatusBrush = MediaBrushes.Gray;
    private string _aiTestStatus = string.Empty;
    private Brush _aiTestStatusBrush = MediaBrushes.Gray;
    private TestStatusKind _argosTestKind = TestStatusKind.None;
    private TestStatusKind _aiTestKind = TestStatusKind.None;
    private bool _isTestingArgosTranslate;
    private bool _isTestingAi;
    private bool _isRefreshingHistory;
    private bool _isFetchingAiModels;
    private string _aiModelsStatus = string.Empty;

    public SettingsViewModel(
        SettingsService settings,
        HistoryService historyService,
        AutostartService autostartService,
        HttpClient httpClient)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _autostartService = autostartService ?? throw new ArgumentNullException(nameof(autostartService));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        _draft = SettingsService.Clone(settings.Current);
        _draft.History = settings.Current.History;
        _selectedAiProvider = ProviderCatalog.Get(_draft.Provider.AiProvider);
        if (_selectedAiProvider.Kind == ProviderKind.ArgosTranslate)
        {
            _selectedAiProvider = ProviderCatalog.Get(ProviderKind.OpenAi);
            _draft.Provider.AiProvider = ProviderKind.OpenAi;
        }

        _targetLanguage = LanguageCatalog.Find(_draft.TargetLanguage)
                          ?? LanguageCatalog.All.FirstOrDefault()
                          ?? LanguageCatalog.FindAny("en")
                          ?? new LanguageOption("en", "English", "English", "🇬🇧");
        _selectedTheme = ThemeCatalog.Get(_draft.Appearance.Theme);
        _selectedStrategy = StrategyOptionsList.First(option => option.Value == _draft.Replacement.Strategy);
        _selectedCursorPlacement = CursorPlacementOptionsList.First(option => option.Value == _draft.Replacement.CursorPlacement);

        _saveCommand = new AsyncRelayCommand(SaveAsync);
        _testArgosTranslateCommand = new AsyncRelayCommand(TestArgosTranslateConnectionAsync);
        _testAiProviderCommand = new AsyncRelayCommand(TestAiProviderConnectionAsync);
        _fetchAiModelsCommand = new AsyncRelayCommand(FetchAiModelsAsync);

        SaveCommand = _saveCommand;
        TestArgosTranslateConnectionCommand = _testArgosTranslateCommand;
        TestAiProviderConnectionCommand = _testAiProviderCommand;
        FetchAiModelsCommand = _fetchAiModelsCommand;
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
        ResetAiPromptCommand = new RelayCommand(() => AiSystemPrompt = PromptDefaults.DefaultSystemPrompt);
        ResetHotkeysCommand = new RelayCommand(ResetHotkeys);
        ClearHistoryCommand = new RelayCommand(ClearHistory);
        OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
        OpenLogCommand = new RelayCommand(OpenLog);
        ClearSearchCommand = new RelayCommand(() => SettingsSearchText = string.Empty);

        RefreshHistory();
        ValidateHotkeys();
        SeedModelLists();
        RefreshSectionFilter();
        _ = FetchAiModelsAsync();
    }

    public event EventHandler? Saved;
    public event EventHandler? CloseRequested;
    public Func<string, string, bool>? Confirm { get; set; }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand TestArgosTranslateConnectionCommand { get; }
    public ICommand TestAiProviderConnectionCommand { get; }
    public ICommand FetchAiModelsCommand { get; }
    public ICommand ResetAiPromptCommand { get; }
    public ICommand ResetHotkeysCommand { get; }
    public ICommand ClearHistoryCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenLogCommand { get; }
    public ICommand ClearSearchCommand { get; }

    public IReadOnlyList<SettingsSectionOption> Sections => SectionsList;
    public ObservableCollection<SettingsSectionOption> VisibleSections => _visibleSections;
    public IReadOnlyList<ProviderInfo> AiProviders => AiProvidersList;
    public IReadOnlyList<ThemeDefinition> Themes => ThemeCatalog.All;

    public ObservableCollection<string> AiAvailableModels { get; } = new();
    public ObservableCollection<string> ArgosAvailableModels { get; } = new();

    public string SettingsSearchText
    {
        get => _settingsSearchText;
        set
        {
            if (SetProperty(ref _settingsSearchText, value ?? string.Empty))
            {
                RefreshSectionFilter();
                OnPropertyChanged(nameof(HasSearchText));
            }
        }
    }

    public bool HasSearchText => !string.IsNullOrWhiteSpace(SettingsSearchText);
    public bool HasVisibleSections => _visibleSections.Count > 0;

    public SettingsSection SelectedSection
    {
        get => _selectedSection;
        set => SetProperty(ref _selectedSection, value);
    }

    private void RefreshSectionFilter()
    {
        var query = SettingsSearchText.Trim();
        _visibleSections.Clear();

        foreach (var section in SectionsList)
        {
            if (query.Length == 0
                || section.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || section.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || section.Keywords.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            {
                _visibleSections.Add(section);
            }
        }

        OnPropertyChanged(nameof(HasVisibleSections));

        if (_visibleSections.Count > 0 && !_visibleSections.Any(section => section.Value == SelectedSection))
        {
            SelectedSection = _visibleSections[0].Value;
        }
    }

    /// <summary>True when the draft differs from the saved settings (used to guard window close).</summary>
    public bool HasUnsavedChanges()
    {
        try
        {
            return JsonSerializer.Serialize(_draft) != JsonSerializer.Serialize(_settings.Current);
        }
        catch (Exception)
        {
            return true;
        }
    }

    public bool AiModeEnabledInDraft
    {
        get => _draft.AiMode.Enabled;
        set
        {
            if (_draft.AiMode.Enabled == value)
            {
                return;
            }

            _draft.AiMode.Enabled = value;
            OnPropertiesChanged(nameof(AiModeEnabledInDraft), nameof(TranslationModeDescription));
        }
    }

    public string TranslationModeDescription => _draft.AiMode.Enabled
        ? $"AI-режим включён: перевод выполняется через {_selectedAiProvider.DisplayName}."
        : "AI-режим выключен: стандартный локальный перевод выполняется через Argos Translate.";

    public ProviderInfo ArgosTranslateProviderInfo => ProviderCatalog.Get(ProviderKind.ArgosTranslate);

    public string ArgosTranslateModelType
    {
        get => _draft.Provider.GetProfile(ProviderKind.ArgosTranslate).Model;
        set
        {
            var profile = _draft.Provider.GetProfile(ProviderKind.ArgosTranslate);
            var normalized = value ?? string.Empty;
            if (profile.Model == normalized) return;
            profile.Model = normalized;
            OnPropertyChanged();
        }
    }

    public ProviderInfo SelectedAiProvider
    {
        get => _selectedAiProvider;
        set
        {
            if (value is null || value.Kind == ProviderKind.ArgosTranslate || !SetProperty(ref _selectedAiProvider, value))
            {
                return;
            }

            _draft.Provider.AiProvider = value.Kind;
            ClearAiTestStatus();
            AiModelsStatus = string.Empty;
            SeedAiModelList();
            OnPropertiesChanged(
                nameof(AiApiKey),
                nameof(AiModel),
                nameof(AiBaseUrl),
                nameof(AiSystemPrompt),
                nameof(AiTemperature),
                nameof(AiTemperatureText),
                nameof(AiProviderDescription),
                nameof(AiApiKeyHint),
                nameof(AiModelHint),
                nameof(AiBaseUrlHint),
                nameof(AiRequiresApiKey),
                nameof(ShowAiApiKey),
                nameof(ShowAiBaseUrl),
                nameof(ShowAiModel),
                nameof(ShowAiSystemPrompt),
                nameof(ShowAiTemperature),
                nameof(TranslationModeDescription));
            _ = FetchAiModelsAsync();
        }
    }

    public string AiProviderDescription => SelectedAiProvider.Description;
    public bool AiRequiresApiKey => SelectedAiProvider.RequiresApiKey;
    public bool ShowAiApiKey => SelectedAiProvider.RequiresApiKey || SelectedAiProvider.Kind == ProviderKind.OpenCode || SelectedAiProvider.Kind == ProviderKind.OpenAiCompatible;
    public bool ShowAiBaseUrl => true;
    public bool ShowAiModel => true;
    public bool ShowAiSystemPrompt => true;
    public bool ShowAiTemperature => true;

    public string AiModelHint => SelectedAiProvider.ModelHint + " Можно выбрать из списка или ввести ID вручную.";
    public string AiBaseUrlHint => SelectedAiProvider.BaseUrlHint;

    public string AiApiKeyHint
    {
        get
        {
            var example = ProviderCatalog.ExampleApiKey(SelectedAiProvider.Kind);
            return SelectedAiProvider.RequiresApiKey
                ? $"Формат: {example}. Ключ хранится только в settings.json на этом компьютере."
                : SelectedAiProvider.Kind == ProviderKind.OpenCode || SelectedAiProvider.Kind == ProviderKind.OpenAiCompatible
                    ? "Необязательно. Если шлюз/сервер не требует авторизацию, оставьте пустым — заголовок Authorization не будет добавлен."
                    : "Для локального сервера ключ обычно не нужен. Если сервер требует авторизацию — вставьте ключ.";
        }
    }

    private ProviderProfile AiProfile => _draft.Provider.GetProfile(SelectedAiProvider.Kind);

    public string AiApiKey
    {
        get => AiProfile.ApiKey;
        set { if (AiProfile.ApiKey == (value ?? string.Empty)) return; AiProfile.ApiKey = value ?? string.Empty; OnPropertyChanged(); }
    }

    public string AiModel
    {
        get => AiProfile.Model;
        set { if (AiProfile.Model == (value ?? string.Empty)) return; AiProfile.Model = value ?? string.Empty; OnPropertyChanged(); }
    }

    public string AiBaseUrl
    {
        get => AiProfile.BaseUrl;
        set { if (AiProfile.BaseUrl == (value ?? string.Empty)) return; AiProfile.BaseUrl = value ?? string.Empty; OnPropertyChanged(); }
    }

    public string AiSystemPrompt
    {
        get => AiProfile.SystemPrompt;
        set { if (AiProfile.SystemPrompt == (value ?? string.Empty)) return; AiProfile.SystemPrompt = value ?? string.Empty; OnPropertyChanged(); }
    }

    public double AiTemperature
    {
        get => AiProfile.Temperature;
        set
        {
            var clamped = Math.Clamp(value, 0, 2);
            if (Math.Abs(AiProfile.Temperature - clamped) < 0.0001) return;
            AiProfile.Temperature = clamped;
            OnPropertiesChanged(nameof(AiTemperature), nameof(AiTemperatureText));
        }
    }

    public string AiTemperatureText => AiTemperature.ToString("0.00");

    public IReadOnlyList<LanguageOption> Languages => LanguageCatalog.All;

    public LanguageOption TargetLanguage
    {
        get => _targetLanguage;
        set
        {
            if (value is null || !SetProperty(ref _targetLanguage, value)) return;
            _draft.TargetLanguage = value.Code;
        }
    }

    public string AvailableLanguagesHint => LanguageCatalog.All.Count == 0
        ? "Установленные языки не обнаружены. Поместите пакеты Argos в папку argos-packages проекта."
        : $"Обнаружено установленных языков: {LanguageCatalog.All.Count}.";

    public IReadOnlyList<NamedOption<ReplacementStrategy>> StrategyOptions => StrategyOptionsList;

    public NamedOption<ReplacementStrategy> SelectedStrategy
    {
        get => _selectedStrategy;
        set
        {
            if (value is null || !SetProperty(ref _selectedStrategy, value)) return;
            _draft.Replacement.Strategy = value.Value;
        }
    }

    public double ReplacementDelayMs
    {
        get => _draft.Replacement.DelayBeforeReplaceMs;
        set
        {
            var rounded = (int)Math.Round(value);
            if (rounded == _draft.Replacement.DelayBeforeReplaceMs) return;
            _draft.Replacement.DelayBeforeReplaceMs = Math.Clamp(rounded, 0, 2000);
            OnPropertiesChanged(nameof(ReplacementDelayMs), nameof(ReplacementDelayText));
        }
    }

    public string ReplacementDelayText => $"{_draft.Replacement.DelayBeforeReplaceMs} мс";

    public IReadOnlyList<NamedOption<CursorPlacement>> CursorPlacementOptions => CursorPlacementOptionsList;

    public NamedOption<CursorPlacement> SelectedCursorPlacement
    {
        get => _selectedCursorPlacement;
        set
        {
            if (value is null || !SetProperty(ref _selectedCursorPlacement, value)) return;
            _draft.Replacement.CursorPlacement = value.Value;
        }
    }

    public bool RestoreClipboardAfterPaste
    {
        get => _draft.Replacement.RestoreClipboardAfterPaste;
        set { if (_draft.Replacement.RestoreClipboardAfterPaste == value) return; _draft.Replacement.RestoreClipboardAfterPaste = value; OnPropertyChanged(); }
    }

    public bool ActivateTargetWindow
    {
        get => _draft.Replacement.ActivateTargetWindow;
        set { if (_draft.Replacement.ActivateTargetWindow == value) return; _draft.Replacement.ActivateTargetWindow = value; OnPropertyChanged(); }
    }

    public bool MoveCaretAfterReplace
    {
        get => _draft.Replacement.MoveCaretAfterReplace;
        set { if (_draft.Replacement.MoveCaretAfterReplace == value) return; _draft.Replacement.MoveCaretAfterReplace = value; OnPropertyChanged(); }
    }

    public bool AllowClipboardCapture
    {
        get => _draft.General.AllowClipboardCapture;
        set { if (_draft.General.AllowClipboardCapture == value) return; _draft.General.AllowClipboardCapture = value; OnPropertyChanged(); }
    }

    public string TranslateHotkey
    {
        get => _draft.Hotkeys.TranslateSelection;
        set { var n = value ?? string.Empty; if (_draft.Hotkeys.TranslateSelection == n) return; _draft.Hotkeys.TranslateSelection = n; OnPropertyChanged(); ValidateHotkeys(); }
    }

    public string AiHotkey
    {
        get => _draft.Hotkeys.ToggleAiMode;
        set { var n = value ?? string.Empty; if (_draft.Hotkeys.ToggleAiMode == n) return; _draft.Hotkeys.ToggleAiMode = n; OnPropertyChanged(); ValidateHotkeys(); }
    }

    public string SettingsHotkey
    {
        get => _draft.Hotkeys.OpenSettings;
        set { var n = value ?? string.Empty; if (_draft.Hotkeys.OpenSettings == n) return; _draft.Hotkeys.OpenSettings = n; OnPropertyChanged(); ValidateHotkeys(); }
    }

    public string HotkeyValidationMessage
    {
        get => _hotkeyValidationMessage;
        private set { if (SetProperty(ref _hotkeyValidationMessage, value)) OnPropertyChanged(nameof(HasHotkeyErrors)); }
    }

    public bool HasHotkeyErrors => !string.IsNullOrWhiteSpace(HotkeyValidationMessage);

    public double AiDelayMs
    {
        get => _draft.AiMode.DelayMs;
        set { var r = (int)Math.Round(value); if (r == _draft.AiMode.DelayMs) return; _draft.AiMode.DelayMs = Math.Clamp(r, 150, 3000); OnPropertiesChanged(nameof(AiDelayMs), nameof(AiDelayText)); }
    }
    public string AiDelayText => $"{_draft.AiMode.DelayMs} мс";

    public double AiMinLength
    {
        get => _draft.AiMode.MinSentenceLength;
        set { var r = (int)Math.Round(value); if (r == _draft.AiMode.MinSentenceLength) return; _draft.AiMode.MinSentenceLength = Math.Clamp(r, 1, 100); OnPropertiesChanged(nameof(AiMinLength), nameof(AiMinLengthText)); }
    }
    public string AiMinLengthText => $"{_draft.AiMode.MinSentenceLength} симв.";

    public double AiMaxBuffer
    {
        get => _draft.AiMode.MaxBufferLength;
        set { var r = (int)Math.Round(value); if (r == _draft.AiMode.MaxBufferLength) return; _draft.AiMode.MaxBufferLength = Math.Clamp(r, 500, 10000); OnPropertiesChanged(nameof(AiMaxBuffer), nameof(AiMaxBufferText)); }
    }
    public string AiMaxBufferText => $"{_draft.AiMode.MaxBufferLength:N0} симв.";

    public ThemeDefinition SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (value is null || !SetProperty(ref _selectedTheme, value)) return;
            _draft.Appearance.Theme = value.Id;
            OnPropertyChanged(nameof(SelectedThemeId));
        }
    }

    public string SelectedThemeId => _selectedTheme.Id;

    public double OpacityPercent
    {
        get => _draft.Appearance.Opacity;
        set { var c = Math.Clamp(value, 0.3, 1.0); if (Math.Abs(_draft.Appearance.Opacity - c) < 0.0001) return; _draft.Appearance.Opacity = c; OnPropertiesChanged(nameof(OpacityPercent), nameof(OpacityText)); }
    }
    public string OpacityText => $"{_draft.Appearance.Opacity:P0}";

    public IReadOnlyList<string> FontFamilies => FontFamiliesList;

    public string FontFamilyName
    {
        get => _draft.Appearance.FontFamily;
        set { var n = value ?? string.Empty; if (_draft.Appearance.FontFamily == n) return; _draft.Appearance.FontFamily = n; OnPropertyChanged(); }
    }

    public double OverlayFontSize
    {
        get => _draft.Appearance.FontSize;
        set { var c = Math.Clamp(value, 10, 28); if (Math.Abs(_draft.Appearance.FontSize - c) < 0.0001) return; _draft.Appearance.FontSize = c; OnPropertiesChanged(nameof(OverlayFontSize), nameof(OverlayFontSizeText)); }
    }
    public string OverlayFontSizeText => $"{_draft.Appearance.FontSize:0.#} pt";

    public bool ShowLanguageFlags
    {
        get => _draft.Appearance.ShowLanguageFlags;
        set { if (_draft.Appearance.ShowLanguageFlags == value) return; _draft.Appearance.ShowLanguageFlags = value; OnPropertyChanged(); }
    }

    public bool ShowLanguageCodes
    {
        get => _draft.Appearance.ShowLanguageCodes;
        set { if (_draft.Appearance.ShowLanguageCodes == value) return; _draft.Appearance.ShowLanguageCodes = value; OnPropertyChanged(); }
    }

    public bool EnableAnimations
    {
        get => _draft.Appearance.EnableAnimations;
        set { if (_draft.Appearance.EnableAnimations == value) return; _draft.Appearance.EnableAnimations = value; OnPropertyChanged(); }
    }

    public bool AutoStart
    {
        get => _draft.General.AutoStart;
        set { if (_draft.General.AutoStart == value) return; _draft.General.AutoStart = value; OnPropertyChanged(); }
    }

    public bool ShowTrayNotifications
    {
        get => _draft.General.ShowTrayNotifications;
        set { if (_draft.General.ShowTrayNotifications == value) return; _draft.General.ShowTrayNotifications = value; OnPropertyChanged(); }
    }

    public bool SaveHistory
    {
        get => _draft.General.SaveHistory;
        set { if (_draft.General.SaveHistory == value) return; _draft.General.SaveHistory = value; OnPropertyChanged(); }
    }

    public double HistoryLimit
    {
        get => _draft.General.HistoryLimit;
        set { var r = (int)Math.Round(value); if (r == _draft.General.HistoryLimit) return; _draft.General.HistoryLimit = Math.Clamp(r, 0, 200); OnPropertiesChanged(nameof(HistoryLimit), nameof(HistoryLimitText)); }
    }
    public string HistoryLimitText => _draft.General.HistoryLimit == 0 ? "История отключена" : $"{_draft.General.HistoryLimit} записей";

    public ObservableCollection<TranslationHistoryEntry> HistoryEntries { get; } = new();
    public string HistoryCountText => HistoryEntries.Count == 0
        ? "История пуста"
        : $"Сохранено записей: {HistoryEntries.Count}";
    public bool HasHistory => HistoryEntries.Count > 0;
    public string SettingsPath => _settings.SettingsFilePath;
    public string VersionText
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "1.0.0" : version.ToString(3);
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set { if (SetProperty(ref _statusMessage, value)) OnPropertyChanged(nameof(HasStatusMessage)); }
    }
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool StatusIsSuccess { get => _statusIsSuccess; private set => SetProperty(ref _statusIsSuccess, value); }

    public string ArgosTranslateTestStatus
    {
        get => _argosTranslateTestStatus;
        private set { if (SetProperty(ref _argosTranslateTestStatus, value)) OnPropertyChanged(nameof(HasArgosTranslateTestStatus)); }
    }
    public bool HasArgosTranslateTestStatus => !string.IsNullOrWhiteSpace(ArgosTranslateTestStatus);
    public Brush ArgosTranslateTestStatusBrush { get => _argosTranslateTestStatusBrush; private set => SetProperty(ref _argosTranslateTestStatusBrush, value); }
    public bool IsTestingArgosTranslate { get => _isTestingArgosTranslate; private set { if (SetProperty(ref _isTestingArgosTranslate, value)) OnPropertyChanged(nameof(IsNotTestingArgosTranslate)); } }
    public bool IsNotTestingArgosTranslate => !IsTestingArgosTranslate;
    public TestStatusKind ArgosTestKind { get => _argosTestKind; private set { if (SetProperty(ref _argosTestKind, value)) { OnPropertiesChanged(nameof(ShowArgosTestBadge), nameof(ArgosTestBadgeText), nameof(ArgosTestBadgeBrush)); } } }
    public bool ShowArgosTestBadge => ArgosTestKind != TestStatusKind.None;
    public string ArgosTestBadgeText => ArgosTestKind switch { TestStatusKind.Success => "Успешно", TestStatusKind.Error => "Ошибка", TestStatusKind.Pending => "Проверка", _ => string.Empty };
    public Brush ArgosTestBadgeBrush => ResolveStatusBrush(ArgosTestKind);

    public string AiTestStatus
    {
        get => _aiTestStatus;
        private set { if (SetProperty(ref _aiTestStatus, value)) OnPropertyChanged(nameof(HasAiTestStatus)); }
    }
    public bool HasAiTestStatus => !string.IsNullOrWhiteSpace(AiTestStatus);
    public Brush AiTestStatusBrush { get => _aiTestStatusBrush; private set => SetProperty(ref _aiTestStatusBrush, value); }
    public bool IsTestingAi { get => _isTestingAi; private set { if (SetProperty(ref _isTestingAi, value)) OnPropertyChanged(nameof(IsNotTestingAi)); } }
    public bool IsNotTestingAi => !IsTestingAi;
    public TestStatusKind AiTestKind { get => _aiTestKind; private set { if (SetProperty(ref _aiTestKind, value)) { OnPropertiesChanged(nameof(ShowAiTestBadge), nameof(AiTestBadgeText), nameof(AiTestBadgeBrush)); } } }
    public bool ShowAiTestBadge => AiTestKind != TestStatusKind.None;
    public string AiTestBadgeText => AiTestKind switch { TestStatusKind.Success => "Успешно", TestStatusKind.Error => "Ошибка", TestStatusKind.Pending => "Проверка", _ => string.Empty };
    public Brush AiTestBadgeBrush => ResolveStatusBrush(AiTestKind);

    public bool IsFetchingAiModels { get => _isFetchingAiModels; private set { if (SetProperty(ref _isFetchingAiModels, value)) OnPropertyChanged(nameof(IsNotFetchingAiModels)); } }
    public bool IsNotFetchingAiModels => !IsFetchingAiModels;

    public string AiModelsStatus { get => _aiModelsStatus; private set { if (SetProperty(ref _aiModelsStatus, value)) OnPropertyChanged(nameof(HasAiModelsStatus)); } }
    public bool HasAiModelsStatus => !string.IsNullOrWhiteSpace(AiModelsStatus);

    private void SeedModelLists()
    {
        SeedArgosModelList();
        SeedAiModelList();
    }

    private void SeedArgosModelList()
    {
        ArgosAvailableModels.Clear();
        foreach (var item in ArgosTranslateProviderInfo.SuggestedModels.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ArgosAvailableModels.Add(item);
        }
        if (!string.IsNullOrWhiteSpace(ArgosTranslateModelType) && !ArgosAvailableModels.Contains(ArgosTranslateModelType, StringComparer.OrdinalIgnoreCase))
        {
            ArgosAvailableModels.Insert(0, ArgosTranslateModelType.Trim());
        }
    }

    private void SeedAiModelList()
    {
        AiAvailableModels.Clear();
        var profile = AiProfile;
        var seeded = SelectedAiProvider.SuggestedModels
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!string.IsNullOrWhiteSpace(profile.Model) && !seeded.Contains(profile.Model, StringComparer.OrdinalIgnoreCase))
        {
            seeded.Insert(0, profile.Model.Trim());
        }
        foreach (var model in seeded)
        {
            AiAvailableModels.Add(model);
        }
    }

    private async Task FetchAiModelsAsync()
    {
        var requestedKind = SelectedAiProvider.Kind;
        var info = SelectedAiProvider;
        var profile = AiProfile.Clone();

        // A provider switch must never be blocked by a request started for the previous provider.
        _aiModelsCts?.Cancel();
        _aiModelsCts?.Dispose();
        _aiModelsCts = null;
        IsFetchingAiModels = false;

        var baseUrl = string.IsNullOrWhiteSpace(profile.BaseUrl) ? info.DefaultBaseUrl : profile.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            if (SelectedAiProvider.Kind == requestedKind)
            {
                SeedAiModelList();
                AiModelsStatus = "Укажите Base URL для загрузки списка моделей.";
            }
            return;
        }

        // Gemini/Claude have provider-specific models endpoints; fall back to suggested/current models.
        if (info.Kind is ProviderKind.Gemini or ProviderKind.Claude)
        {
            if (SelectedAiProvider.Kind == requestedKind)
            {
                SeedAiModelList();
                AiModelsStatus = info.Kind == ProviderKind.Gemini
                    ? "Для Gemini используйте ручной ввод/выбор из предложенных моделей."
                    : "Для Claude используйте ручной ввод/выбор из предложенных моделей.";
            }
            return;
        }

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        _aiModelsCts = cts;
        IsFetchingAiModels = true;
        AiModelsStatus = "Загрузка списка моделей…";
        try
        {
            var models = await ProviderHttp.FetchModelsAsync(_httpClient, baseUrl, profile.ApiKey, cts.Token);
            if (cts.IsCancellationRequested || SelectedAiProvider.Kind != requestedKind)
            {
                return;
            }

            if (models.Count == 0)
            {
                SeedAiModelList();
                AiModelsStatus = "Сервер не вернул список моделей. Можно ввести ID вручную.";
                return;
            }

            var current = AiModel;
            AiAvailableModels.Clear();
            foreach (var model in models)
            {
                AiAvailableModels.Add(model);
            }
            if (!string.IsNullOrWhiteSpace(current) && !AiAvailableModels.Contains(current, StringComparer.OrdinalIgnoreCase))
            {
                AiAvailableModels.Insert(0, current.Trim());
            }
            if (string.IsNullOrWhiteSpace(AiModel) && AiAvailableModels.Count > 0)
            {
                AiModel = AiAvailableModels[0];
            }
            AiModelsStatus = $"Загружено моделей: {models.Count}.";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            if (SelectedAiProvider.Kind != requestedKind)
            {
                return;
            }
            SeedAiModelList();
            AiModelsStatus = exception is TranslationException te
                ? te.Message
                : "Не удалось загрузить модели. Можно ввести ID вручную.";
        }
        finally
        {
            if (ReferenceEquals(_aiModelsCts, cts))
            {
                _aiModelsCts = null;
                IsFetchingAiModels = false;
            }
            cts.Dispose();
        }
    }

    private async Task TestArgosTranslateConnectionAsync() => await TestProviderAsync(ProviderKind.ArgosTranslate, argosTranslate: true);
    private async Task TestAiProviderConnectionAsync() => await TestProviderAsync(SelectedAiProvider.Kind, argosTranslate: false);

    private async Task TestProviderAsync(ProviderKind kind, bool argosTranslate)
    {
        var info = ProviderCatalog.Get(kind);
        var profile = _draft.Provider.GetProfile(kind).Clone();
        var requiresApiKey = info.RequiresApiKey && kind != ProviderKind.OpenCode && kind != ProviderKind.OpenAiCompatible;

        if (requiresApiKey && string.IsNullOrWhiteSpace(profile.ApiKey))
        {
            SetTestStatus(argosTranslate, "Сначала укажите API-ключ.", false);
            return;
        }
        if (kind != ProviderKind.ArgosTranslate && string.IsNullOrWhiteSpace(profile.Model))
        {
            SetTestStatus(argosTranslate, "Сначала укажите модель.", false);
            return;
        }
        if (kind == ProviderKind.OpenAiCompatible && string.IsNullOrWhiteSpace(profile.BaseUrl))
        {
            SetTestStatus(argosTranslate, "Укажите Base URL локального/совместимого сервиса.", false);
            return;
        }

        if (argosTranslate) IsTestingArgosTranslate = true; else IsTestingAi = true;
        SetTestStatus(argosTranslate, "Проверяем подключение…", null);
        try
        {
            var provider = TranslationProviderFactory.Create(kind, profile, _httpClient);
            var target = _draft.TargetLanguage;
            if (!LanguageCatalog.IsSupported(target))
            {
                target = LanguageCatalog.All.FirstOrDefault()?.Code ?? LanguageCatalog.DefaultTargetCode;
            }
            var source = target.Equals("en", StringComparison.OrdinalIgnoreCase) ? "de" : "en";
            if (!LanguageCatalog.IsSupported(source))
            {
                source = LanguageCatalog.All.FirstOrDefault(c => !c.Code.Equals(target, StringComparison.OrdinalIgnoreCase))?.Code ?? "en";
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await provider.TranslateAsync(new TranslationRequest("Hello!", target, source), timeout.Token);
            if (argosTranslate || SelectedAiProvider.Kind == kind)
            {
                var preview = result.TranslatedText.Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (preview.Length > 100) preview = preview[..100] + "…";
                SetTestStatus(argosTranslate, $"Подключение работает. Ответ: {preview}", true);
            }
        }
        catch (TranslationException exception)
        {
            if (argosTranslate || SelectedAiProvider.Kind == kind)
            {
                SetTestStatus(argosTranslate, exception.Message, false);
            }
        }
        catch (OperationCanceledException)
        {
            if (argosTranslate || SelectedAiProvider.Kind == kind)
            {
                SetTestStatus(argosTranslate, "Превышено время ожидания ответа (30 секунд).", false);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Error("Ошибка проверки подключения.", exception);
            if (argosTranslate || SelectedAiProvider.Kind == kind)
            {
                SetTestStatus(argosTranslate, "Не удалось проверить подключение. Подробности записаны в журнал.", false);
            }
        }
        finally
        {
            if (argosTranslate) IsTestingArgosTranslate = false; else IsTestingAi = false;
        }
    }

    private async Task SaveAsync()
    {
        ValidateHotkeys();
        if (HasHotkeyErrors)
        {
            SetStatus("Исправьте горячие клавиши: " + HotkeyValidationMessage, false);
            SettingsSearchText = string.Empty;
            SelectedSection = SettingsSection.Hotkeys;
            return;
        }

        if (!LanguageCatalog.IsSupported(_draft.TargetLanguage))
        {
            SetStatus("Выберите корректный установленный язык перевода.", false);
            SettingsSearchText = string.Empty;
            SelectedSection = SettingsSection.Provider;
            return;
        }

        if (string.IsNullOrWhiteSpace(_draft.Appearance.FontFamily))
        {
            _draft.Appearance.FontFamily = "Segoe UI";
        }

        _draft.Appearance.Theme = ThemeCatalog.NormalizeId(_draft.Appearance.Theme);
        _draft.Provider.AiProvider = ProviderSettings.NormalizeAiProvider(_draft.Provider.AiProvider);
        _draft.History = _settings.Current.History;

        try
        {
            await _settings.ApplyAsync(_draft).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            AppLogger.Error("Не удалось сохранить настройки.", exception);
            SetStatus("Не удалось сохранить settings.json. Подробности записаны в журнал.", false);
            return;
        }

        if (!_autostartService.SetEnabled(_draft.General.AutoStart))
        {
            SetStatus("Настройки сохранены, но изменить автозапуск не удалось. Подробности в журнале.", false);
            return;
        }

        SetStatus("Настройки сохранены.", true);
        Saved?.Invoke(this, EventArgs.Empty);
    }

    private void ResetHotkeys()
    {
        var defaults = new HotkeySettings();
        TranslateHotkey = defaults.TranslateSelection;
        AiHotkey = defaults.ToggleAiMode;
        SettingsHotkey = defaults.OpenSettings;
        ValidateHotkeys();
    }

    private void ValidateHotkeys()
    {
        var errors = HotkeyService.Validate(_draft.Hotkeys);
        HotkeyValidationMessage = string.Join(Environment.NewLine, errors);
    }

    private void ClearHistory()
    {
        if (Confirm is not null
            && !Confirm("Очистить историю?", "Удалить все сохранённые переводы? Это действие нельзя отменить."))
        {
            return;
        }

        _historyService.Clear();
        RefreshHistory();
        SetStatus("История переводов очищена.", true);
    }

    private void RefreshHistory()
    {
        if (_isRefreshingHistory) return;
        _isRefreshingHistory = true;
        try
        {
            HistoryEntries.Clear();
            foreach (var entry in _historyService.GetEntries().Take(50))
            {
                HistoryEntries.Add(entry);
            }
            OnPropertiesChanged(nameof(HistoryCountText), nameof(HasHistory));
        }
        finally
        {
            _isRefreshingHistory = false;
        }
    }

    private void OpenDataFolder()
    {
        try { OpenPath(_settings.DirectoryPath); }
        catch (Exception exception) { AppLogger.Error("Не удалось открыть папку настроек.", exception); SetStatus("Не удалось открыть папку настроек.", false); }
    }

    private void OpenLog()
    {
        try
        {
            var logFile = Path.Combine(AppLogger.Directory, "app.log");
            OpenPath(File.Exists(logFile) ? logFile : AppLogger.Directory);
        }
        catch (Exception exception) { AppLogger.Error("Не удалось открыть журнал.", exception); SetStatus("Не удалось открыть журнал.", false); }
    }

    private static void OpenPath(string path)
    {
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private void SetStatus(string message, bool success)
    {
        StatusMessage = message;
        StatusIsSuccess = success;
    }

    private void SetTestStatus(bool argosTranslate, string message, bool? success)
    {
        var kind = success switch
        {
            true => TestStatusKind.Success,
            false => TestStatusKind.Error,
            _ => TestStatusKind.Pending
        };
        var resourceKey = success switch { true => "SuccessBrush", false => "ErrorBrush", _ => "TextMutedBrush" };
        var brush = Application.Current?.TryFindResource(resourceKey) as Brush ?? MediaBrushes.Gray;
        if (argosTranslate)
        {
            ArgosTestKind = kind;
            ArgosTranslateTestStatus = message;
            ArgosTranslateTestStatusBrush = brush;
        }
        else
        {
            AiTestKind = kind;
            AiTestStatus = message;
            AiTestStatusBrush = brush;
        }
    }

    private void ClearAiTestStatus()
    {
        AiTestKind = TestStatusKind.None;
        AiTestStatus = string.Empty;
        AiTestStatusBrush = Application.Current?.TryFindResource("TextMutedBrush") as Brush ?? MediaBrushes.Gray;
    }

    private static Brush ResolveStatusBrush(TestStatusKind kind)
    {
        var key = kind switch
        {
            TestStatusKind.Success => "SuccessBrush",
            TestStatusKind.Error => "ErrorBrush",
            TestStatusKind.Pending => "AccentBrush",
            _ => "TextMutedBrush"
        };
        return Application.Current?.TryFindResource(key) as Brush ?? MediaBrushes.Gray;
    }
}
