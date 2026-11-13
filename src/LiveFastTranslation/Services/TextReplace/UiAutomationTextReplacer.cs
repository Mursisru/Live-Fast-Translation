using System.Runtime.InteropServices;
using System.Windows.Automation;
using LiveFastTranslation.Models;

namespace LiveFastTranslation.Services.TextReplace;

/// <summary>
/// Uses UI Automation ValuePattern. To avoid destroying unrelated content, it only replaces the entire
/// field when the selection equals the whole value; partial selections fall through to safer strategies.
/// </summary>
public sealed class UiAutomationTextReplacer : ITextReplacer
{
    public string Name => "UI Automation";

    public Task<ReplaceOutcome> ReplaceAsync(
        ReplaceContext context,
        string replacement,
        ReplaceOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var element = GetElement(context);
            if (element is null)
            {
                return Task.FromResult(ReplaceOutcome.Fail(Name, "Активный элемент UI Automation недоступен."));
            }

            if (element.Current.IsPassword)
            {
                return Task.FromResult(ReplaceOutcome.Fail(Name, "Поле защищено, замена текста запрещена."));
            }

            if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var rawPattern))
            {
                return Task.FromResult(ReplaceOutcome.Fail(Name, "Элемент не поддерживает ValuePattern."));
            }

            var pattern = (ValuePattern)rawPattern;
            if (pattern.Current.IsReadOnly)
            {
                return Task.FromResult(ReplaceOutcome.Fail(Name, "Элемент доступен только для чтения."));
            }

            var currentValue = pattern.Current.Value ?? string.Empty;
            if (!SelectionMatchesWholeValue(context.SelectedText, currentValue))
            {
                return Task.FromResult(ReplaceOutcome.Fail(
                    Name,
                    "Выделена только часть текста; ValuePattern может заменить лишь всё поле."));
            }

            pattern.SetValue(replacement);

            if (options.MoveCaretAfterReplace)
            {
                var focused = Win32Focus.GetFocusedControlHwnd();
                Win32Focus.TrySetCaret(focused, replacement.Length, options.CursorPlacement);
            }

            return Task.FromResult(ReplaceOutcome.Ok(Name));
        }
        catch (Exception exception) when (
            exception is ElementNotAvailableException
                or InvalidOperationException
                or COMException
                or UnauthorizedAccessException)
        {
            return Task.FromResult(ReplaceOutcome.Fail(Name, $"ValuePattern: {exception.Message}"));
        }
    }

    private static AutomationElement? GetElement(ReplaceContext context)
    {
        if (context.Element is not null)
        {
            return context.Element;
        }

        try
        {
            return AutomationElement.FocusedElement;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static bool SelectionMatchesWholeValue(string selectedText, string currentValue)
    {
        if (string.Equals(selectedText, currentValue, StringComparison.Ordinal))
        {
            return true;
        }

        static string Normalize(string value) => value.Replace("\r\n", "\n").Trim();
        return string.Equals(Normalize(selectedText), Normalize(currentValue), StringComparison.Ordinal);
    }
}
