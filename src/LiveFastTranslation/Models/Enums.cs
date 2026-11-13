namespace LiveFastTranslation.Models;

public enum ReplacementStrategy
{
    Auto,
    UiAutomation,
    Win32Message,
    SendKeys,
    Clipboard
}

public enum CursorPlacement
{
    End,
    Start,
    SelectAll
}

public enum CaptureMethod
{
    None,
    UiAutomationTextPattern,
    Win32EditControl,
    Clipboard
}
