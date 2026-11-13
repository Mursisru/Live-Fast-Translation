using System.Windows.Input;
using LiveFastTranslation.Services.Input;
using Xunit;

namespace LiveFastTranslation.Tests.Input;

public sealed class HotkeyGestureTests
{
    [Fact]
    public void TryParse_ParsesModifiersAndKey()
    {
        var success = HotkeyGesture.TryParse("Ctrl+Alt+T", out var gesture, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(gesture);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Alt, gesture.Modifiers);
        Assert.Equal(Key.T, gesture.Key);
        Assert.Equal("Ctrl+Alt+T", gesture.ToString());
    }

    [Theory]
    [InlineData("ctrl+shift+f5", "Ctrl+Shift+F5")]
    [InlineData("win+space", "Win+Space")]
    [InlineData("CTRL + ALT + DELETE", "Ctrl+Alt+Delete")]
    [InlineData("Alt+Num3", "Alt+Num3")]
    public void TryParse_NormalizesAliasesAndCase(string input, string expected)
    {
        Assert.True(HotkeyGesture.TryParse(input, out var gesture, out _));
        Assert.NotNull(gesture);
        Assert.Equal(expected, gesture.ToString());
    }

    [Theory]
    [InlineData("F5", true)]
    [InlineData("Shift+F5", true)]
    [InlineData("Ctrl+F5", true)]
    [InlineData("Shift+Alt+PageDown", true)]
    [InlineData("T", false)]
    [InlineData("Shift+T", false)]
    public void TryParse_RequiresModifierForNonFunctionKeys(string input, bool expected)
    {
        Assert.Equal(expected, HotkeyGesture.TryParse(input, out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+T+Y")]
    [InlineData("Ctrl+UnknownKey")]
    [InlineData(null)]
    public void TryParse_RejectsInvalidGestures(string? input)
    {
        var success = HotkeyGesture.TryParse(input, out var gesture, out var error);

        Assert.False(success);
        Assert.Null(gesture);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void FromKey_RoundTripsThroughToStringAndParse()
    {
        var gesture = HotkeyGesture.FromKey(ModifierKeys.Control | ModifierKeys.Shift, Key.D1);
        var text = gesture.ToString();

        Assert.Equal("Ctrl+Shift+1", text);
        Assert.True(HotkeyGesture.TryParse(text, out var parsed, out _));
        Assert.Equal(gesture, parsed);
        Assert.Equal(gesture.GetHashCode(), parsed!.GetHashCode());
    }

    [Fact]
    public void FromKey_MapsSystemKeyToNoneMarker()
    {
        var gesture = HotkeyGesture.FromKey(ModifierKeys.Alt, Key.System);

        Assert.Equal(Key.None, gesture.Key);
    }

    [Fact]
    public void Win32Modifiers_IncludeNoRepeat()
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+Alt+A", out var gesture, out _));

        Assert.NotNull(gesture);
        Assert.Equal(0x4003u, gesture.Win32Modifiers);
    }
}
