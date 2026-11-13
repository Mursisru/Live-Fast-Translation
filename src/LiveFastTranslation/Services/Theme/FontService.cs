using System.Windows;
using System.Windows.Media;
using MediaFontFamily = System.Windows.Media.FontFamily;

namespace LiveFastTranslation.Services.Theme;

/// <summary>
/// Applies the user-selected UI font to the application-wide <c>AppFontFamily</c> resource.
/// Invalid or missing families fall back to Segoe UI so a typo can never break the layout.
/// </summary>
public static class FontService
{
    public const string FallbackFamily = "Segoe UI";

    public static void Apply(string? familyName)
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        application.Resources["AppFontFamily"] = Create(familyName);
    }

    public static MediaFontFamily Create(string? familyName)
    {
        var normalized = string.IsNullOrWhiteSpace(familyName) ? FallbackFamily : familyName.Trim();
        try
        {
            var family = new MediaFontFamily(normalized);
            return string.IsNullOrWhiteSpace(family.Source) ? new MediaFontFamily(FallbackFamily) : family;
        }
        catch (ArgumentException)
        {
            return new MediaFontFamily(FallbackFamily);
        }
    }
}
