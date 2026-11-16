using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LiveFastTranslation.Interop;
using LiveFastTranslation.ViewModels;
using Forms = System.Windows.Forms;

namespace LiveFastTranslation.Views;

/// <summary>
/// The translucent translation capsule. It never activates, so the target application keeps
/// its selection and keyboard focus while the user reads or clicks the translation.
/// </summary>
public partial class OverlayWindow : Window
{
    private const double ShadowMargin = 10;
    private const double MinimumCapsuleWidth = 240;
    private const double MinimumMinimalCapsuleWidth = 80;
    private const double AnchorGap = 6;
    private const double ControlChromeWidth = 250;
    private const double MinimalChromeWidth = 30;

    private readonly DispatcherTimer _autoHideTimer;
    private HwndSource? _source;
    private System.Drawing.Rectangle? _anchorScreen;
    private bool _mouseOver;
    private bool _animationsEnabled = true;
    private bool _hiding;
    private int _hideVersion;

    public OverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModelOnPropertyChanged;

        _autoHideTimer = new DispatcherTimer(DispatcherPriority.Background);
        _autoHideTimer.Tick += AutoHideTimerOnTick;

        SourceInitialized += OnSourceInitialized;
        SizeChanged += OnSizeChanged;
        Closed += OnClosed;
    }

    public OverlayViewModel ViewModel { get; }

    public event EventHandler? HiddenByUser;

    public bool IsOverlayVisible => IsVisible && !_hiding;

    public bool AnimationsEnabled
    {
        get => _animationsEnabled;
        set => _animationsEnabled = value;
    }

    /// <summary>
    /// Anchors the capsule to the focused input element. The rectangle is in physical
    /// screen pixels, exactly as UI Automation reports it.
    /// </summary>
    public void SetAnchor(Rect? anchor)
    {
        if (anchor is { IsEmpty: false, Width: > 0, Height: > 0 } rect)
        {
            _anchorScreen = new System.Drawing.Rectangle(
                (int)Math.Round(rect.Left),
                (int)Math.Round(rect.Top),
                Math.Max(1, (int)Math.Round(rect.Width)),
                Math.Max(1, (int)Math.Round(rect.Height)));
        }
        else
        {
            _anchorScreen = null;
        }

        if (IsVisible)
        {
            UpdateLayout();
            PlaceAtAnchor();
        }
    }

    /// <summary>
    /// Sizes the capsule to the measured text: long lines widen it up to the 65% screen
    /// limit, then wrap; the window height is taken from a real WPF measurement of the
    /// capsule, so the last wrapped line is never clipped by a rounding mismatch.
    /// </summary>
    public void FitToText(string? text)
    {
        var vm = ViewModel;
        var chrome = vm.ShowOverlayControls ? ControlChromeWidth : MinimalChromeWidth;
        var maxTextWidth = Math.Max(150, GetMaxWindowWidth() - ShadowMargin * 2 - chrome);
        var dpi = VisualTreeHelper.GetDpi(this);
        var typeface = new Typeface(vm.OverlayFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        var formatted = new FormattedText(
            text ?? string.Empty,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            vm.OverlayFontSize,
            System.Windows.Media.Brushes.Black,
            dpi.PixelsPerDip)
        {
            MaxTextWidth = maxTextWidth,
            MaxTextHeight = double.PositiveInfinity
        };

        var measured = Math.Min(formatted.WidthIncludingTrailingWhitespace, maxTextWidth);
        SetWindowSize(measured + chrome + ShadowMargin * 2);
    }

    /// <summary>Re-measures whatever the capsule currently shows after a theme/font change.</summary>
    public void RefitToContent()
    {
        if (ViewModel.ShowError)
        {
            FitToText(ViewModel.ErrorMessage);
        }
        else if (ViewModel.ShowNotice)
        {
            FitToText(ViewModel.NoticeMessage);
        }
        else if (ViewModel.ShowResult)
        {
            FitToText(ViewModel.Translation);
        }
        else if (ViewModel.IsLoading)
        {
            FitToText("Перевод…");
        }
    }

    private void SetWindowSize(double width)
    {
        var maxWindowWidth = GetMaxWindowWidth();
        var minimumCapsuleWidth = ViewModel.ShowOverlayControls
            ? MinimumCapsuleWidth
            : MinimumMinimalCapsuleWidth;
        var minWindowWidth = minimumCapsuleWidth + ShadowMargin * 2;
        Width = Math.Clamp(width, minWindowWidth, Math.Max(minWindowWidth, maxWindowWidth));

        // The window height is owned by WPF (SizeToContent=Height), so wrapped text
        // always gets exactly the room it needs. OnSizeChanged keeps the capsule
        // anchored while that height grows or shrinks.
        if (IsVisible)
        {
            UpdateLayout();
            PlaceAtAnchor();
            EnsureOnScreen();
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsVisible || !e.HeightChanged)
        {
            return;
        }

        PlaceAtAnchor();
        EnsureOnScreen();
    }

    public void ShowOverlay()
    {
        _hiding = false;
        _hideVersion++;
        _autoHideTimer.Stop();
        BeginAnimation(OpacityProperty, null);
        CapsuleOffset.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        CapsuleOffset.Y = _animationsEnabled ? 8 : 0;
        Opacity = _animationsEnabled ? 0 : ViewModel.WindowOpacity;

        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        PlaceAtAnchor();

        if (_animationsEnabled)
        {
            var fade = new DoubleAnimation(0, ViewModel.WindowOpacity, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);

            var move = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            CapsuleOffset.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, move);
        }
        else
        {
            Opacity = ViewModel.WindowOpacity;
            CapsuleOffset.Y = 0;
        }

        RestartAutoHide();
    }

    public void HideOverlay(bool animate = true)
    {
        _hiding = true;
        _autoHideTimer.Stop();
        if (!IsVisible)
        {
            return;
        }

        var version = ++_hideVersion;
        if (!animate || !_animationsEnabled)
        {
            BeginAnimation(OpacityProperty, null);
            Hide();
            return;
        }

        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) =>
        {
            if (version == _hideVersion && Opacity < 0.03)
            {
                Hide();
            }
        };
        BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
    }

    public void EnsureOnScreen()
    {
        if (!IsVisible)
        {
            return;
        }

        UpdateLayout();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return;
        }

        var center = new System.Drawing.Point((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
        var work = Forms.Screen.FromPoint(center).WorkingArea;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var x = Math.Clamp(rect.Left, work.Left + 6, Math.Max(work.Left + 6, work.Right - width - 6));
        var y = Math.Clamp(rect.Top, work.Top + 6, Math.Max(work.Top + 6, work.Bottom - height - 6));

        if (x != rect.Left || y != rect.Top)
        {
            NativeMethods.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                x,
                y,
                0,
                0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }
    }

    private double GetMaxWindowWidth()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            var dpi = NativeMethods.GetDpiForWindow(hwnd);
            var scale = dpi > 0 ? dpi / 96.0 : 1.0;
            var anchor = _anchorScreen;
            var point = anchor is { Width: > 0, Height: > 0 } rect
                ? new System.Drawing.Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2)
                : Forms.Cursor.Position;
            var work = Forms.Screen.FromPoint(point).WorkingArea;
            return work.Width * 0.65 / scale;
        }
        catch (InvalidOperationException)
        {
            return 900;
        }
    }

    private void PlaceAtAnchor()
    {
        if (!IsVisible)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        int x;
        int y;
        Forms.Screen screen;

        if (_anchorScreen is { Width: > 0, Height: > 0 } anchor)
        {
            var center = new System.Drawing.Point(anchor.Left + anchor.Width / 2, anchor.Top + anchor.Height / 2);
            screen = Forms.Screen.FromPoint(center);
            var work = screen.WorkingArea;

            // The 10 px transparent shadow margin is inside the window rectangle, so the
            // visible capsule sits exactly AnchorGap pixels above the input field.
            // Align the visible capsule (inside the transparent shadow margin) to the field.
            x = anchor.Left - (int)ShadowMargin;
            var above = anchor.Top - height + (int)ShadowMargin - (int)AnchorGap;
            var below = anchor.Bottom - (int)ShadowMargin + (int)AnchorGap;
            y = above >= work.Top + 6 ? above : below;

            if (y + height > work.Bottom - 6 && above >= work.Top + 6)
            {
                y = above;
            }
        }
        else
        {
            var cursor = Forms.Cursor.Position;
            screen = Forms.Screen.FromPoint(cursor);
            var work = screen.WorkingArea;

            x = cursor.X + 14;
            var above = cursor.Y - height - 14;
            var below = cursor.Y + 22;
            y = above >= work.Top + 6 ? above : below;

            if (x + width > work.Right - 8)
            {
                x = cursor.X - width - 14;
            }

            if (y + height > work.Bottom - 8)
            {
                y = above >= work.Top + 6 ? above : cursor.Y - height - 14;
            }
        }

        var workArea = screen.WorkingArea;
        x = Math.Clamp(x, workArea.Left + 6, Math.Max(workArea.Left + 6, workArea.Right - width - 6));
        y = Math.Clamp(y, workArea.Top + 6, Math.Max(workArea.Top + 6, workArea.Bottom - height - 6));

        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            x,
            y,
            0,
            0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    private void RestartAutoHide()
    {
        _autoHideTimer.Stop();
        if (!IsVisible || _mouseOver || ViewModel.IsLoading)
        {
            return;
        }

        var seconds = ViewModel.ShowNotice ? 2.5 : ViewModel.ShowError ? 9.0 : 18.0;
        _autoHideTimer.Interval = TimeSpan.FromSeconds(seconds);
        _autoHideTimer.Start();
    }

    private void OnVmStateChanged()
    {
        if (ViewModel.IsLoading)
        {
            _autoHideTimer.Stop();
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                RefitToContent();
                UpdateLayout();
                PlaceAtAnchor();
                RestartAutoHide();
            }));
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(OverlayViewModel.ShowResult):
            case nameof(OverlayViewModel.ShowError):
            case nameof(OverlayViewModel.ShowNotice):
            case nameof(OverlayViewModel.IsLoading):
                OnVmStateChanged();
                break;
            case nameof(OverlayViewModel.WindowOpacity) when IsOverlayVisible:
                BeginAnimation(OpacityProperty, null);
                Opacity = ViewModel.WindowOpacity;
                break;
        }
    }

    private void AutoHideTimerOnTick(object? sender, EventArgs e)
    {
        _autoHideTimer.Stop();
        if (!_mouseOver && !ViewModel.IsLoading)
        {
            HideOverlay();
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var extendedStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        extendedStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(extendedStyle));

        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WindowProc);
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(NativeMethods.MA_NOACTIVATE);
        }

        return IntPtr.Zero;
    }

    private void Result_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && ViewModel.CanActivateResult)
        {
            e.Handled = true;
            ViewModel.ActivateResultCommand.Execute(null);
        }
    }

    private void Error_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            e.Handled = true;
            ViewModel.RetryCommand.Execute(null);
        }
    }

    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        HideByUser();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideByUser();
        }
    }

    private void HideByUser()
    {
        HideOverlay();
        HiddenByUser?.Invoke(this, EventArgs.Empty);
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _mouseOver = true;
        _autoHideTimer.Stop();
    }

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _mouseOver = false;
        RestartAutoHide();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _autoHideTimer.Stop();
        _autoHideTimer.Tick -= AutoHideTimerOnTick;
        ViewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        if (_source is not null)
        {
            _source.RemoveHook(WindowProc);
            _source = null;
        }
    }
}
