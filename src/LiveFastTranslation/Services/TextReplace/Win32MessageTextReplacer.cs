using LiveFastTranslation.Models;

namespace LiveFastTranslation.Services.TextReplace;

/// <summary>Replaces the current selection in classic Win32 edit controls using EM_REPLACESEL.</summary>
public sealed class Win32MessageTextReplacer : ITextReplacer
{
    public string Name => "Win32 EM_REPLACESEL";

    public Task<ReplaceOutcome> ReplaceAsync(
        ReplaceContext context,
        string replacement,
        ReplaceOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var focusedControl = Win32Focus.GetFocusedControlHwnd();
        if (focusedControl == IntPtr.Zero || !Win32Focus.IsEditLike(focusedControl))
        {
            return Task.FromResult(ReplaceOutcome.Fail(Name, "Активный элемент не является стандартным полем ввода."));
        }

        if (Win32Focus.IsPasswordField(focusedControl))
        {
            return Task.FromResult(ReplaceOutcome.Fail(Name, "Поле защищено, замена текста запрещена."));
        }

        try
        {
            if (!Win32Focus.TryReplaceSelection(focusedControl, replacement))
            {
                return Task.FromResult(ReplaceOutcome.Fail(Name, "Не удалось отправить EM_REPLACESEL."));
            }

            if (options.MoveCaretAfterReplace && options.CursorPlacement != CursorPlacement.End)
            {
                Win32Focus.TrySetCaret(focusedControl, replacement.Length, options.CursorPlacement);
            }

            return Task.FromResult(ReplaceOutcome.Ok(Name));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ReplaceOutcome.Fail(Name, $"EM_REPLACESEL: {exception.Message}"));
        }
    }
}
