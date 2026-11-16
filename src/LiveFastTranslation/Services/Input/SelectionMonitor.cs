using System.Runtime.InteropServices;
using System.Windows.Threading;
using LiveFastTranslation.Interop;
using LiveFastTranslation.Services.Infrastructure;

namespace LiveFastTranslation.Services.Input;

/// <summary>
/// Watches for completed text selections outside this application. A short debounce timer
/// lets the target application settle its selection before the overlay reads it through
/// UI Automation; the clipboard is never touched, so ordinary clicks stay cheap.
/// </summary>
public sealed class SelectionMonitor : IDisposable
{
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_CONTROL = 0x11;
    private const int VK_LEFT = 0x25;
    private const int VK_UP = 0x26;
    private const int VK_RIGHT = 0x27;
    private const int VK_DOWN = 0x28;
    private const int VK_HOME = 0x24;
    private const int VK_END = 0x23;
    private const int VK_PRIOR = 0x21;
    private const int VK_NEXT = 0x22;
    private const int VK_BACK = 0x08;
    private const int VK_DELETE = 0x2E;
    private const int VK_X = 0x58;
    private const int VK_A = 0x41;

    private readonly DispatcherTimer _settleTimer;
    private NativeMethods.WinEventProc? _winEventProc;
    private NativeMethods.LowLevelKeyboardProc? _mouseProc;
    private NativeMethods.LowLevelKeyboardProc? _keyboardProc;
    private IntPtr _winEventHook;
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private bool _enabled;
    private bool _disposed;

    public SelectionMonitor()
    {
        _settleTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(320)
        };
        _settleTimer.Tick += (_, _) =>
        {
            _settleTimer.Stop();
            SelectionSettled?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>Raised on the UI thread when a selection has probably finished changing.</summary>
    public event EventHandler? SelectionSettled;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            if (value)
            {
                Install();
            }
            else
            {
                Uninstall();
            }
        }
    }

    private void Install()
    {
        if (_enabled || _disposed)
        {
            return;
        }

        _winEventProc = OnWinEvent;
        _mouseProc = OnMouseHook;
        _keyboardProc = OnKeyboardHook;

        _winEventHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_TEXTSELECTIONCHANGED,
            NativeMethods.EVENT_OBJECT_TEXTSELECTIONCHANGED,
            IntPtr.Zero,
            _winEventProc,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        var module = NativeMethods.GetModuleHandle(null);
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, module, 0);
        _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, module, 0);

        if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero)
        {
            AppLogger.Warn(
                $"SelectionMonitor hook install failed: mouse={_mouseHook}, keyboard={_keyboardHook}.");
            Uninstall();
            return;
        }

        if (_winEventHook == IntPtr.Zero)
        {
            // Selection events are an extra hint; mouse and keyboard triggers still work.
            AppLogger.Warn("SelectionMonitor: WinEvent hook for text selection is unavailable.");
        }

        _enabled = true;
        AppLogger.Info("Авто появление строки: монитор выделения запущен.");
    }

    private void Uninstall()
    {
        _settleTimer.Stop();

        if (_winEventHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        _winEventProc = null;
        _mouseProc = null;
        _keyboardProc = null;

        if (_enabled)
        {
            AppLogger.Info("Авто появление строки: монитор выделения остановлен.");
        }

        _enabled = false;
    }

    private void OnWinEvent(
        IntPtr hookHandle,
        uint eventType,
        IntPtr hWnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime)
    {
        Schedule();
    }

    private IntPtr OnMouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= NativeMethods.HC_ACTION && wParam.ToInt32() == NativeMethods.WM_LBUTTONUP)
        {
            Schedule();
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr OnKeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= NativeMethods.HC_ACTION)
        {
            var message = wParam.ToInt32();
            if (message is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                var key = (int)info.vkCode;
                var control = (NativeMethods.GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;

                if ((control && (key == VK_A || key == VK_X))
                    || IsSelectionNavigationKey(key)
                    || key is VK_BACK or VK_DELETE)
                {
                    Schedule();
                }
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private static bool IsSelectionNavigationKey(int key)
    {
        return key is VK_LEFT or VK_UP or VK_RIGHT or VK_DOWN or VK_HOME or VK_END or VK_PRIOR or VK_NEXT;
    }

    private void Schedule()
    {
        if (!_enabled || _disposed)
        {
            return;
        }

        _settleTimer.Stop();
        _settleTimer.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Uninstall();
    }
}
