namespace LiveFastTranslation.Models;

/// <summary>A language that can be selected as translation target.</summary>
public sealed record LanguageOption(
    string Code,
    string EnglishName,
    string NativeName,
    string Flag)
{
    public string DisplayName => $"{NativeName} ({EnglishName})";

    public string Compact(bool showFlag, bool showCode)
    {
        if (showFlag && showCode)
        {
            return $"{Flag} {Code.ToUpperInvariant()}";
        }

        if (showFlag)
        {
            return $"{Flag} {NativeName}";
        }

        return showCode ? Code.ToUpperInvariant() : NativeName;
    }
}

/// <summary>
/// Language metadata and currently available/installed target languages.
/// Available languages are detected from installed Argos Translate packages.
/// </summary>
public static class LanguageCatalog
{
    public static IReadOnlyList<LanguageOption> MasterAll { get; } = new List<LanguageOption>
    {
        new("en", "English", "English", "🇬🇧"),
        new("en-US", "English (US)", "English (US)", "🇺🇸"),
        new("en-GB", "English (UK)", "English (UK)", "🇬🇧"),
        new("ru", "Russian", "Русский", "🇷🇺"),
        new("uk", "Ukrainian", "Українська", "🇺🇦"),
        new("de", "German", "Deutsch", "🇩🇪"),
        new("fr", "French", "Français", "🇫🇷"),
        new("es", "Spanish", "Español", "🇪🇸"),
        new("it", "Italian", "Italiano", "🇮🇹"),
        new("pt", "Portuguese", "Português", "🇵🇹"),
        new("pt-BR", "Portuguese (Brazil)", "Português (Brasil)", "🇧🇷"),
        new("nl", "Dutch", "Nederlands", "🇳🇱"),
        new("pl", "Polish", "Polski", "🇵🇱"),
        new("cs", "Czech", "Čeština", "🇨🇿"),
        new("sk", "Slovak", "Slovenčina", "🇸🇰"),
        new("sv", "Swedish", "Svenska", "🇸🇪"),
        new("da", "Danish", "Dansk", "🇩🇰"),
        new("fi", "Finnish", "Suomi", "🇫🇮"),
        new("nb", "Norwegian", "Norsk", "🇳🇴"),
        new("el", "Greek", "Ελληνικά", "🇬🇷"),
        new("tr", "Turkish", "Türkçe", "🇹🇷"),
        new("ro", "Romanian", "Română", "🇷🇴"),
        new("hu", "Hungarian", "Magyar", "🇭🇺"),
        new("bg", "Bulgarian", "Български", "🇧🇬"),
        new("hr", "Croatian", "Hrvatski", "🇭🇷"),
        new("sr", "Serbian", "Српски", "🇷🇸"),
        new("sl", "Slovenian", "Slovenščina", "🇸🇮"),
        new("et", "Estonian", "Eesti", "🇪🇪"),
        new("lv", "Latvian", "Latviešu", "🇱🇻"),
        new("lt", "Lithuanian", "Lietuvių", "🇱🇹"),
        new("ar", "Arabic", "العربية", "🇸🇦"),
        new("he", "Hebrew", "עברית", "🇮🇱"),
        new("fa", "Persian", "فارسی", "🇮🇷"),
        new("hi", "Hindi", "हिन्दी", "🇮🇳"),
        new("th", "Thai", "ไทย", "🇹🇭"),
        new("vi", "Vietnamese", "Tiếng Việt", "🇻🇳"),
        new("id", "Indonesian", "Bahasa Indonesia", "🇮🇩"),
        new("ms", "Malay", "Bahasa Melayu", "🇲🇾"),
        new("ja", "Japanese", "日本語", "🇯🇵"),
        new("ko", "Korean", "한국어", "🇰🇷"),
        new("zh-Hans", "Chinese (Simplified)", "简体中文", "🇨🇳"),
        new("zh-Hant", "Chinese (Traditional)", "繁體中文", "🇹🇼")
    };

    private static readonly Dictionary<string, LanguageOption> MasterByCode =
        MasterAll.ToDictionary(language => language.Code, StringComparer.OrdinalIgnoreCase);

    private static readonly object Sync = new();
    private static IReadOnlyList<LanguageOption> _available = MasterAll.ToList();
    private static Dictionary<string, LanguageOption> _availableByCode =
        _available.ToDictionary(language => language.Code, StringComparer.OrdinalIgnoreCase);

    /// <summary>Languages currently available as translation targets (detected from installed Argos packages).</summary>
    public static IReadOnlyList<LanguageOption> All
    {
        get
        {
            lock (Sync)
            {
                return _available;
            }
        }
    }

    /// <summary>Updates the available target languages. Pass app language codes (e.g. en, ru, zh-Hans).</summary>
    public static void SetAvailable(IEnumerable<string> codes)
    {
        var selected = new List<LanguageOption>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in codes ?? Array.Empty<string>())
        {
            var option = ResolveOption(code);
            if (option is null || !seen.Add(option.Code))
            {
                continue;
            }

            selected.Add(option);
        }

        // Keep stable, user-friendly order based on master catalog, then unknowns.
        selected = selected
            .OrderBy(o => MasterIndex(o.Code))
            .ThenBy(o => o.NativeName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (selected.Count == 0)
        {
            selected = new List<LanguageOption> { MasterByCode["en"] };
        }

        lock (Sync)
        {
            _available = selected;
            _availableByCode = selected.ToDictionary(language => language.Code, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Restores the full master language list (useful for tests/reset scenarios).</summary>
    public static void ResetToMaster()
    {
        lock (Sync)
        {
            _available = MasterAll.ToList();
            _availableByCode = _available.ToDictionary(language => language.Code, StringComparer.OrdinalIgnoreCase);
        }
    }

    public static LanguageOption? Find(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var trimmed = code.Trim();
        lock (Sync)
        {
            if (_availableByCode.TryGetValue(trimmed, out var available))
            {
                return available;
            }
        }

        // Try normalized/aliased lookup against available.
        var normalized = NormalizeToAppCode(trimmed);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            lock (Sync)
            {
                if (_availableByCode.TryGetValue(normalized, out var availableNormalized))
                {
                    return availableNormalized;
                }
            }
        }

        return null;
    }

    /// <summary>Finds language metadata in master catalog (including currently unavailable languages).</summary>
    public static LanguageOption? FindAny(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var trimmed = code.Trim();
        if (MasterByCode.TryGetValue(trimmed, out var option))
        {
            return option;
        }

        var normalized = NormalizeToAppCode(trimmed);
        if (!string.IsNullOrWhiteSpace(normalized) && MasterByCode.TryGetValue(normalized, out option))
        {
            return option;
        }

        // Fallback for detected/source codes not in master list.
        var simple = trimmed.ToLowerInvariant();
        return new LanguageOption(simple, simple.ToUpperInvariant(), simple.ToUpperInvariant(), "🌐");
    }

    public static LanguageOption Get(string code)
    {
        return Find(code) ?? FindAny(code) ?? throw new ArgumentException($"Unknown language code '{code}'.", nameof(code));
    }

    public static string GetPromptName(string code)
    {
        return FindAny(code)?.EnglishName ?? code;
    }

    public static bool IsSupported(string? code) => Find(code) is not null;

    public static string DefaultTargetCode
    {
        get
        {
            var ui = System.Globalization.CultureInfo.CurrentUICulture.Name;
            if (IsSupported(ui))
            {
                return Find(ui)!.Code;
            }

            var two = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            if (IsSupported(two))
            {
                return Find(two)!.Code;
            }

            lock (Sync)
            {
                return _available.Count > 0 ? _available[0].Code : "en";
            }
        }
    }

    /// <summary>Maps Argos/engine language codes to app language codes when possible.</summary>
    public static string NormalizeToAppCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        var c = code.Trim();
        if (MasterByCode.ContainsKey(c))
        {
            return MasterByCode[c].Code;
        }

        return c.ToLowerInvariant() switch
        {
            "en" => "en",
            "en-us" => "en-US",
            "en-gb" => "en-GB",
            "pt-br" => "pt-BR",
            "zh" => "zh-Hans",
            "zh-cn" => "zh-Hans",
            "zh-hans" => "zh-Hans",
            "zt" => "zh-Hant",
            "zh-tw" => "zh-Hant",
            "zh-hant" => "zh-Hant",
            "no" => "nb",
            "nb" => "nb",
            "iw" => "he",
            _ => c.Contains('-', StringComparison.Ordinal) ? c.Split('-', 2)[0].ToLowerInvariant() : c.ToLowerInvariant()
        };
    }

    /// <summary>Maps app language code to Argos language code.</summary>
    public static string ToArgosCode(string? appCode)
    {
        return (appCode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "zh-hans" => "zh",
            "zh-cn" => "zh",
            "zh-hant" => "zt",
            "zh-tw" => "zt",
            "pt-br" => "pt",
            "en-us" => "en",
            "en-gb" => "en",
            "nb" => "no",
            _ => (appCode ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                var x when x.Contains('-', StringComparison.Ordinal) => x.Split('-', 2)[0],
                var x => x
            }
        };
    }

    private static LanguageOption? ResolveOption(string code)
    {
        var appCode = NormalizeToAppCode(code);
        if (MasterByCode.TryGetValue(appCode, out var option))
        {
            return option;
        }

        if (MasterByCode.TryGetValue(code, out option))
        {
            return option;
        }

        return FindAny(code);
    }

    private static int MasterIndex(string code)
    {
        for (var i = 0; i < MasterAll.Count; i++)
        {
            if (MasterAll[i].Code.Equals(code, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return int.MaxValue;
    }
}
