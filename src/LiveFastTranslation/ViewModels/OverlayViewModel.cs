using System.Windows.Input;
using LiveFastTranslation.Models;

namespace LiveFastTranslation.ViewModels;

/// <summary>A language entry shown in the overlay dropdown, respecting flag/code settings.</summary>
public sealed class LanguageChoice
{
    public LanguageChoice(LanguageOption language, string displayText)
    {
        Language = language;
        DisplayText = displayText;
    }

    public LanguageOption Language { get; }

    public string DisplayText { get; }

    public override string ToString() => DisplayText;
}

/// <summary>State and commands for the translucent translation capsule.</summary>
public sealed class OverlayViewModel : ObservableObject
{
    private readonly List<LanguageChoice> _languages = new();

    private LanguageChoice? _selectedLanguage;
    private string _translation = string.Empty;
    private string _errorMessage = string.Empty;
    private string _noticeMessage = string.Empty;
    private string _sourcePreview = string.Empty;
    private string _sourceBadge = "⇄";
    private string _directionText = "Перевод";
    private string _providerName = string.Empty;
    private bool _isLoading;
    private bool _hasError;
    private bool _hasNotice;
    private bool _isAiMode;
    private bool _minimalOverlay;
    private bool _suppressNotifications;
    private double _windowOpacity = 0.96;
    private double _backgroundOpacity = 1.0;
    private System.Windows.Media.FontFamily _overlayFontFamily = new("Segoe UI");
    private double _overlayFontSize = 13.5;

    public OverlayViewModel()
    {
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
        SettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke(this, EventArgs.Empty));
        RetryCommand = new RelayCommand(() => RetryRequested?.Invoke(this, EventArgs.Empty));
        ActivateResultCommand = new RelayCommand(
            () => ResultActivated?.Invoke(this, EventArgs.Empty),
            () => CanActivateResult);
        CopyCommand = new RelayCommand(() => CopyRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? CloseRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? RetryRequested;

    public event EventHandler? ResultActivated;

    public event EventHandler? CopyRequested;

    public event EventHandler<string>? TargetLanguageChanged;

    public event EventHandler<bool>? AiModeChanged;

    public ICommand CloseCommand { get; }

    public ICommand SettingsCommand { get; }

    public ICommand RetryCommand { get; }

    public ICommand ActivateResultCommand { get; }

    public ICommand CopyCommand { get; }

    public IReadOnlyList<LanguageChoice> Languages => _languages;

    public LanguageChoice? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (!SetProperty(ref _selectedLanguage, value) || value is null)
            {
                return;
            }

            if (!_suppressNotifications)
            {
                TargetLanguageChanged?.Invoke(this, value.Language.Code);
            }
        }
    }

    public string Translation
    {
        get => _translation;
        private set => SetProperty(ref _translation, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string NoticeMessage
    {
        get => _noticeMessage;
        private set => SetProperty(ref _noticeMessage, value);
    }

    public string SourcePreview
    {
        get => _sourcePreview;
        private set => SetProperty(ref _sourcePreview, value);
    }

    public string SourceBadge
    {
        get => _sourceBadge;
        private set => SetProperty(ref _sourceBadge, value);
    }

    public string DirectionText
    {
        get => _directionText;
        private set => SetProperty(ref _directionText, value);
    }

    public string ProviderName
    {
        get => _providerName;
        private set => SetProperty(ref _providerName, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                RaiseStateProperties();
            }
        }
    }

    public bool HasError
    {
        get => _hasError;
        private set
        {
            if (SetProperty(ref _hasError, value))
            {
                RaiseStateProperties();
            }
        }
    }

    public bool HasNotice
    {
        get => _hasNotice;
        private set
        {
            if (SetProperty(ref _hasNotice, value))
            {
                RaiseStateProperties();
            }
        }
    }

    public bool ShowLoading => IsLoading;

    public bool ShowError => !IsLoading && HasError;

    public bool ShowNotice => !IsLoading && !HasError && HasNotice;

    public bool ShowResult => !IsLoading && !HasError && !HasNotice && Translation.Length > 0;

    public bool CanActivateResult => ShowResult;

    public bool IsAiMode
    {
        get => _isAiMode;
        set
        {
            if (!SetProperty(ref _isAiMode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ActionHint));
            if (!_suppressNotifications)
            {
                AiModeChanged?.Invoke(this, value);
            }
        }
    }

    public string ActionHint => IsAiMode
        ? "Нажмите, чтобы скопировать перевод в буфер обмена"
        : "Нажмите, чтобы заменить выделенный текст переводом";

    /// <summary>When enabled the capsule shows only the translation text: no badge, language, AI or settings controls.</summary>
    public bool MinimalOverlay
    {
        get => _minimalOverlay;
        private set
        {
            if (SetProperty(ref _minimalOverlay, value))
            {
                OnPropertyChanged(nameof(ShowOverlayControls));
            }
        }
    }

    public bool ShowOverlayControls => !MinimalOverlay;

    public double WindowOpacity
    {
        get => _windowOpacity;
        private set => SetProperty(ref _windowOpacity, value);
    }

    /// <summary>Background-only opacity; the text is never faded with it.</summary>
    public double BackgroundOpacity
    {
        get => _backgroundOpacity;
        private set => SetProperty(ref _backgroundOpacity, value);
    }

    public System.Windows.Media.FontFamily OverlayFontFamily
    {
        get => _overlayFontFamily;
        private set => SetProperty(ref _overlayFontFamily, value);
    }

    public double OverlayFontSize
    {
        get => _overlayFontSize;
        private set
        {
            if (SetProperty(ref _overlayFontSize, value))
            {
                OnPropertyChanged(nameof(OverlayLineHeight));
            }
        }
    }

    /// <summary>Compact line spacing: wrapped text stays fully visible in fewer pixels.</summary>
    public double OverlayLineHeight => Math.Round(_overlayFontSize * 1.22, 1);

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _suppressNotifications = true;
        try
        {
            RebuildLanguages(
                settings.Appearance.ShowLanguageFlags,
                settings.Appearance.ShowLanguageCodes,
                settings.TargetLanguage);

            IsAiMode = settings.AiMode.Enabled;
            WindowOpacity = settings.Appearance.Opacity;
            BackgroundOpacity = settings.Appearance.BackgroundOpacity;
            OverlayFontSize = settings.Appearance.FontSize;
            MinimalOverlay = settings.Appearance.MinimalOverlay;
            try
            {
                OverlayFontFamily = new System.Windows.Media.FontFamily(settings.Appearance.FontFamily);
            }
            catch (ArgumentException)
            {
                OverlayFontFamily = new System.Windows.Media.FontFamily("Segoe UI");
            }
        }
        finally
        {
            _suppressNotifications = false;
        }
    }

    public void BeginLoading(string sourceText, string targetLanguageCode, bool aiMode)
    {
        Translation = string.Empty;
        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;
        SourcePreview = TruncateSingleLine(sourceText, 90);
        ProviderName = string.Empty;
        HasError = false;
        HasNotice = false;
        IsLoading = true;

        var target = LanguageCatalog.Find(targetLanguageCode);
        SourceBadge = aiMode ? "AI" : "⇄";
        DirectionText = target is null
            ? "Перевод"
            : $"Перевод на {target.NativeName}";
        SelectLanguageSilently(targetLanguageCode);

        RaiseStateProperties();
    }

    /// <summary>
    /// Updates the dropdown without raising <see cref="TargetLanguageChanged"/>: the automatic
    /// direction must not look like a manual choice and must not be persisted as the native language.
    /// </summary>
    public void SelectLanguageSilently(string targetLanguageCode)
    {
        if (string.IsNullOrWhiteSpace(targetLanguageCode))
        {
            return;
        }

        var choice = _languages.FirstOrDefault(
            item => item.Language.Code.Equals(targetLanguageCode, StringComparison.OrdinalIgnoreCase));
        if (choice is null || ReferenceEquals(choice, _selectedLanguage))
        {
            return;
        }

        var previous = _suppressNotifications;
        _suppressNotifications = true;
        try
        {
            SelectedLanguage = choice;
        }
        finally
        {
            _suppressNotifications = previous;
        }
    }

    public void SetSourcePreview(string sourceText)
    {
        SourcePreview = TruncateSingleLine(sourceText, 90);
    }

    public void SetResult(TranslationResult result)
    {
        Translation = result.TranslatedText;
        ProviderName = result.Provider;
        IsLoading = false;
        HasError = false;
        HasNotice = false;

        var source = LanguageCatalog.Find(result.DetectedSourceLanguage);
        var target = LanguageCatalog.Find(result.TargetLanguage);
        SourceBadge = source?.Flag ?? "⇄";
        DirectionText = BuildDirection(source, target, result.Provider);
        RaiseStateProperties();
    }

    public void SetError(string message)
    {
        ErrorMessage = string.IsNullOrWhiteSpace(message) ? "Не удалось выполнить перевод." : message.Trim();
        Translation = string.Empty;
        NoticeMessage = string.Empty;
        IsLoading = false;
        HasError = true;
        HasNotice = false;
        RaiseStateProperties();
    }

    public void SetNotice(string message)
    {
        NoticeMessage = message;
        ErrorMessage = string.Empty;
        IsLoading = false;
        HasError = false;
        HasNotice = true;
        RaiseStateProperties();
    }

    public void ClearState()
    {
        Translation = string.Empty;
        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;
        SourcePreview = string.Empty;
        IsLoading = false;
        HasError = false;
        HasNotice = false;
        RaiseStateProperties();
    }

    private void RebuildLanguages(bool showFlags, bool showCodes, string targetCode)
    {
        _languages.Clear();
        foreach (var language in LanguageCatalog.All)
        {
            _languages.Add(new LanguageChoice(language, language.Compact(showFlags, showCodes)));
        }

        OnPropertyChanged(nameof(Languages));

        var selected = _languages.FirstOrDefault(
                           choice => choice.Language.Code.Equals(targetCode, StringComparison.OrdinalIgnoreCase))
                       ?? _languages.FirstOrDefault();

        _selectedLanguage = selected;
        OnPropertyChanged(nameof(SelectedLanguage));
    }

    private static string BuildDirection(LanguageOption? source, LanguageOption? target, string provider)
    {
        var sourceText = source is null
            ? "Авто"
            : $"{source.Flag} {source.Code.ToUpperInvariant()}";
        var targetText = target is null
            ? "—"
            : $"{target.Flag} {target.Code.ToUpperInvariant()}";
        return string.IsNullOrWhiteSpace(provider)
            ? $"{sourceText} → {targetText}"
            : $"{sourceText} → {targetText} · {provider}";
    }

    private void RaiseStateProperties()
    {
        OnPropertiesChanged(
            nameof(ShowLoading),
            nameof(ShowError),
            nameof(ShowNotice),
            nameof(ShowResult),
            nameof(CanActivateResult));
    }

    private static string TruncateSingleLine(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var singleLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= maxLength ? singleLine : singleLine[..maxLength] + "…";
    }
}
