using System.Windows.Input;
using LiveFastTranslation.Interop;

namespace LiveFastTranslation.Services.Input;

/// <summary>Parsed global hotkey such as Ctrl+Alt+T.</summary>
public sealed class HotkeyGesture : IEquatable<HotkeyGesture>
{
    private static readonly Dictionary<string, Key> KeyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Esc"] = Key.Escape,
        ["Escape"] = Key.Escape,
        ["Enter"] = Key.Return,
        ["Return"] = Key.Return,
        ["Space"] = Key.Space,
        ["Plus"] = Key.OemPlus,
        ["Minus"] = Key.OemMinus,
        ["Comma"] = Key.OemComma,
        ["Period"] = Key.OemPeriod,
        ["Slash"] = Key.OemQuestion,
        ["Backtick"] = Key.OemTilde,
        ["Semicolon"] = Key.OemSemicolon,
        ["Quote"] = Key.OemQuotes,
        ["Insert"] = Key.Insert,
        ["Delete"] = Key.Delete,
        ["Home"] = Key.Home,
        ["End"] = Key.End,
        ["PageUp"] = Key.PageUp,
        ["PageDown"] = Key.PageDown,
        ["Up"] = Key.Up,
        ["Down"] = Key.Down,
        ["Left"] = Key.Left,
        ["Right"] = Key.Right,
        ["Tab"] = Key.Tab,
        ["Backspace"] = Key.Back,
        ["PrintScreen"] = Key.PrintScreen,
        ["Pause"] = Key.Pause
    };

    private static readonly Dictionary<Key, string> DisplayNames = new()
    {
        [Key.Return] = "Enter",
        [Key.Escape] = "Escape",
        [Key.Space] = "Space",
        [Key.OemPlus] = "Plus",
        [Key.OemMinus] = "Minus",
        [Key.OemComma] = "Comma",
        [Key.OemPeriod] = "Period",
        [Key.OemQuestion] = "Slash",
        [Key.OemTilde] = "Backtick",
        [Key.OemSemicolon] = "Semicolon",
        [Key.OemQuotes] = "Quote",
        [Key.Back] = "Backspace",
        [Key.PageUp] = "PageUp",
        [Key.PageDown] = "PageDown"
    };

    private HotkeyGesture(ModifierKeys modifiers, Key key)
    {
        Modifiers = modifiers;
        Key = key;
    }

    public ModifierKeys Modifiers { get; }

    public Key Key { get; }

    public uint Win32Modifiers
    {
        get
        {
            uint result = NativeMethods.MOD_NOREPEAT;
            if (Modifiers.HasFlag(ModifierKeys.Control))
            {
                result |= NativeMethods.MOD_CONTROL;
            }

            if (Modifiers.HasFlag(ModifierKeys.Alt))
            {
                result |= NativeMethods.MOD_ALT;
            }

            if (Modifiers.HasFlag(ModifierKeys.Shift))
            {
                result |= NativeMethods.MOD_SHIFT;
            }

            if (Modifiers.HasFlag(ModifierKeys.Windows))
            {
                result |= NativeMethods.MOD_WIN;
            }

            return result;
        }
    }

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public static bool TryParse(string? text, out HotkeyGesture? gesture, out string? error)
    {
        gesture = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Сочетание клавиш не задано.";
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = "Сочетание клавиш не задано.";
            return false;
        }

        var modifiers = ModifierKeys.None;
        Key? key = null;

        foreach (var rawPart in parts)
        {
            var part = rawPart.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                case "ctl":
                    modifiers |= ModifierKeys.Control;
                    continue;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    continue;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    continue;
                case "win":
                case "windows":
                case "super":
                case "meta":
                    modifiers |= ModifierKeys.Windows;
                    continue;
            }

            if (key is not null)
            {
                error = "Указано больше одной основной клавиши.";
                return false;
            }

            key = ParseKey(part);
            if (key is null)
            {
                error = $"Неизвестная клавиша «{part}».";
                return false;
            }
        }

        if (key is null)
        {
            error = "Нужно указать основную клавишу, например T.";
            return false;
        }

        var isFunctionKey = key is >= Key.F1 and <= Key.F24;
        var hasPrimaryModifier = (modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows))
                                 != ModifierKeys.None;
        if (!hasPrimaryModifier && !isFunctionKey)
        {
            error = "Нужен хотя бы один модификатор: Ctrl, Alt или Win (Shift сам по себе не подходит).";
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key.Value);
        return true;
    }

    public static HotkeyGesture FromKey(ModifierKeys modifiers, Key key)
    {
        return new HotkeyGesture(modifiers, key == Key.System ? Key.None : key);
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(GetKeyName(Key));
        return string.Join("+", parts);
    }

    public static string GetKeyName(Key key)
    {
        if (DisplayNames.TryGetValue(key, out var display))
        {
            return display;
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((int)key - (int)Key.D0).ToString();
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return "Num" + ((int)key - (int)Key.NumPad0);
        }

        return key.ToString();
    }

    private static Key? ParseKey(string text)
    {
        if (KeyAliases.TryGetValue(text, out var aliased))
        {
            return aliased;
        }

        if (text.Length == 1 && char.IsDigit(text[0]))
        {
            return Key.D0 + (text[0] - '0');
        }

        if (text.StartsWith("Num", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(text[3..], out var numPad) && numPad is >= 0 and <= 9)
        {
            return Key.NumPad0 + numPad;
        }

        return Enum.TryParse<Key>(text, ignoreCase: true, out var key) ? key : null;
    }

    public bool Equals(HotkeyGesture? other)
    {
        return other is not null && Modifiers == other.Modifiers && Key == other.Key;
    }

    public override bool Equals(object? obj) => obj is HotkeyGesture other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Modifiers, Key);
}
