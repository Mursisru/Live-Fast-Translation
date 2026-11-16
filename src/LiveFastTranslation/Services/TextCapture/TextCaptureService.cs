using System.Windows;
using System.Windows.Automation;
using LiveFastTranslation.Interop;
using LiveFastTranslation.Models;
using LiveFastTranslation.Services.Infrastructure;
using LiveFastTranslation.Services.TextReplace;

namespace LiveFastTranslation.Services.TextCapture;

/// <summary>
/// Reads the current selection via UI Automation TextPattern, then falls back to native edit controls
/// and finally to a clipboard round-trip that restores the previous clipboard contents.
/// </summary>
public sealed class TextCaptureService : ITextCaptureService
{
    private const int MaxTextLength = 20_000;

    public async Task<TextCaptureResult> CaptureSelectedTextAsync(
        bool allowClipboardFallback,
        CancellationToken cancellationToken = default)
    {
        var targetWindow = NativeMethods.GetForegroundWindow();
        var processId = 0;
        if (targetWindow != IntPtr.Zero)
        {
            NativeMethods.GetWindowThreadProcessId(targetWindow, out var nativeProcessId);
            processId = (int)nativeProcessId;
        }

        if (processId == Environment.ProcessId)
        {
            return new TextCaptureResult
            {
                TargetWindow = targetWindow,
                ProcessId = processId,
                Error = "Активное окно принадлежит Live-Fast Translation. Перейдите в приложение с текстом."
            };
        }

        var focusedControl = Win32Focus.GetFocusedControlHwnd();
        if (focusedControl != IntPtr.Zero && Win32Focus.IsPasswordField(focusedControl))
        {
            return new TextCaptureResult
            {
                TargetWindow = targetWindow,
                ProcessId = processId,
                IsSensitive = true,
                Error = "Поле ввода защищено. Чтение и перевод паролей отключены."
            };
        }

        cancellationToken.ThrowIfCancellationRequested();

        var (uiaText, element, sensitive, uiaError, uiaAnchor) = TryReadWithUiAutomation();
        var fallbackAnchor = uiaAnchor ?? TextAnchorLocator.TryGetAnchor(targetWindow);
        if (sensitive)
        {
            return new TextCaptureResult
            {
                TargetWindow = targetWindow,
                ProcessId = processId,
                Element = element,
                AnchorBounds = fallbackAnchor,
                IsSensitive = true,
                Error = "Поле ввода защищено. Чтение и перевод паролей отключены."
            };
        }

        if (!string.IsNullOrWhiteSpace(uiaText))
        {
            return CreateResult(uiaText, element, targetWindow, processId, CaptureMethod.UiAutomationTextPattern, fallbackAnchor);
        }

        if (Win32Focus.TryReadSelectedText(out var nativeText, out _))
        {
            return CreateResult(nativeText, element, targetWindow, processId, CaptureMethod.Win32EditControl, fallbackAnchor);
        }

        if (!allowClipboardFallback)
        {
            return new TextCaptureResult
            {
                TargetWindow = targetWindow,
                ProcessId = processId,
                Element = element,
                AnchorBounds = fallbackAnchor,
                Error = uiaError ?? "Не удалось прочитать выделенный текст. Включите запасной способ через буфер обмена в настройках."
            };
        }

        var clipboardText = await TryReadWithClipboardAsync(cancellationToken).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(clipboardText))
        {
            return CreateResult(clipboardText, element, targetWindow, processId, CaptureMethod.Clipboard, fallbackAnchor);
        }

        return new TextCaptureResult
        {
            TargetWindow = targetWindow,
            ProcessId = processId,
            Element = element,
            AnchorBounds = fallbackAnchor,
            Error = "Выделенный текст не найден. Выделите текст в другом приложении и повторите."
        };
    }

    private static TextCaptureResult CreateResult(
        string text,
        AutomationElement? element,
        IntPtr targetWindow,
        int processId,
        CaptureMethod method,
        Rect? anchorBounds)
    {
        var trimmed = text.Trim();
        if (trimmed.Length > MaxTextLength)
        {
            return new TextCaptureResult
            {
                TargetWindow = targetWindow,
                ProcessId = processId,
                Element = element,
                AnchorBounds = anchorBounds,
                Method = method,
                Error = $"Выделено слишком много текста (максимум {MaxTextLength:N0} символов)."
            };
        }

        return new TextCaptureResult
        {
            Text = trimmed,
            Element = element,
            TargetWindow = targetWindow,
            ProcessId = processId,
            AnchorBounds = anchorBounds,
            Method = method
        };
    }

    private static (string? Text, AutomationElement? Element, bool Sensitive, string? Error, Rect? Anchor) TryReadWithUiAutomation()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null)
            {
                return (null, null, false, "UI Automation не вернул активный элемент.", null);
            }

            var anchor = TextAnchorLocator.TryGetElementBounds(focused);
            var current = focused;
            for (var level = 0; level < 5 && current is not null; level++)
            {
                try
                {
                    if (current.Current.IsPassword)
                    {
                        return (null, focused, true, null, anchor);
                    }
                }
                catch (ElementNotAvailableException)
                {
                    break;
                }

                var text = TryReadTextPattern(current);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return (text, focused, false, null, anchor);
                }

                try
                {
                    current = TreeWalker.ControlViewWalker.GetParent(current);
                }
                catch (ElementNotAvailableException)
                {
                    break;
                }
            }

            return (null, focused, false, "UI Automation не видит выделение в этом элементе.", anchor);
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException)
        {
            AppLogger.Warn($"UI Automation capture failed: {exception.Message}");
            return (null, null, false, "UI Automation недоступен для активного приложения.", null);
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"UI Automation capture failed: {exception.Message}");
            return (null, null, false, "UI Automation недоступен для активного приложения.", null);
        }
    }

    private static string? TryReadTextPattern(AutomationElement element)
    {
        try
        {
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
            {
                return null;
            }

            var textPattern = (TextPattern)pattern;
            var ranges = textPattern.GetSelection();
            if (ranges is null || ranges.Length == 0)
            {
                return null;
            }

            var builder = new System.Text.StringBuilder();
            foreach (var range in ranges)
            {
                try
                {
                    var text = range.GetText(-1);
                    if (!string.IsNullOrEmpty(text))
                    {
                        builder.Append(text);
                    }
                }
                catch (ElementNotAvailableException)
                {
                    // The selection can disappear between calls when the user releases the mouse.
                }
            }

            return builder.ToString();
        }
        catch (Exception exception) when (
            exception is ElementNotAvailableException
                or InvalidOperationException
                or NotSupportedException
                or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task<string?> TryReadWithClipboardAsync(CancellationToken cancellationToken)
    {
        var snapshot = ClipboardSnapshot.Capture();
        var sentinel = $"__LFT_CLIP_{Guid.NewGuid():N}__";
        var copied = string.Empty;

        try
        {
            if (!ClipboardSnapshot.TrySetText(sentinel))
            {
                return null;
            }

            try
            {
                NativeInput.SendCtrlC();
            }
            catch (InvalidOperationException exception)
            {
                AppLogger.Warn($"Clipboard capture failed: {exception.Message}");
                return null;
            }

            for (var attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(20, cancellationToken).ConfigureAwait(true);
                if (!ClipboardSnapshot.TryGetText(out var text) || text == sentinel)
                {
                    continue;
                }

                copied = text;
                break;
            }
        }
        finally
        {
            snapshot.Restore();
        }

        return copied;
    }
}
