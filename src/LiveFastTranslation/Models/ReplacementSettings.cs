namespace LiveFastTranslation.Models;

public sealed class ReplacementSettings
{
    public ReplacementStrategy Strategy { get; set; } = ReplacementStrategy.Auto;

    public int DelayBeforeReplaceMs { get; set; } = 120;

    public CursorPlacement CursorPlacement { get; set; } = CursorPlacement.End;

    public bool RestoreClipboardAfterPaste { get; set; } = true;

    public bool ActivateTargetWindow { get; set; } = true;

    public bool MoveCaretAfterReplace { get; set; } = true;
}
