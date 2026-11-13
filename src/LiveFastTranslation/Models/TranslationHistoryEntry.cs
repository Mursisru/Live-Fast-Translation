namespace LiveFastTranslation.Models;

public sealed class TranslationHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    public string SourceText { get; set; } = string.Empty;

    public string TranslatedText { get; set; } = string.Empty;

    public string SourceLanguage { get; set; } = "auto";

    public string TargetLanguage { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string Summary
    {
        get
        {
            var source = SourceText.Replace('\r', ' ').Replace('\n', ' ');
            if (source.Length > 90)
            {
                source = source[..90] + "…";
            }

            return $"{Timestamp:dd.MM.yyyy HH:mm} · {source}";
        }
    }
}
