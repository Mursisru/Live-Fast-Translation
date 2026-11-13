using System.Windows.Automation;
using LiveFastTranslation.Models;

namespace LiveFastTranslation.Services.TextCapture;

public sealed class TextCaptureResult
{
    public string Text { get; init; } = string.Empty;

    public AutomationElement? Element { get; init; }

    public IntPtr TargetWindow { get; init; }

    public int ProcessId { get; init; }

    public CaptureMethod Method { get; init; }

    public bool IsSensitive { get; init; }

    public string? Error { get; init; }

    public bool HasText => !string.IsNullOrWhiteSpace(Text);
}

public interface ITextCaptureService
{
    Task<TextCaptureResult> CaptureSelectedTextAsync(
        bool allowClipboardFallback,
        CancellationToken cancellationToken = default);
}
