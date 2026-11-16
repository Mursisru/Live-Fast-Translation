using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LiveFastTranslation.Interop;
using LiveFastTranslation.ViewModels;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;

namespace LiveFastTranslation.Views;

public partial class SettingsWindow : Window
{
    private const string MaximizeGlyph = "M 3.5 3.5 H 12.5 V 12.5 H 3.5 Z";
    private const string RestoreGlyph = "M 3.5 6 H 10 V 12.5 H 3.5 Z M 6 6 V 3.5 H 12.5 V 10 H 10";

    private bool _initialSectionShown;
    private bool _skipClosePrompt;
    private bool _savedInSession;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        viewModel.Saved += ViewModelOnSaved;
        viewModel.CloseRequested += ViewModelOnCloseRequested;
        viewModel.Confirm = (title, message, confirmText) =>
            ConfirmDialog.Show(this, title, message, confirmText);

        Closed += OnClosed;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    public SettingsViewModel ViewModel { get; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
    }

    // A WindowStyle=None window with WindowChrome maximises to the whole
    // monitor, so the taskbar covers the footer. WM_GETMINMAXINFO lets the
    // window fill the monitor work area on whichever display it is on.
    private static IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != NativeMethods.WM_GETMINMAXINFO)
        {
            return IntPtr.Zero;
        }

        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var monitorInfo = new NativeMethods.MONITORINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };
        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return IntPtr.Zero;
        }

        var dpi = (uint)NativeMethods.GetDpiForWindow(hwnd);
        var borderX = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXSIZEFRAME, dpi)
                      + NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXPADDEDBORDER, dpi);
        var borderY = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CYSIZEFRAME, dpi)
                      + NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXPADDEDBORDER, dpi);

        var info = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);
        info.ptMaxPosition.X = monitorInfo.rcWork.Left - monitorInfo.rcMonitor.Left - borderX;
        info.ptMaxPosition.Y = monitorInfo.rcWork.Top - monitorInfo.rcMonitor.Top - borderY;
        info.ptMaxSize.X = monitorInfo.rcWork.Right - monitorInfo.rcWork.Left + borderX * 2;
        info.ptMaxSize.Y = monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top + borderY * 2;
        Marshal.StructureToPtr(info, lParam, true);

        handled = true;
        return IntPtr.Zero;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _initialSectionShown = true;
        UpdateMaximizeGlyph();
        PlaySectionTransition();
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialSectionShown || !IsLoaded)
        {
            return;
        }

        // Sections share one ScrollViewer; without this a long section leaves the
        // next one scrolled past its heading.
        SettingsContentScroll.ScrollToTop();
        PlaySectionTransition();
    }

    private void PlaySectionTransition()
    {
        if (SectionHost is null)
        {
            return;
        }

        if (SectionHost.RenderTransform is not TranslateTransform translate)
        {
            translate = new TranslateTransform(0, 0);
            SectionHost.RenderTransform = translate;
        }

        SectionHost.BeginAnimation(UIElement.OpacityProperty, null);
        translate.BeginAnimation(TranslateTransform.YProperty, null);
        SectionHost.Opacity = 0;
        translate.Y = 8;

        var duration = TimeSpan.FromSeconds(0.18);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        SectionHost.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = ease },
            HandoffBehavior.SnapshotAndReplace);

        translate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, duration) { EasingFunction = ease },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void ViewModelOnSaved(object? sender, EventArgs e)
    {
        _skipClosePrompt = true;
        _savedInSession = true;
        Close();
    }

    private void ViewModelOnCloseRequested(object? sender, EventArgs e)
    {
        _skipClosePrompt = true;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximized();
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

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximized();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximized()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        UpdateMaximizeGlyph();
    }

    private void UpdateMaximizeGlyph()
    {
        if (MaximizeIcon is null)
        {
            return;
        }

        var maximized = WindowState == WindowState.Maximized;
        MaximizeIcon.Data = Geometry.Parse(maximized ? RestoreGlyph : MaximizeGlyph);

        if (MaximizeIcon.Parent is Button button)
        {
            button.ToolTip = maximized ? "Восстановить" : "Развернуть";
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (ViewModel.SaveCommand.CanExecute(null))
            {
                ViewModel.SaveCommand.Execute(null);
            }

            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape)
        {
            return;
        }

        // Let an open ComboBox drop-down consume Escape and close itself first.
        if (FindAncestor<ComboBox>(Keyboard.FocusedElement) is { IsDropDownOpen: true })
        {
            return;
        }

        if (SearchBox.IsKeyboardFocusWithin && ViewModel.HasSearchText)
        {
            ViewModel.SettingsSearchText = string.Empty;
            e.Handled = true;
            return;
        }

        Close();
        e.Handled = true;
    }

    private static T? FindAncestor<T>(IInputElement? element) where T : DependencyObject
    {
        var current = element as DependencyObject;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current switch
            {
                Visual visual => VisualTreeHelper.GetParent(visual),
                FrameworkContentElement content => content.Parent,
                _ => null
            };
        }

        return null;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_skipClosePrompt || !ViewModel.HasUnsavedChanges())
        {
            return;
        }

        var confirmed = ConfirmDialog.Show(
            this,
            "Закрыть без сохранения?",
            "Есть несохранённые изменения. Они будут потеряны, если закрыть окно сейчас.",
            "Закрыть без сохранения");

        if (!confirmed)
        {
            e.Cancel = true;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (!_savedInSession)
        {
            // The gallery previews themes live; discard it when nothing was saved.
            ViewModel.RevertThemePreview();
        }

        ViewModel.Saved -= ViewModelOnSaved;
        ViewModel.CloseRequested -= ViewModelOnCloseRequested;
        Loaded -= OnLoaded;
        Closed -= OnClosed;
        Closing -= OnClosing;
    }
}
