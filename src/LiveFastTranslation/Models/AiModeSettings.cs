namespace LiveFastTranslation.Models;

public sealed class AiModeSettings
{
    public bool Enabled { get; set; }

    public int DelayMs { get; set; } = 700;

    public int MinSentenceLength { get; set; } = 4;

    public int MaxBufferLength { get; set; } = 4000;
}
