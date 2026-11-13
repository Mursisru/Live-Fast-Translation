using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LiveFastTranslation.Services.Input;
using UserControl = System.Windows.Controls.UserControl;

namespace LiveFastTranslation.Views.Controls;

/// <summary>Captures a key combination and exposes it as text such as Ctrl+Alt+T.</summary>
public partial class HotkeyBox : UserControl
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey),
        typeof(string),
        typeof(HotkeyBox),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnHotkeyChanged));

    public static readonly DependencyProperty ValidationMessageProperty = DependencyProperty.Register(
        nameof(ValidationMessage),
        typeof(string),
        typeof(HotkeyBox),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty InputAutomationIdProperty = DependencyProperty.Register(
        nameof(InputAutomationId),
        typeof(string),
        typeof(HotkeyBox),
        new PropertyMetadata(string.Empty));

    public HotkeyBox()
    {
        InitializeComponent();
    }

    public string Hotkey
    {
        get => (string)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    public string ValidationMessage
    {
        get => (string)GetValue(ValidationMessageProperty);
        private set => SetValue(ValidationMessageProperty, value);
    }

    /// <summary>AutomationId exposed on the inner text box so UI tests can focus the capture field.</summary>
    public string InputAutomationId
    {
        get => (string)GetValue(InputAutomationIdProperty);
        set => SetValue(InputAutomationIdProperty, value);
    }

    private static void OnHotkeyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is HotkeyBox box && !string.IsNullOrWhiteSpace(box.Hotkey))
        {
            box.ValidationMessage = string.Empty;
        }
    }

    private void InputBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (IsModifierKey(key))
        {
            return;
        }

        if (key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            Hotkey = string.Empty;
            ValidationMessage = string.Empty;
            return;
        }

        if (key == Key.Escape)
        {
            ValidationMessage = string.Empty;
            Keyboard.ClearFocus();
            return;
        }

        if (key == Key.Tab)
        {
            e.Handled = false;
            return;
        }

        var gesture = HotkeyGesture.FromKey(Keyboard.Modifiers, key);
        var text = gesture.ToString();
        if (!HotkeyGesture.TryParse(text, out _, out var error))
        {
            ValidationMessage = error ?? "Недопустимое сочетание клавиш.";
            return;
        }

        Hotkey = text;
        ValidationMessage = string.Empty;
    }

    private void InputBox_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        InputBox.SelectAll();
    }

    private void InputBox_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        InputBox.Select(0, 0);
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin
            or Key.None;
    }
}
