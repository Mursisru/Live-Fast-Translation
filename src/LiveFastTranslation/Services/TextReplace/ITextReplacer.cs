using System.Windows.Automation;
using LiveFastTranslation.Models;

namespace LiveFastTranslation.Services.TextReplace;

public sealed class ReplaceContext
{
    public ReplaceContext(
        string selectedText,
        AutomationElement? element,
        IntPtr targetWindow,
        int processId)
    {
        SelectedText = selectedText;
        Element = element;
        TargetWindow = targetWindow;
        ProcessId = processId;
    }

    public string SelectedText { get; }

    public AutomationElement? Element { get; }

    public IntPtr TargetWindow { get; }

    public int ProcessId { get; }
}

public sealed class ReplaceOptions
{
    public int DelayBeforeReplaceMs { get; init; } = 120;

    public CursorPlacement CursorPlacement { get; init; } = CursorPlacement.End;

    public bool RestoreClipboardAfterPaste { get; init; } = true;

    public bool ActivateTargetWindow { get; init; } = true;

    public bool MoveCaretAfterReplace { get; init; } = true;

    public bool AnimateInsertion { get; init; } = true;

    public static ReplaceOptions FromSettings(ReplacementSettings settings)
    {
        return new ReplaceOptions
        {
            DelayBeforeReplaceMs = settings.DelayBeforeReplaceMs,
            CursorPlacement = settings.CursorPlacement,
            RestoreClipboardAfterPaste = settings.RestoreClipboardAfterPaste,
            ActivateTargetWindow = settings.ActivateTargetWindow,
            MoveCaretAfterReplace = settings.MoveCaretAfterReplace,
            AnimateInsertion = settings.AnimateInsertion
        };
    }
}

public sealed record ReplaceOutcome(bool Success, string Method, string? Error)
{
    public static ReplaceOutcome Ok(string method) => new(true, method, null);

    public static ReplaceOutcome Fail(string method, string error) => new(false, method, error);
}

public interface ITextReplacer
{
    string Name { get; }

    Task<ReplaceOutcome> ReplaceAsync(
        ReplaceContext context,
        string replacement,
        ReplaceOptions options,
        CancellationToken cancellationToken = default);
}
