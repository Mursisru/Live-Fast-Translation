using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Threading;
using LiveFastTranslation.Interop;
using LiveFastTranslation.Models;
using LiveFastTranslation.Services.Infrastructure;
using LiveFastTranslation.Services.TextReplace;

namespace LiveFastTranslation.Services.Input;

public sealed class SentenceDetectedEventArgs : EventArgs
{
    public SentenceDetectedEventArgs(string sentence, IntPtr targetWindow)
    {
        Sentence = sentence;
        TargetWindow = targetWindow;
    }

    public string Sentence { get; }

    public IntPtr TargetWindow { get; }
}

/// <summary>
/// Local AI-mode input monitor. It never sends keystrokes anywhere; it only builds an in-memory
/// sentence buffer, skips password fields, and raises an event after a sentence is completed.
/// </summary>
public sealed class TypingMonitor : IDisposable
{
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private const int VK_BACK = 0x08;
    private const int VK_TAB = 0x09;
    private const int VK_RETURN = 0x0D;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_SPACE = 0x20;
    private const int VK_PRIOR = 0x21;
    private const int VK_NEXT = 0x22;
    private const int VK_END = 0x23;
    private const int VK_HOME = 0x24;
    private const int VK_LEFT = 0x25;
    private const int VK_UP = 0x26;
    private const int VK_RIGHT = 0x27;
    private const int VK_DOWN = 0x28;
    private const int VK_DELETE = 0x2E;

    private readonly SentenceCompletionDetector _detector = new();
    private NativeMethods.LowLevelKeyboardProc? _keyboardProc;
    private NativeMethods.WinEventProc? _focusProc;
    private IntPtr _keyboardHook;
    private IntPtr _focusHook;
    private DispatcherTimer? _pendingTimer;
    private string? _pendingSentence;
    private IntPtr _pendingTargetWindow;
    private AiModeSettings _settings = new();
    private bool _enabled;
    private bool _sensitiveContext;
    private bool _ownAppContext;
    private bool _disposed;

    public event EventHandler<SentenceDetectedEventArgs>? SentenceDetected;

    public Func<bool>? ShouldPauseInput { get; set; }

    public string? LastError { get; private set; }

    public bool IsEnabled => _enabled;

    public void ApplySettings(AiModeSettings settings)
    {
        _settings = new AiModeSettings
        {
            Enabled = settings.Enabled,
            DelayMs = settings.DelayMs,
            MinSentenceLength = settings.MinSentenceLength,
            MaxBufferLength = settings.MaxBufferLength
        };
        _detector.MinSentenceLength = _settings.MinSentenceLength;
        _detector.MaxBufferLength = _settings.MaxBufferLength;

        if (_settings.Enabled && !_enabled)
        {
            Install();
        }
        else if (!_settings.Enabled && _enabled)
        {
            Uninstall();
        }
    }

    private void Install()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            _enabled = true;
            return;
        }

        _keyboardProc = KeyboardHookCallback;
        _keyboardHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _keyboardProc,
            NativeMethods.GetModuleHandle(null),
            0);

        _focusProc = FocusHookCallback;
        _focusHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_FOCUS,
            NativeMethods.EVENT_OBJECT_FOCUS,
            IntPtr.Zero,
            _focusProc,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT);

        if (_keyboardHook == IntPtr.Zero || _focusHook == IntPtr.Zero)
        {
            LastError = "Не удалось установить системный монитор ввода. AI-режим недоступен.";
            AppLogger.Error($"TypingMonitor hook install failed: keyboard={_keyboardHook}, focus={_focusHook}.");
            Uninstall();
            return;
        }

        _enabled = true;
        LastError = null;
        _detector.Reset();
        UpdateFocusContext();
        AppLogger.Info("AI-режим: локальный монитор ввода запущен.");
    }

    private void Uninstall()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_focusHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_focusHook);
            _focusHook = IntPtr.Zero;
        }

        _keyboardProc = null;
        _focusProc = null;
        _enabled = false;
        CancelPending();
        _detector.Reset();
        AppLogger.Info("AI-режим: локальный монитор ввода остановлен.");
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < NativeMethods.HC_ACTION || !_enabled)
        {
            return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        try
        {
            var message = wParam.ToInt32();
            if (message is not (WM_KEYDOWN or WM_SYSKEYDOWN))
            {
                return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
            }

            var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            if ((info.flags & NativeMethods.LLKHF_INJECTED) != 0)
            {
                return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
            }

            if (_sensitiveContext || _ownAppContext || ShouldPauseInput?.Invoke() == true)
            {
                _detector.Reset();
                CancelPending();
                return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
            }

            var virtualKey = (int)info.vkCode;
            switch (virtualKey)
            {
                case VK_BACK:
                    _detector.Backspace();
                    return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
                case VK_RETURN:
                {
                    var completed = _detector.ProcessEnter();
                    if (completed is not null)
                    {
                        Schedule(completed, NativeMethods.GetForegroundWindow());
                    }

                    return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
                }

                case VK_SPACE:
                {
                    var completed = _detector.Append(' ');
                    if (completed is not null)
                    {
                        Schedule(completed, NativeMethods.GetForegroundWindow());
                    }

                    return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
                }

                case VK_TAB:
                    return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
                case VK_ESCAPE:
                case VK_LEFT:
                case VK_RIGHT:
                case VK_UP:
                case VK_DOWN:
                case VK_HOME:
                case VK_END:
                case VK_DELETE:
                case VK_PRIOR:
                case VK_NEXT:
                    _detector.Reset();
                    CancelPending();
                    return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
            }

            if (IsShortcutModifierPressed())
            {
                return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
            }

            var typed = ConvertToCharacter((uint)virtualKey, info.scanCode);
            if (typed.Length == 0 || typed.All(char.IsControl))
            {
                return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
            }

            foreach (var character in typed)
            {
                var completed = _detector.Append(character);
                if (completed is not null)
                {
                    Schedule(completed, NativeMethods.GetForegroundWindow());
                    break;
                }
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"TypingMonitor: {exception.Message}");
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private void FocusHookCallback(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hWnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (eventType != NativeMethods.EVENT_OBJECT_FOCUS || !_enabled)
        {
            return;
        }

        _detector.Reset();
        CancelPending();
        UpdateFocusContext();
    }

    private void UpdateFocusContext()
    {
        _sensitiveContext = false;
        _ownAppContext = false;

        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        _ownAppContext = processId == (uint)Environment.ProcessId;

        var focusedControl = Win32Focus.GetFocusedControlHwnd();
        if (focusedControl != IntPtr.Zero && Win32Focus.IsPasswordField(focusedControl))
        {
            _sensitiveContext = true;
            return;
        }

        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is not null && element.Current.IsPassword)
            {
                _sensitiveContext = true;
            }
        }
        catch (ElementNotAvailableException)
        {
            // Some legacy apps do not expose UI Automation; the Win32 check above is still applied.
        }
        catch (InvalidOperationException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void Schedule(string sentence, IntPtr targetWindow)
    {
        if (!_enabled || _sensitiveContext || _ownAppContext)
        {
            return;
        }

        var trimmed = sentence.Trim();
        if (trimmed.Length < Math.Max(1, _settings.MinSentenceLength))
        {
            return;
        }

        _pendingTimer?.Stop();
        _pendingSentence = trimmed;
        _pendingTargetWindow = targetWindow;
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.DelayMs, 150, 5000))
        };
        timer.Tick += PendingTimerOnTick;
        _pendingTimer = timer;
        timer.Start();
    }

    private void PendingTimerOnTick(object? sender, EventArgs e)
    {
        _pendingTimer?.Stop();
        if (_pendingTimer is not null)
        {
            _pendingTimer.Tick -= PendingTimerOnTick;
        }

        _pendingTimer = null;
        var sentence = _pendingSentence;
        var target = _pendingTargetWindow;
        _pendingSentence = null;
        _pendingTargetWindow = IntPtr.Zero;

        if (!_enabled || sentence is null || _sensitiveContext || _ownAppContext)
        {
            return;
        }

        SentenceDetected?.Invoke(this, new SentenceDetectedEventArgs(sentence, target));
    }

    private void CancelPending()
    {
        if (_pendingTimer is not null)
        {
            _pendingTimer.Stop();
            _pendingTimer.Tick -= PendingTimerOnTick;
            _pendingTimer = null;
        }

        _pendingSentence = null;
        _pendingTargetWindow = IntPtr.Zero;
    }

    private static bool IsShortcutModifierPressed()
    {
        var control = IsPressed(NativeMethods.VK_CONTROL);
        var alt = IsPressed(NativeMethods.VK_MENU);
        var win = IsPressed(NativeMethods.VK_LWIN) || IsPressed(NativeMethods.VK_RWIN);
        var altGr = control && alt && IsPressed(NativeMethods.VK_RMENU);

        if (win || control && !altGr || alt && !altGr)
        {
            return true;
        }

        return false;
    }

    private static bool IsPressed(int virtualKey)
    {
        return (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static string ConvertToCharacter(uint virtualKey, uint scanCode)
    {
        var state = new byte[256];
        if (IsPressed(NativeMethods.VK_SHIFT))
        {
            state[NativeMethods.VK_SHIFT] = 0x80;
        }

        if (IsPressed(NativeMethods.VK_CONTROL))
        {
            state[NativeMethods.VK_CONTROL] = 0x80;
        }

        if (IsPressed(NativeMethods.VK_MENU))
        {
            state[NativeMethods.VK_MENU] = 0x80;
        }

        if ((NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 1) != 0)
        {
            state[NativeMethods.VK_CAPITAL] = 0x01;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        var threadId = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var layout = NativeMethods.GetKeyboardLayout(threadId);
        var buffer = new char[8];
        var count = NativeMethods.ToUnicodeEx(virtualKey, scanCode, state, buffer, buffer.Length, 0, layout);
        return count > 0 ? new string(buffer, 0, count) : string.Empty;
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
