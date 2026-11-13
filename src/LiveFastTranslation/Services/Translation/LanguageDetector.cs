namespace LiveFastTranslation.Services.Translation;

/// <summary>
/// Lightweight local language detector used for the source-language badge.
/// Translation itself is always delegated to the selected provider, which also performs its own detection.
/// </summary>
public static class LanguageDetector
{
    private static readonly Dictionary<string, string[]> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new[] { "the", "and", "is", "are", "was", "were", "to", "of", "in", "that", "it", "you", "for", "with", "this", "on", "have", "not" },
        ["de"] = new[] { "der", "die", "das", "und", "ist", "nicht", "mit", "ein", "eine", "zu", "den", "von", "sich", "auf", "für" },
        ["fr"] = new[] { "le", "la", "les", "et", "est", "une", "un", "des", "du", "dans", "pour", "que", "qui", "pas", "avec" },
        ["es"] = new[] { "el", "la", "los", "las", "y", "es", "una", "un", "de", "en", "que", "por", "con", "para", "no" },
        ["it"] = new[] { "il", "la", "gli", "le", "e", "è", "un", "una", "di", "che", "per", "con", "non", "sono" },
        ["pt"] = new[] { "o", "a", "os", "as", "e", "é", "um", "uma", "de", "que", "para", "com", "não", "em" },
        ["nl"] = new[] { "de", "het", "een", "en", "is", "van", "dat", "op", "te", "met", "voor", "niet" },
        ["pl"] = new[] { "i", "w", "z", "na", "się", "nie", "jest", "że", "do", "to", "dla" },
        ["sv"] = new[] { "och", "att", "det", "som", "en", "ett", "är", "för", "med", "inte", "på" },
        ["da"] = new[] { "og", "at", "det", "som", "en", "et", "er", "for", "med", "ikke", "på" },
        ["nb"] = new[] { "og", "å", "det", "som", "en", "et", "er", "for", "med", "ikke", "på" },
        ["fi"] = new[] { "ja", "on", "ei", "että", "se", "oli", "joka", "kanssa", "mutta", "niin" },
        ["tr"] = new[] { "ve", "bir", "bu", "da", "de", "için", "ile", "değil", "çok", "olarak" },
        ["ro"] = new[] { "și", "în", "este", "un", "o", "de", "la", "cu", "pentru", "nu" },
        ["id"] = new[] { "dan", "yang", "di", "ini", "itu", "dengan", "untuk", "tidak", "dari" },
        ["vi"] = new[] { "và", "là", "của", "có", "trong", "một", "không", "cho", "với" },
        ["cs"] = new[] { "a", "je", "se", "na", "že", "není", "pro", "jako" },
        ["sk"] = new[] { "a", "je", "sa", "na", "že", "nie", "pre", "ako" },
        ["sl"] = new[] { "in", "je", "se", "na", "da", "ne", "za", "kot" },
        ["hr"] = new[] { "i", "je", "u", "na", "se", "ne", "za", "sa" },
        ["sr"] = new[] { "и", "је", "у", "на", "се", "не", "за", "са" },
        ["ru"] = new[] { "и", "в", "не", "на", "что", "это", "с", "по", "для", "как" },
        ["uk"] = new[] { "і", "й", "що", "не", "на", "з", "це", "для", "як" },
        ["bg"] = new[] { "и", "е", "на", "в", "се", "не", "за", "с", "това" }
    };

    private static readonly Dictionary<string, string> SpecialLetters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["de"] = "ßäöü",
        ["fr"] = "àâçéèêëîïôùûüÿœ",
        ["es"] = "ñ¿¡áíóú",
        ["pt"] = "ãõçáíóú",
        ["it"] = "àèéìòù",
        ["pl"] = "ąćęłńóśźż",
        ["cs"] = "čřěůžýáíé",
        ["sk"] = "ľĺŕôäčťďšž",
        ["hu"] = "őűáéíóöü",
        ["tr"] = "ğışçöü",
        ["ro"] = "ăâîșț",
        ["sv"] = "åäö",
        ["da"] = "æøå",
        ["nb"] = "æøå",
        ["fi"] = "äö",
        ["et"] = "õäöü",
        ["lv"] = "āčēģīķļņšūž",
        ["lt"] = "ąčęėįšųūž",
        ["hr"] = "čćđšž",
        ["sr"] = "ђјљњћџ"
    };

    public static string Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "auto";
        }

        var sample = text.Trim();
        if (sample.Length > 4000)
        {
            sample = sample[..4000];
        }

        var hiragana = 0;
        var katakana = 0;
        var han = 0;
        var hangul = 0;
        var cyrillic = 0;
        var greek = 0;
        var hebrew = 0;
        var arabic = 0;
        var devanagari = 0;
        var thai = 0;
        var latin = 0;
        var cyrillicLetters = new HashSet<char>();

        foreach (var character in sample)
        {
            if (character is >= '\u3040' and <= '\u309F')
            {
                hiragana++;
            }
            else if (character is >= '\u30A0' and <= '\u30FF')
            {
                katakana++;
            }
            else if (character is >= '\u4E00' and <= '\u9FFF')
            {
                han++;
            }
            else if (character is >= '\uAC00' and <= '\uD7AF')
            {
                hangul++;
            }
            else if (character is >= '\u0400' and <= '\u04FF')
            {
                cyrillic++;
                cyrillicLetters.Add(character);
            }
            else if (character is >= '\u0370' and <= '\u03FF')
            {
                greek++;
            }
            else if (character is >= '\u0590' and <= '\u05FF')
            {
                hebrew++;
            }
            else if (character is >= '\u0600' and <= '\u06FF')
            {
                arabic++;
            }
            else if (character is >= '\u0900' and <= '\u097F')
            {
                devanagari++;
            }
            else if (character is >= '\u0E00' and <= '\u0E7F')
            {
                thai++;
            }
            else if (char.IsLetter(character) && (character < 128 || character is >= '\u00C0' and <= '\u024F'))
            {
                latin++;
            }
        }

        if (hangul > 0 && hangul >= han)
        {
            return "ko";
        }

        if (hiragana + katakana > 0)
        {
            return "ja";
        }

        if (han > 0)
        {
            return "zh-Hans";
        }

        if (cyrillic > 0)
        {
            return DetectCyrillic(cyrillicLetters);
        }

        if (greek > 0)
        {
            return "el";
        }

        if (hebrew > 0)
        {
            return "he";
        }

        if (arabic > 0)
        {
            return "ar";
        }

        if (devanagari > 0)
        {
            return "hi";
        }

        if (thai > 0)
        {
            return "th";
        }

        if (latin == 0)
        {
            return "auto";
        }

        return DetectLatin(sample);
    }

    private static string DetectCyrillic(HashSet<char> letters)
    {
        if ("іїєґІЇЄҐ".Any(letters.Contains))
        {
            return "uk";
        }

        if ("ђјљњћџЂЈЉЊЋЏ".Any(letters.Contains))
        {
            return "sr";
        }

        if ((letters.Contains('ы') || letters.Contains('э') || letters.Contains('ё')
             || letters.Contains('Ы') || letters.Contains('Э') || letters.Contains('Ё')))
        {
            return "ru";
        }

        if (letters.Contains('ъ') || letters.Contains('Ъ'))
        {
            return "bg";
        }

        return "ru";
    }

    private static string DetectLatin(string sample)
    {
        var normalized = sample.ToLowerInvariant();
        var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        void Add(string code, double value)
        {
            scores[code] = scores.GetValueOrDefault(code) + value;
        }

        foreach (var (code, letters) in SpecialLetters)
        {
            foreach (var letter in letters)
            {
                if (normalized.Contains(letter))
                {
                    Add(code, 1.2);
                }
            }
        }

        var words = normalized.Split(
            new[] { ' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '(', ')', '"', '«', '»', '/' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var (code, stopwords) in Stopwords)
        {
            if ((code is "ru" or "uk" or "bg" or "sr")
                && normalized.Any(c => c is >= '\u0400' and <= '\u04FF'))
            {
                continue;
            }

            var set = stopwords.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var word in words)
            {
                if (set.Contains(word))
                {
                    Add(code, 1.0);
                }
            }
        }

        if (scores.Count == 0)
        {
            return "en";
        }

        var best = scores.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).First();
        return best.Value >= 1.0 ? best.Key : "en";
    }
}
