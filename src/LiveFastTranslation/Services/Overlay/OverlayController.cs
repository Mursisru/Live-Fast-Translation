using LiveFastTranslation.Models;
using LiveFastTranslation.Interop;
using LiveFastTranslation.Services.History;
using LiveFastTranslation.Services.Infrastructure;
using LiveFastTranslation.Services.Settings;
using LiveFastTranslation.Services.TextCapture;
using LiveFastTranslation.Services.TextReplace;
using LiveFastTranslation.Services.Translation;
using LiveFastTranslation.ViewModels;
using LiveFastTranslation.Views;

namespace LiveFastTranslation.Services.Overlay;

/// <summary>
/// Owns the overlay window and coordinates capture, translation, replacement and history.
/// All members are expected to be used from the WPF UI thread.
/// </summary>
public sealed class OverlayController : IDisposable
{
    private readonly SettingsService _settings;
    private readonly ITextCaptureService _captureService;
    private readonly TranslationService _translationService;
    private readonly TextReplacementService _replacementService;
    private readonly HistoryService _historyService;
    private readonly OverlayWindow _window;

    private CancellationTokenSource? _requestCts;
    private int _requestId;
    private TextCaptureResult? _capture;
    private string? _sourceText;
    private bool _isAiSource;
    private bool _autoCaptureActive;
    private bool _disposed;

    public OverlayController(
        SettingsService settings,
        ITextCaptureService captureService,
        TranslationService translationService,
        TextReplacementService replacementService,
        HistoryService historyService)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _replacementService = replacementService ?? throw new ArgumentNullException(nameof(replacementService));
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));

        ViewModel = new OverlayViewModel();
        ViewModel.ApplySettings(settings.Current);
        _window = new OverlayWindow(ViewModel);
        _window.AnimationsEnabled = settings.Current.Appearance.EnableAnimations;

        ViewModel.CloseRequested += (_, _) =>
        {
            _requestCts?.Cancel();
            _window.HideOverlay();
        };
        ViewModel.SettingsRequested += (_, _) => OpenSettingsFromOverlay();
        ViewModel.ResultActivated += (_, _) => _ = HandleResultActivatedAsync();
        ViewModel.RetryRequested += (_, _) =>
        {
            if (_sourceText is null)
            {
                _ = TranslateSelectionAsync();
            }
            else
            {
                _ = RetranslateAsync(TranslationTargetResolver.Resolve(_sourceText, settings.Current.TargetLanguage));
            }
        };
        ViewModel.TargetLanguageChanged += (_, code) => OnTargetLanguageChanged(code);
        ViewModel.AiModeChanged += (_, enabled) => OnAiModeChanged(enabled);
        _window.HiddenByUser += (_, _) => _requestCts?.Cancel();
        settings.Changed += OnSettingsChanged;
    }

    public OverlayViewModel ViewModel { get; }

    public OverlayWindow Window => _window;

    public event EventHandler? SettingsRequested;

    public bool IsOverlayVisible => _window.IsOverlayVisible;

    public void ApplySettings()
    {
        ViewModel.ApplySettings(_settings.Current);
        _window.AnimationsEnabled = _settings.Current.Appearance.EnableAnimations;
        if (_window.IsOverlayVisible)
        {
            _window.RefitToContent();
        }
    }

    public async Task TranslateSelectionAsync()
    {
        if (_disposed)
        {
            return;
        }

        var cts = BeginRequest();
        var requestId = _requestId;
        var nativeLanguage = _settings.Current.TargetLanguage;

        _capture = null;
        _sourceText = null;
        _isAiSource = false;

        ViewModel.BeginLoading(string.Empty, nativeLanguage, aiMode: false);
        _window.FitToText("Перевод…");
        _window.ShowOverlay();

        try
        {
            var capture = await _captureService
                .CaptureSelectedTextAsync(_settings.Current.General.AllowClipboardCapture, cts.Token)
                .ConfigureAwait(true);

            if (IsStale(requestId, cts))
            {
                return;
            }

            if (!capture.HasText)
            {
                _window.SetAnchor(capture.AnchorBounds);
                ShowError(capture.Error ?? "Не удалось прочитать выделенный текст. Выделите текст в другом приложении и повторите.");
                return;
            }

            await RunSelectionTranslationAsync(capture, requestId, cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (TranslationException exception)
        {
            if (!IsStale(requestId, cts))
            {
                ShowError(exception.Message);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Error("Неожиданная ошибка при переводе выделенного текста.", exception);
            if (!IsStale(requestId, cts))
            {
                ShowError("Неожиданная ошибка. Подробности записаны в журнал.");
            }
        }
        finally
        {
            CompleteRequest(cts);
        }
    }

    /// <summary>
    /// Silently reads the current selection and translates it when a non-empty selection appears.
    /// Used by the optional auto-show mode; an empty selection (a plain click, erased text or a
    /// collapsed caret) hides a visible capsule. The clipboard is never used.
    /// </summary>
    public async Task AutoTranslateSelectionAsync()
    {
        if (_disposed || _autoCaptureActive)
        {
            return;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground != IntPtr.Zero)
        {
            NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
            if (processId == (uint)Environment.ProcessId)
            {
                return;
            }
        }

        _autoCaptureActive = true;
        try
        {
            var capture = await _captureService
                .CaptureSelectedTextAsync(allowClipboardFallback: false)
                .ConfigureAwait(true);

            if (_disposed)
            {
                return;
            }

            // With auto-show enabled the capsule follows the selection: an empty selection
            // (a plain click, erased text or a collapsed caret) must hide it instead of
            // leaving a stale translation on screen.
            if (!capture.HasText)
            {
                if (_window.IsOverlayVisible)
                {
                    HideOverlay();
                }

                return;
            }

            // The same selection in the same field must not re-open or restart the capsule.
            if (_window.IsOverlayVisible
                && string.Equals(capture.Text, _sourceText, StringComparison.Ordinal)
                && _capture?.TargetWindow == capture.TargetWindow)
            {
                return;
            }

            var cts = BeginRequest();
            var requestId = _requestId;
            try
            {
                await RunSelectionTranslationAsync(capture, requestId, cts.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
            catch (TranslationException exception)
            {
                if (!IsStale(requestId, cts))
                {
                    ShowError(exception.Message);
                }
            }
            catch (Exception exception)
            {
                AppLogger.Error("Неожиданная ошибка авто-перевода выделения.", exception);
                if (!IsStale(requestId, cts))
                {
                    ShowError("Неожиданная ошибка. Подробности записаны в журнал.");
                }
            }
            finally
            {
                CompleteRequest(cts);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Auto mode is best-effort: an application without UI Automation text support
            // must not produce error balloons on every click.
            AppLogger.Warn($"Авто-перевод: не удалось прочитать выделение: {exception.Message}");
        }
        finally
        {
            _autoCaptureActive = false;
        }
    }

    private async Task RunSelectionTranslationAsync(
        TextCaptureResult capture,
        int requestId,
        CancellationToken cancellationToken)
    {
        var target = TranslationTargetResolver.Resolve(capture.Text, _settings.Current.TargetLanguage);

        _capture = capture;
        _sourceText = capture.Text;
        _isAiSource = false;

        ViewModel.BeginLoading(capture.Text, target, aiMode: false);
        _window.SetAnchor(capture.AnchorBounds);
        _window.FitToText("Перевод…");
        if (!_window.IsOverlayVisible)
        {
            _window.ShowOverlay();
        }

        await TranslateCoreAsync(requestId, capture.Text, target, cancellationToken, validateSelection: true, expectedWindow: capture.TargetWindow)
            .ConfigureAwait(true);
    }

    public async Task TranslateAiSentenceAsync(string sentence, IntPtr targetWindow)
    {
        if (_disposed || string.IsNullOrWhiteSpace(sentence))
        {
            return;
        }

        var cts = BeginRequest();
        var requestId = _requestId;
        var settings = _settings.Current;
        var target = TranslationTargetResolver.Resolve(sentence, settings.TargetLanguage);

        _capture = null;
        _sourceText = sentence.Trim();
        _isAiSource = true;

        ViewModel.BeginLoading(_sourceText, target, aiMode: true);
        _window.SetAnchor(TextAnchorLocator.TryGetAnchor(targetWindow));
        _window.FitToText(_sourceText);
        _window.ShowOverlay();

        try
        {
            await TranslateCoreAsync(requestId, _sourceText, target, cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (TranslationException exception)
        {
            if (!IsStale(requestId, cts))
            {
                ShowError(exception.Message);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Error("Неожиданная ошибка AI-режима.", exception);
            if (!IsStale(requestId, cts))
            {
                ShowError("Неожиданная ошибка. Подробности записаны в журнал.");
            }
        }
        finally
        {
            CompleteRequest(cts);
        }
    }

    public async Task RetranslateAsync(string targetLanguageCode)
    {
        if (_disposed || string.IsNullOrWhiteSpace(_sourceText))
        {
            return;
        }

        var cts = BeginRequest();
        var requestId = _requestId;

        ViewModel.BeginLoading(_sourceText, targetLanguageCode, _isAiSource);
        _window.FitToText(_sourceText);

        try
        {
            await TranslateCoreAsync(requestId, _sourceText, targetLanguageCode, cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (TranslationException exception)
        {
            if (!IsStale(requestId, cts))
            {
                ShowError(exception.Message);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Error("Неожиданная ошибка повторного перевода.", exception);
            if (!IsStale(requestId, cts))
            {
                ShowError("Неожиданная ошибка. Подробности записаны в журнал.");
            }
        }
        finally
        {
            CompleteRequest(cts);
        }
    }

    public void HideOverlay()
    {
        _requestCts?.Cancel();
        _window.HideOverlay();
    }

    private async Task TranslateCoreAsync(
        int requestId,
        string text,
        string targetLanguage,
        CancellationToken cancellationToken,
        bool validateSelection = false,
        IntPtr expectedWindow = default)
    {
        var result = await _translationService
            .TranslateAsync(new TranslationRequest(text, targetLanguage), cancellationToken)
            .ConfigureAwait(true);

        if (IsStale(requestId, null) || cancellationToken.IsCancellationRequested || !_window.IsOverlayVisible)
        {
            return;
        }

        // The user may have collapsed or replaced the selection while the provider was
        // working. Re-read it before showing anything so a detached translation is never
        // painted over an empty or different selection.
        if (validateSelection)
        {
            var selectionIsValid = await IsSelectionStillValidAsync(text, expectedWindow, cancellationToken)
                .ConfigureAwait(true);
            if (IsStale(requestId, null) || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!selectionIsValid)
            {
                _window.HideOverlay();
                return;
            }
        }

        ViewModel.SetResult(result);
        ViewModel.SelectLanguageSilently(result.TargetLanguage);
        _window.FitToText(result.TranslatedText);

        _historyService.Add(new TranslationHistoryEntry
        {
            SourceText = text,
            TranslatedText = result.TranslatedText,
            SourceLanguage = result.DetectedSourceLanguage,
            TargetLanguage = result.TargetLanguage,
            Provider = result.Provider
        });
    }

    private async Task<bool> IsSelectionStillValidAsync(string text, IntPtr expectedWindow, CancellationToken cancellationToken)
    {
        try
        {
            var current = await _captureService
                .CaptureSelectedTextAsync(_settings.Current.General.AllowClipboardCapture, cancellationToken)
                .ConfigureAwait(true);
            if (!current.HasText)
            {
                return false;
            }

            if (!string.Equals(current.Text, text, StringComparison.Ordinal)
                && !string.Equals(current.Text.Trim(), text.Trim(), StringComparison.Ordinal))
            {
                return false;
            }

            return expectedWindow == IntPtr.Zero
                   || current.TargetWindow == IntPtr.Zero
                   || current.TargetWindow == expectedWindow;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"Не удалось повторно проверить выделение: {exception.Message}");
            return false;
        }
    }

    private async Task HandleResultActivatedAsync()
    {
        if (!ViewModel.CanActivateResult || string.IsNullOrWhiteSpace(ViewModel.Translation) || _sourceText is null)
        {
            return;
        }

        var settings = _settings.Current;
        var translation = ViewModel.Translation;

        if (_isAiSource || _capture is null)
        {
            if (!ClipboardSnapshot.TrySetText(translation))
            {
                ViewModel.SetError("Не удалось скопировать перевод в буфер обмена.");
                return;
            }

            ViewModel.SetNotice("Скопировано в буфер обмена");
            await Task.Delay(850).ConfigureAwait(true);
            if (ViewModel.ShowNotice)
            {
                _window.HideOverlay();
            }

            return;
        }

        try
        {
            var context = new ReplaceContext(
                _capture.Text,
                _capture.Element,
                _capture.TargetWindow,
                _capture.ProcessId);

            var outcome = await _replacementService
                .ReplaceAsync(
                    settings.Replacement.Strategy,
                    context,
                    translation,
                    ReplaceOptions.FromSettings(settings.Replacement))
                .ConfigureAwait(true);

            if (outcome.Success)
            {
                ViewModel.SetNotice("Заменено");
                await Task.Delay(550).ConfigureAwait(true);
                _window.HideOverlay();
                return;
            }

            ViewModel.SetError($"Не заменено. {outcome.Error}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLogger.Error("Ошибка замены текста.", exception);
            ViewModel.SetError("Не заменено. Подробности записаны в журнал.");
        }
    }

    private void OnTargetLanguageChanged(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || !LanguageCatalog.IsSupported(code))
        {
            return;
        }

        // The dropdown only retranslates the current text; it must not overwrite the native
        // language from settings. The automatic direction picks it up on the next translation.
        if (_sourceText is not null)
        {
            _ = RetranslateAsync(code);
        }
    }

    private void OnAiModeChanged(bool enabled)
    {
        _settings.Update(settings => settings.AiMode.Enabled = enabled);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplySettings();
        }
        else
        {
            dispatcher.BeginInvoke(ApplySettings);
        }
    }

    private void OpenSettingsFromOverlay()
    {
        _requestCts?.Cancel();
        _window.HideOverlay();
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ShowError(string message)
    {
        ViewModel.SetError(message);
        _window.FitToText(message);
    }

    private CancellationTokenSource BeginRequest()
    {
        var previous = _requestCts;
        var cts = new CancellationTokenSource();
        _requestCts = cts;
        _requestId++;
        previous?.Cancel();
        return cts;
    }

    private bool IsStale(int requestId, CancellationTokenSource? cts)
    {
        return _disposed
               || requestId != _requestId
               || (cts?.IsCancellationRequested ?? false);
    }

    private void CompleteRequest(CancellationTokenSource cts)
    {
        if (ReferenceEquals(_requestCts, cts))
        {
            _requestCts = null;
        }

        cts.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settings.Changed -= OnSettingsChanged;
        _requestCts?.Cancel();
        _requestCts?.Dispose();
        _requestCts = null;
        _window.Close();
    }
}
