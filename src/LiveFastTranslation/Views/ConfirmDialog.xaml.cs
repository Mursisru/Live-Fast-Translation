using System.Windows;
using System.Windows.Input;

namespace LiveFastTranslation.Views;

/// <summary>
/// Modern modal confirmation used for destructive actions and the unsaved-changes guard.
/// Escape and Enter both take the safe action (cancel).
/// </summary>
public partial class ConfirmDialog : Window
{
    private ConfirmDialog(string title, string message, string confirmText)
    {
        InitializeComponent();
        Title = "Live-Fast Translation";
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        Loaded += (_, _) => CancelButton.Focus();
    }

    public static bool Show(Window owner, string title, string message, string confirmText)
    {
        var dialog = new ConfirmDialog(title, message, confirmText)
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        return dialog.ShowDialog() == true;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }

    private void Chrome_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The mouse button can be released before DragMove starts.
        }
    }
}
