using LiveFastTranslation.Models;
using LiveFastTranslation.Services.Settings;

namespace LiveFastTranslation.Services.History;

/// <summary>Keeps a bounded translation history inside settings.json.</summary>
public sealed class HistoryService
{
    private const int MaxStoredSourceLength = 4000;
    private const int MaxStoredTranslationLength = 4000;

    private readonly SettingsService _settings;

    public HistoryService(SettingsService settings)
    {
        _settings = settings;
    }

    public void Add(TranslationHistoryEntry entry)
    {
        var settings = _settings.Current;
        if (!settings.General.SaveHistory || settings.General.HistoryLimit == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(entry.SourceText) || string.IsNullOrWhiteSpace(entry.TranslatedText))
        {
            return;
        }

        entry.SourceText = Trim(entry.SourceText, MaxStoredSourceLength);
        entry.TranslatedText = Trim(entry.TranslatedText, MaxStoredTranslationLength);

        _settings.MutateSilently(current =>
        {
            current.History.Insert(0, entry);
            var limit = Math.Max(0, current.General.HistoryLimit);
            if (limit == 0)
            {
                current.History.Clear();
            }
            else if (current.History.Count > limit)
            {
                current.History.RemoveRange(limit, current.History.Count - limit);
            }
        });

        _settings.PersistInBackground();
    }

    public IReadOnlyList<TranslationHistoryEntry> GetEntries()
    {
        var settings = _settings.Current;
        return settings.General.SaveHistory
            ? settings.History.ToList()
            : Array.Empty<TranslationHistoryEntry>();
    }

    public void Clear()
    {
        _settings.MutateSilently(settings => settings.History.Clear());
        _settings.PersistInBackground();
    }

    private static string Trim(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
