using LiveFastTranslation.Models;

namespace LiveFastTranslation.Services.Translation;

/// <summary>
/// Picks the translation direction from the user's native language: text written in the
/// native language turns into English, everything else turns into the native language.
/// </summary>
public static class TranslationTargetResolver
{
    public static string Resolve(string text, string nativeLanguage)
    {
        var native = LanguageCatalog.NormalizeToAppCode(nativeLanguage);
        var source = LanguageCatalog.NormalizeToAppCode(LanguageDetector.Detect(text));
        var nativeBase = BaseCode(native);
        var sourceBase = BaseCode(source);

        var desired = sourceBase.Length > 0 && sourceBase.Equals(nativeBase, StringComparison.OrdinalIgnoreCase)
            ? (nativeBase.Equals("en", StringComparison.OrdinalIgnoreCase) ? "ru" : "en")
            : native;

        return EnsureAvailable(desired, sourceBase);
    }

    private static string EnsureAvailable(string desired, string sourceBase)
    {
        var exact = LanguageCatalog.Find(desired);
        if (exact is not null && !BaseCode(exact.Code).Equals(sourceBase, StringComparison.OrdinalIgnoreCase))
        {
            return exact.Code;
        }

        var sameLanguage = LanguageCatalog.All.FirstOrDefault(
            language => BaseCode(language.Code).Equals(BaseCode(desired), StringComparison.OrdinalIgnoreCase)
                        && !BaseCode(language.Code).Equals(sourceBase, StringComparison.OrdinalIgnoreCase));
        if (sameLanguage is not null)
        {
            return sameLanguage.Code;
        }

        var alternative = LanguageCatalog.All.FirstOrDefault(
            language => !BaseCode(language.Code).Equals(sourceBase, StringComparison.OrdinalIgnoreCase));
        return alternative?.Code
               ?? LanguageCatalog.All.FirstOrDefault()?.Code
               ?? LanguageCatalog.DefaultTargetCode;
    }

    private static string BaseCode(string? code)
    {
        var normalized = LanguageCatalog.NormalizeToAppCode(code);
        var separator = normalized.IndexOf('-');
        return (separator > 0 ? normalized[..separator] : normalized).ToLowerInvariant();
    }
}
