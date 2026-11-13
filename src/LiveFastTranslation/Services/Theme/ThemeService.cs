using System.Windows;
using System.Windows.Media;
using LiveFastTranslation.Models;

namespace LiveFastTranslation.Services.Theme;

/// <summary>Applies one of the 64 built-in theme palettes by swapping an application resource dictionary.</summary>
public sealed class ThemeService : IDisposable
{
    private bool _loaded;
    private string _themeId = ThemeCatalog.DefaultThemeId;

    public string CurrentThemeId => _themeId;

    public ThemeDefinition CurrentTheme => ThemeCatalog.Get(_themeId);

    public bool IsDark => CurrentTheme.IsDark;

    public event EventHandler? ThemeApplied;

    public void Apply(string? themeId)
    {
        var theme = ThemeCatalog.Get(themeId);
        if (_loaded && string.Equals(_themeId, theme.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SwapDictionary(theme);
        _themeId = theme.Id;
        _loaded = true;
        ThemeApplied?.Invoke(this, EventArgs.Empty);
    }

    private static void SwapDictionary(ThemeDefinition theme)
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        var dictionaries = application.Resources.MergedDictionaries;
        for (var index = dictionaries.Count - 1; index >= 0; index--)
        {
            var source = dictionaries[index].Source?.OriginalString ?? string.Empty;
            if (source.Contains("/Themes/Light.xaml", StringComparison.OrdinalIgnoreCase)
                || source.Contains("/Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase))
            {
                dictionaries.RemoveAt(index);
                continue;
            }

            if (dictionaries[index].Contains("__LiveFastThemePalette"))
            {
                dictionaries.RemoveAt(index);
            }
        }

        dictionaries.Add(BuildDictionary(theme));
    }

    private static ResourceDictionary BuildDictionary(ThemeDefinition theme)
    {
        var dictionary = new ResourceDictionary
        {
            ["__LiveFastThemePalette"] = true
        };

        SetBrush(dictionary, "AppBackgroundBrush", theme.AppBackground);
        SetBrush(dictionary, "SurfaceBrush", theme.Surface);
        SetBrush(dictionary, "SurfaceAltBrush", theme.SurfaceAlt);
        SetBrush(dictionary, "SurfaceRaisedBrush", theme.SurfaceRaised);
        SetBrush(dictionary, "BorderBrush", theme.Border);
        SetBrush(dictionary, "BorderStrongBrush", theme.BorderStrong);
        SetBrush(dictionary, "TextPrimaryBrush", theme.TextPrimary);
        SetBrush(dictionary, "TextSecondaryBrush", theme.TextSecondary);
        SetBrush(dictionary, "TextMutedBrush", theme.TextMuted);
        SetBrush(dictionary, "AccentBrush", theme.Accent);
        SetBrush(dictionary, "AccentHoverBrush", theme.AccentHover);
        SetBrush(dictionary, "AccentPressedBrush", theme.AccentPressed);
        SetBrush(dictionary, "AccentSoftBrush", theme.AccentSoft);
        SetBrush(dictionary, "OnAccentBrush", theme.OnAccent);
        SetBrush(dictionary, "ErrorBrush", theme.Error);
        SetBrush(dictionary, "ErrorSoftBrush", theme.ErrorSoft);
        SetBrush(dictionary, "WarningBrush", theme.Warning);
        SetBrush(dictionary, "SuccessBrush", theme.Success);
        SetBrush(dictionary, "HoverBrush", theme.Hover);
        SetBrush(dictionary, "SelectedBrush", theme.Selected);
        SetBrush(dictionary, "InputBackgroundBrush", theme.InputBackground);
        SetBrush(dictionary, "PopupBackgroundBrush", theme.PopupBackground);
        SetBrush(dictionary, "OverlayBackgroundBrush", theme.OverlayBackground);
        SetBrush(dictionary, "OverlayBorderBrush", theme.OverlayBorder);
        SetBrush(dictionary, "OverlaySeparatorBrush", theme.OverlaySeparator);
        SetBrush(dictionary, "TooltipBackgroundBrush", theme.TooltipBackground);
        SetBrush(dictionary, "TooltipForegroundBrush", theme.TooltipForeground);
        SetBrush(dictionary, "ScrollBarBrush", theme.ScrollBar);
        SetBrush(dictionary, "WindowBorderBrush", theme.WindowBorder);
        dictionary["ShadowColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.ShadowColor);

        return dictionary;
    }

    private static void SetBrush(ResourceDictionary dictionary, string key, string color)
    {
        var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        dictionary[key] = brush;
    }

    public void Dispose()
    {
        // No timer/resources to release; kept for host lifecycle symmetry.
    }
}
