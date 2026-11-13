using System.Windows;
using System.Windows.Threading;
using LiveFastTranslation.Models;
using LiveFastTranslation.Services.History;
using LiveFastTranslation.Services.Infrastructure;
using LiveFastTranslation.Services.Input;
using LiveFastTranslation.Services.Overlay;
using LiveFastTranslation.Services.Settings;
using LiveFastTranslation.Services.TextCapture;
using LiveFastTranslation.Services.TextReplace;
using LiveFastTranslation.Services.Theme;
using LiveFastTranslation.Services.Tray;
using LiveFastTranslation.Services.Translation;
using LiveFastTranslation.ViewModels;
using LiveFastTranslation.Views;
using Forms = System.Windows.Forms;

namespace LiveFastTranslation;

/// <summary>
/// Application host: composition root, global hotkeys, AI typing monitor, tray icon and settings window.
/// The application has no main window and lives in the notification area until the user exits.
/// </summary>
public partial class App : Application
{
    private SingleInstanceService? _singleInstance;
    private SettingsService? _settings;
    private HistoryService? _history;
    private AutostartService? _autostart;
    private ThemeService? _theme;
    private HttpClient? _httpClient;
    private OverlayController? _overlay;
    private HotkeyService? _hotkeys;
    private TypingMonitor? _typingMonitor;
    private TrayService? _tray;
    private SettingsWindow? _settingsWindow;

    private string _lastHotkeySignature = string.Empty;
    private bool _hotkeyApplyFailed;
    private string? _lastTypingError;
    private bool _firstRun;
    private bool _shuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SetupExceptionHandling();

        try
        {
            _singleInstance = new SingleInstanceService();
            if (!_singleInstance.IsFirstInstance)
            {
                if (!_singleInstance.NotifyExistingInstance())
                {
                    MessageBox.Show(
                        "Live-Fast Translation уже запущен. Найдите значок приложения в области уведомлений.",
                        "Live-Fast Translation",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                _singleInstance.Dispose();
                _singleInstance = null;
                Shutdown();
                return;
            }

            _singleInstance.ActivationRequested += OnSecondInstanceActivated;
            InitializeServices();
            ShowStartupState();
        }
        catch (Exception exception)
        {
            AppLogger.Error("Не удалось запустить приложение.", exception);
            MessageBox.Show(
                "Не удалось запустить Live-Fast Translation. Подробности записаны в журнал приложения.",
                "Live-Fast Translation",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }

        AppLogger.Info("Live-Fast Translation запущен.");
    }

    private void InitializeServices()
    {
        var settingsService = new SettingsService();
        var isFirstRun = !settingsService.SettingsFileExists;

        var argosPackages = new ArgosPackageService();
        argosPackages.Initialize();

        settingsService.Load();
        _settings = settingsService;

        AppLogger.Initialize(Path.Combine(settingsService.DirectoryPath, "logs"));
        if (isFirstRun)
        {
            AppLogger.Info("Первый запуск: файл настроек будет создан после сохранения.");
        }

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "LiveFastTranslation/1.0");

        _history = new HistoryService(settingsService);
        _autostart = new AutostartService();
        _theme = new ThemeService();
        _theme.Apply(settingsService.Current.Appearance.Theme);
        FontService.Apply(settingsService.Current.Appearance.FontFamily);

        _overlay = new OverlayController(
            settingsService,
            new TextCaptureService(),
            new TranslationService(settingsService, _httpClient),
            new TextReplacementService(),
            _history);
        _overlay.SettingsRequested += (_, _) => OpenSettings();

        _hotkeys = new HotkeyService();
        _hotkeys.HotkeyPressed += OnHotkeyPressed;
        ApplyHotkeys(settingsService.Current.Hotkeys, notifyErrors: false);

        _typingMonitor = new TypingMonitor
        {
            ShouldPauseInput = () => _overlay?.IsOverlayVisible == true
        };
        _typingMonitor.SentenceDetected += OnSentenceDetected;
        _typingMonitor.ApplySettings(settingsService.Current.AiMode);
        _lastTypingError = settingsService.Current.AiMode.Enabled ? _typingMonitor.LastError : null;

        _tray = new TrayService
        {
            NotificationsEnabled = settingsService.Current.General.ShowTrayNotifications
        };
        _tray.TranslateSelectionRequested += (_, _) => TranslateSelectionFromUi();
        _tray.SettingsRequested += (_, _) => OpenSettings();
        _tray.ToggleAiModeRequested += (_, _) => ToggleAiMode();
        _tray.ExitRequested += (_, _) => Shutdown();
        _tray.UpdateAiMode(settingsService.Current.AiMode.Enabled);
        _tray.UpdateHotkeyHint(settingsService.Current.Hotkeys.TranslateSelection);
        _tray.Show();

        SyncAutostart(settingsService.Current);

        settingsService.Changed += OnSettingsChanged;
        _firstRun = isFirstRun;
    }

    private void ShowStartupState()
    {
        if (_settings is null || _tray is null)
        {
            return;
        }

        var settings = _settings.Current;
        if (_firstRun)
        {
            OpenSettings();
            return;
        }

        if (!string.IsNullOrWhiteSpace(settings.Hotkeys.TranslateSelection))
        {
            _tray.Notify(
                "Live-Fast Translation",
                $"Приложение работает в трее. {settings.Hotkeys.TranslateSelection} — перевести выделенное.",
                Forms.ToolTipIcon.Info);
        }

        if (_lastTypingError is not null)
        {
            _tray.Notify("AI-режим", _lastTypingError, Forms.ToolTipIcon.Warning);
        }
    }

    private void SyncAutostart(AppSettings settings)
    {
        if (_autostart is null)
        {
            return;
        }

        // Automated UI checks must never touch the real HKCU Run key.
        if (string.Equals(Environment.GetEnvironmentVariable("LFT_DISABLE_AUTOSTART"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var registered = _autostart.IsEnabled();
        if (settings.General.AutoStart == registered)
        {
            return;
        }

        if (!_autostart.SetEnabled(settings.General.AutoStart))
        {
            AppLogger.Warn("Не удалось синхронизировать запись автозапуска при старте.");
        }
    }

    private void OnSecondInstanceActivated(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(OpenSettings), DispatcherPriority.Normal);
    }

    private void OnHotkeyPressed(object? sender, int id)
    {
        switch (id)
        {
            case HotkeyService.TranslateSelectionId:
                TranslateSelectionFromUi();
                break;
            case HotkeyService.ToggleAiModeId:
                ToggleAiMode();
                break;
            case HotkeyService.OpenSettingsId:
                OpenSettings();
                break;
        }
    }

    private async void TranslateSelectionFromUi()
    {
        if (_shuttingDown || _overlay is null)
        {
            return;
        }

        try
        {
            await _overlay.TranslateSelectionAsync();
        }
        catch (Exception exception)
        {
            AppLogger.Error("Ошибка запуска перевода выделенного текста.", exception);
        }
    }

    private async void OnSentenceDetected(object? sender, SentenceDetectedEventArgs e)
    {
        if (_shuttingDown || _overlay is null)
        {
            return;
        }

        try
        {
            await _overlay.TranslateAiSentenceAsync(e.Sentence, e.TargetWindow);
        }
        catch (Exception exception)
        {
            AppLogger.Error("Ошибка AI-перевода предложения.", exception);
        }
    }

    private void ToggleAiMode()
    {
        if (_shuttingDown || _settings is null)
        {
            return;
        }

        var enabled = !_settings.Current.AiMode.Enabled;
        _settings.Update(settings => settings.AiMode.Enabled = enabled);

        _tray?.Notify(
            "AI-режим",
            enabled
                ? "Включён. Завершайте фразы точкой, восклицательным или вопросительным знаком."
                : "Выключен.",
            Forms.ToolTipIcon.Info);
        AppLogger.Info($"AI-режим переключён: {(enabled ? "включён" : "выключен")}.");
    }

    private void OpenSettings()
    {
        if (_shuttingDown || _settings is null || _history is null || _autostart is null || _httpClient is null)
        {
            return;
        }

        _overlay?.HideOverlay();

        if (_settingsWindow is { IsLoaded: true } existing)
        {
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }

            existing.Show();
            existing.Activate();
            existing.Topmost = true;
            existing.Topmost = false;
            existing.Focus();
            return;
        }

        var viewModel = new SettingsViewModel(_settings, _history, _autostart, _httpClient);
        var window = new SettingsWindow(viewModel);
        _settingsWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_settingsWindow, window))
            {
                _settingsWindow = null;
            }
        };

        window.Show();
        window.Activate();
        window.Focus();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnSettingsChanged(sender, e)), DispatcherPriority.Normal);
            return;
        }

        if (_shuttingDown || _settings is null)
        {
            return;
        }

        var settings = _settings.Current;
        _theme?.Apply(settings.Appearance.Theme);
        FontService.Apply(settings.Appearance.FontFamily);

        var signature = GetHotkeySignature(settings.Hotkeys);
        var hotkeysChanged = !string.Equals(signature, _lastHotkeySignature, StringComparison.Ordinal);
        if (hotkeysChanged || _hotkeyApplyFailed)
        {
            ApplyHotkeys(settings.Hotkeys, notifyErrors: hotkeysChanged);
        }

        _typingMonitor?.ApplySettings(settings.AiMode);
        var typingError = settings.AiMode.Enabled ? _typingMonitor?.LastError : null;
        if (!string.Equals(typingError, _lastTypingError, StringComparison.Ordinal))
        {
            _lastTypingError = typingError;
            if (typingError is not null)
            {
                _tray?.Notify("AI-режим", typingError, Forms.ToolTipIcon.Warning);
            }
        }

        if (_tray is not null)
        {
            _tray.NotificationsEnabled = settings.General.ShowTrayNotifications;
            _tray.UpdateAiMode(settings.AiMode.Enabled);
            _tray.UpdateHotkeyHint(settings.Hotkeys.TranslateSelection);
        }

        SyncAutostart(settings);
        _overlay?.ApplySettings();
    }

    private void ApplyHotkeys(HotkeySettings hotkeys, bool notifyErrors)
    {
        if (_hotkeys is null)
        {
            return;
        }

        _lastHotkeySignature = GetHotkeySignature(hotkeys);
        var errors = _hotkeys.Apply(hotkeys);
        _hotkeyApplyFailed = errors.Count > 0;

        foreach (var error in errors)
        {
            AppLogger.Warn($"Горячая клавиша: {error}");
        }

        if (_hotkeyApplyFailed && notifyErrors)
        {
            _tray?.Notify(
                "Горячие клавиши",
                string.Join(Environment.NewLine, errors),
                Forms.ToolTipIcon.Warning);
        }
    }

    private static string GetHotkeySignature(HotkeySettings hotkeys)
    {
        return $"{hotkeys.TranslateSelection}|{hotkeys.ToggleAiMode}|{hotkeys.OpenSettings}";
    }

    private void SetupExceptionHandling()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            AppLogger.Error("Необработанная ошибка интерфейса.", args.Exception);
            args.Handled = true;

            if (_shuttingDown)
            {
                return;
            }

            MessageBox.Show(
                "Произошла непредвиденная ошибка. Приложение продолжит работу, подробности записаны в журнал.",
                "Live-Fast Translation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                AppLogger.Error("Необработанная ошибка приложения.", exception);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLogger.Error("Необработанная ошибка фоновой задачи.", args.Exception);
            args.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shuttingDown = true;
        AppLogger.Info("Завершение работы Live-Fast Translation.");

        if (_settings is not null)
        {
            _settings.Changed -= OnSettingsChanged;
        }

        if (_singleInstance is not null)
        {
            _singleInstance.ActivationRequested -= OnSecondInstanceActivated;
        }

        try
        {
            _settingsWindow?.Close();
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"Не удалось закрыть окно настроек: {exception.Message}");
        }

        _settingsWindow = null;

        _typingMonitor?.Dispose();
        _hotkeys?.Dispose();
        _overlay?.Dispose();
        _tray?.Dispose();
        _theme?.Dispose();

        try
        {
            _settings?.Save();
        }
        catch (Exception exception)
        {
            AppLogger.Error("Не удалось сохранить настройки при выходе.", exception);
        }

        _httpClient?.Dispose();
        _singleInstance?.Dispose();

        base.OnExit(e);
    }
}
