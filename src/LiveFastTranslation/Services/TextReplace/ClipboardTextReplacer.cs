using LiveFastTranslation.Models;

namespace LiveFastTranslation.Services.TextReplace;

/// <summary>
/// Pastes the translation through the clipboard, preserving and restoring all clipboard formats
/// unless the user explicitly disables restoration.
/// </summary>
public sealed class ClipboardTextReplacer : ITextReplacer
{
    public string Name => "Буфер обмена";

    public async Task<ReplaceOutcome> ReplaceAsync(
        ReplaceContext context,
        string replacement,
        ReplaceOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = ClipboardSnapshot.Capture();
        try
        {
            if (!ClipboardSnapshot.TrySetText(replacement))
            {
                return ReplaceOutcome.Fail(Name, "Не удалось записать текст в буфер обмена (он занят другим приложением).");
            }

            Win32Focus.ActivateTargetWindow(context.TargetWindow);
            await Task.Delay(60, cancellationToken).ConfigureAwait(true);

            try
            {
                NativeInput.SendCtrlV();
            }
            catch (InvalidOperationException exception)
            {
                return ReplaceOutcome.Fail(Name, $"Не удалось отправить Ctrl+V: {exception.Message}");
            }

            await Task.Delay(180, cancellationToken).ConfigureAwait(true);

            if (options.MoveCaretAfterReplace && options.CursorPlacement != CursorPlacement.End)
            {
                var focused = Win32Focus.GetFocusedControlHwnd();
                Win32Focus.TrySetCaret(focused, replacement.Length, options.CursorPlacement);
            }

            return ReplaceOutcome.Ok(Name);
        }
        finally
        {
            if (options.RestoreClipboardAfterPaste)
            {
                // Give the target application time to consume the paste before restoring formats.
                await Task.Delay(220, CancellationToken.None).ConfigureAwait(true);
                snapshot.Restore();
            }
        }
    }
}
