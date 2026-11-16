using System.Globalization;
using Media = System.Windows.Media;

namespace LiveFastTranslation.Models;

public sealed record ThemeDefinition(
    string Id,
    string Name,
    bool IsDark,
    string AppBackground,
    string Surface,
    string SurfaceAlt,
    string SurfaceRaised,
    string Border,
    string BorderStrong,
    string TextPrimary,
    string TextSecondary,
    string TextMuted,
    string Accent,
    string AccentHover,
    string AccentPressed,
    string AccentSoft,
    string OnAccent,
    string Error,
    string ErrorSoft,
    string Warning,
    string Success,
    string Hover,
    string Selected,
    string InputBackground,
    string PopupBackground,
    string OverlayBackground,
    string OverlayBorder,
    string OverlaySeparator,
    string TooltipBackground,
    string TooltipForeground,
    string ScrollBar,
    string WindowBorder,
    string ShadowColor)
{
    public string GroupName => IsDark ? "Тёмные" : "Светлые";
    public string DisplayName => $"{(IsDark ? "Тёмная" : "Светлая")}: {Name}";

    // Preview brushes for the theme gallery. Frozen and cached, so binding many
    // cards never allocates brushes while scrolling.
    public Media.Brush PreviewBackground => ThemeCatalog.CreateBrush(AppBackground);
    public Media.Brush PreviewPanel => ThemeCatalog.CreateBrush(Surface);
    public Media.Brush PreviewBorder => ThemeCatalog.CreateBrush(Border);
    public Media.Brush PreviewText => ThemeCatalog.CreateBrush(TextPrimary);
    public Media.Brush PreviewMuted => ThemeCatalog.CreateBrush(TextMuted);
    public Media.Brush PreviewAccent => ThemeCatalog.CreateBrush(Accent);
    public Media.Brush PreviewOnAccent => ThemeCatalog.CreateBrush(OnAccent);
}

/// <summary>64 built-in UI themes (32 light, 32 dark) with all brush resources used by the app.</summary>
public static class ThemeCatalog
{
    public const string DefaultThemeId = "light-sky";

    public static IReadOnlyList<ThemeDefinition> All { get; } = Build();

    private static readonly Dictionary<string, ThemeDefinition> ById = All.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Media.Brush> BrushCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object BrushLock = new();

    /// <summary>Returns a frozen brush for a palette colour; repeated calls reuse the instance.</summary>
    public static Media.Brush CreateBrush(string color)
    {
        lock (BrushLock)
        {
            if (BrushCache.TryGetValue(color, out var cached))
            {
                return cached;
            }

            var brush = new Media.SolidColorBrush((Media.Color)Media.ColorConverter.ConvertFromString(color));
            brush.Freeze();
            BrushCache[color] = brush;
            return brush;
        }
    }

    public static ThemeDefinition Get(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id) && ById.TryGetValue(id.Trim(), out var theme))
        {
            return theme;
        }

        return ById.TryGetValue(id?.Trim() switch
        {
            "System" => "light-sky",
            "Light" => "light-sky",
            "Dark" => "dark-sky",
            _ => DefaultThemeId
        }, out var mapped)
            ? mapped
            : ById[DefaultThemeId];
    }

    public static bool IsKnown(string? id) => !string.IsNullOrWhiteSpace(id) && ById.ContainsKey(id.Trim());

    public static string NormalizeId(string? id) => Get(id).Id;

    private static IReadOnlyList<ThemeDefinition> Build()
    {
        var lightNames = new (string Id, string Name, string Accent)[]
        {
            ("light-sky", "Небесная", "#2F6BFF"),
            ("light-azure", "Лазурная", "#0078D4"),
            ("light-ocean", "Океан", "#0F766E"),
            ("light-mint", "Мятная", "#059669"),
            ("light-forest", "Лесная", "#2F855A"),
            ("light-olive", "Оливковая", "#658D1B"),
            ("light-lime", "Лайм", "#65A30D"),
            ("light-sun", "Солнечная", "#D97706"),
            ("light-amber", "Янтарная", "#B45309"),
            ("light-peach", "Персиковая", "#EA580C"),
            ("light-coral", "Коралловая", "#E11D48"),
            ("light-rose", "Розовая", "#DB2777"),
            ("light-magenta", "Маджента", "#C026D3"),
            ("light-lilac", "Сиреневая", "#9333EA"),
            ("light-violet", "Фиолетовая", "#7C3AED"),
            ("light-indigo", "Индиго", "#4F46E5"),
            ("light-cobalt", "Кобальт", "#2563EB"),
            ("light-steel", "Стальная", "#475569"),
            ("light-slate", "Сланцевая", "#334155"),
            ("light-graphite", "Графит", "#374151"),
            ("light-sand", "Песочная", "#A16207"),
            ("light-cream", "Кремовая", "#CA8A04"),
            ("light-lavender", "Лавандовая", "#8B5CF6"),
            ("light-ice", "Ледяная", "#0284C7"),
            ("light-teal", "Бирюзовая", "#0D9488"),
            ("light-emerald", "Изумрудная", "#10B981"),
            ("light-ruby", "Рубиновая", "#BE123C"),
            ("light-cherry", "Вишнёвая", "#9F1239"),
            ("light-copper", "Медная", "#C2410C"),
            ("light-gold", "Золотая", "#B45309"),
            ("light-silver", "Серебряная", "#64748B"),
            ("light-paper", "Бумажная", "#0369A1"),
        };

        var darkNames = new (string Id, string Name, string Accent)[]
        {
            ("dark-sky", "Небесная", "#5C8DFF"),
            ("dark-azure", "Лазурная", "#4CC2FF"),
            ("dark-ocean", "Океан", "#2DD4BF"),
            ("dark-mint", "Мятная", "#34D399"),
            ("dark-forest", "Лесная", "#4ADE80"),
            ("dark-olive", "Оливковая", "#A3E635"),
            ("dark-lime", "Лайм", "#84CC16"),
            ("dark-sun", "Солнечная", "#FBBF24"),
            ("dark-amber", "Янтарная", "#F59E0B"),
            ("dark-peach", "Персиковая", "#FB923C"),
            ("dark-coral", "Коралловая", "#FB7185"),
            ("dark-rose", "Розовая", "#F472B6"),
            ("dark-magenta", "Маджента", "#E879F9"),
            ("dark-lilac", "Сиреневая", "#C084FC"),
            ("dark-violet", "Фиолетовая", "#A78BFA"),
            ("dark-indigo", "Индиго", "#818CF8"),
            ("dark-cobalt", "Кобальт", "#60A5FA"),
            ("dark-steel", "Стальная", "#94A3B8"),
            ("dark-slate", "Сланцевая", "#CBD5E1"),
            ("dark-graphite", "Графит", "#D1D5DB"),
            ("dark-sand", "Песочная", "#FCD34D"),
            ("dark-cream", "Кремовая", "#FDE68A"),
            ("dark-lavender", "Лавандовая", "#D8B4FE"),
            ("dark-ice", "Ледяная", "#7DD3FC"),
            ("dark-teal", "Бирюзовая", "#5EEAD4"),
            ("dark-emerald", "Изумрудная", "#6EE7B7"),
            ("dark-ruby", "Рубиновая", "#FDA4AF"),
            ("dark-cherry", "Вишнёвая", "#FB7185"),
            ("dark-copper", "Медная", "#FDBA74"),
            ("dark-gold", "Золотая", "#FCD34D"),
            ("dark-silver", "Серебряная", "#E2E8F0"),
            ("dark-paper", "Бумажная", "#BAE6FD"),
        };

        var themes = new List<ThemeDefinition>(64);
        themes.AddRange(lightNames.Select(x => CreateLight(x.Id, x.Name, x.Accent)));
        themes.AddRange(darkNames.Select(x => CreateDark(x.Id, x.Name, x.Accent)));
        return themes;
    }

    /// <summary>
    /// Cursor-style light palette: neutral near-white chrome, white surfaces and a single
    /// accent hue per theme. Surfaces deliberately stay neutral so all 32 light themes share
    /// the same calm, professional base and differ only by their accent colour.
    /// </summary>
    private static ThemeDefinition CreateLight(string id, string name, string accentHex)
    {
        var accent = Parse(accentHex);
        var hover = Darken(accent, 0.10);
        var pressed = Darken(accent, 0.20);
        var soft = WithAlpha(accent, 0x1A);
        var selected = WithAlpha(accent, 0x24);
        var appBg = Parse("#F6F6F7");
        var surface = Parse("#FFFFFF");
        var surfaceAlt = Parse("#F1F1F2");
        var raised = Parse("#FFFFFF");
        var border = Parse("#E4E4E6");
        var borderStrong = Parse("#CFCFD2");
        var input = Parse("#FFFFFF");
        var popup = Parse("#FFFFFF");
        var rowHover = Parse("#ECECEE");
        var overlay = WithAlpha(Parse("#FFFFFF"), 0xF7);
        var overlayBorder = WithAlpha(Parse("#1F1F1F"), 0x22);
        var overlaySep = WithAlpha(Parse("#000000"), 0x14);
        return new ThemeDefinition(
            id, name, false,
            Hex(appBg), Hex(surface), Hex(surfaceAlt), Hex(raised),
            Hex(border), Hex(borderStrong),
            "#1F1F1F", "#5C5C61", "#8A8A8F",
            Hex(accent), Hex(hover), Hex(pressed), Hex(soft),
            ContrastOn(accent),
            "#C42B1C", "#FDE7E9", "#9A6700", "#0F7B3F",
            Hex(rowHover), Hex(selected),
            Hex(input), Hex(popup),
            Hex(overlay), Hex(overlayBorder), Hex(overlaySep),
            "#F21F1F1F", "#FFFFFF",
            Hex(Parse("#C6C6C9")), Hex(border),
            "#26000000");
    }

    /// <summary>
    /// Cursor-style dark palette: #1E1E1E editor, #181818 chrome, #252526 panels, thin
    /// #2B2B2B separators and a restrained accent. Neutral by design for every dark theme.
    /// </summary>
    private static ThemeDefinition CreateDark(string id, string name, string accentHex)
    {
        var accent = Parse(accentHex);
        var hover = Lighten(accent, 0.08);
        var pressed = Darken(accent, 0.10);
        var soft = WithAlpha(accent, 0x2E);
        var selected = WithAlpha(accent, 0x38);
        var appBg = Parse("#1E1E1E");
        var surface = Parse("#252526");
        var surfaceAlt = Parse("#181818");
        var raised = Parse("#2D2D2E");
        var border = Parse("#2B2B2B");
        var borderStrong = Parse("#3C3C3D");
        var input = Parse("#1E1E1E");
        var popup = Parse("#252526");
        var rowHover = Parse("#2A2A2B");
        var overlay = WithAlpha(Parse("#202020"), 0xF2);
        var overlayBorder = WithAlpha(Parse("#FFFFFF"), 0x30);
        var overlaySep = WithAlpha(Parse("#FFFFFF"), 0x1C);
        return new ThemeDefinition(
            id, name, true,
            Hex(appBg), Hex(surface), Hex(surfaceAlt), Hex(raised),
            Hex(border), Hex(borderStrong),
            "#E6E6E6", "#B3B3B3", "#8A8A8A",
            Hex(accent), Hex(hover), Hex(pressed), Hex(soft),
            ContrastOn(accent),
            "#F48771", "#3A2422", "#CCA700", "#89D185",
            Hex(rowHover), Hex(selected),
            Hex(input), Hex(popup),
            Hex(overlay), Hex(overlayBorder), Hex(overlaySep),
            "#F52A2A2A", "#E6E6E6",
            Hex(Parse("#4A4A4D")), Hex(Parse("#303031")),
            "#A0000000");
    }

    private readonly record struct Rgba(byte A, byte R, byte G, byte B);

    private static Rgba Parse(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length == 6)
        {
            return new Rgba(0xFF,
                byte.Parse(hex[..2], NumberStyles.HexNumber),
                byte.Parse(hex[2..4], NumberStyles.HexNumber),
                byte.Parse(hex[4..6], NumberStyles.HexNumber));
        }

        if (hex.Length == 8)
        {
            return new Rgba(
                byte.Parse(hex[..2], NumberStyles.HexNumber),
                byte.Parse(hex[2..4], NumberStyles.HexNumber),
                byte.Parse(hex[4..6], NumberStyles.HexNumber),
                byte.Parse(hex[6..8], NumberStyles.HexNumber));
        }

        throw new ArgumentException($"Invalid color: {hex}");
    }

    private static Rgba WithAlpha(Rgba c, byte a) => c with { A = a };

    private static Rgba Mix(Rgba a, Rgba b, double amountOfB)
    {
        amountOfB = Math.Clamp(amountOfB, 0, 1);
        var amountOfA = 1 - amountOfB;
        return new Rgba(
            (byte)Math.Round(a.A * amountOfA + b.A * amountOfB),
            (byte)Math.Round(a.R * amountOfA + b.R * amountOfB),
            (byte)Math.Round(a.G * amountOfA + b.G * amountOfB),
            (byte)Math.Round(a.B * amountOfA + b.B * amountOfB));
    }

    private static Rgba Darken(Rgba c, double amount) => Mix(c, new Rgba(255, 0, 0, 0), amount);

    private static Rgba Lighten(Rgba c, double amount) => Mix(c, new Rgba(255, 255, 255, 255), amount);

    private static string Hex(Rgba c) => c.A == 255
        ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
        : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string ContrastOn(Rgba accent)
    {
        var luminance = (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255.0;
        return luminance > 0.62 ? "#0A1020" : "#FFFFFF";
    }
}
