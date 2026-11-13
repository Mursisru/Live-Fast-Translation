namespace LiveFastTranslation.Models;

public sealed class GeneralSettings
{
    public bool AutoStart { get; set; }

    public bool StartMinimized { get; set; }

    public bool SaveHistory { get; set; } = true;

    public int HistoryLimit { get; set; } = 50;

    public bool AllowClipboardCapture { get; set; } = true;

    public bool ShowTrayNotifications { get; set; } = true;
}
