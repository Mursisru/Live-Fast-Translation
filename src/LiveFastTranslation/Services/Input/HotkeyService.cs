using System.Runtime.InteropServices;
using System.Windows.Interop;
using LiveFastTranslation.Interop;
using LiveFastTranslation.Models;
using LiveFastTranslation.Services.Infrastructure;

namespace LiveFastTranslation.Services.Input;

public sealed class HotkeyService : IDisposable
{
    public const int TranslateSelectionId = 1;
    public const int ToggleAiModeId = 2;
    public const int OpenSettingsId = 3;

    private readonly Dictionary<int, HotkeyGesture> _registered = new();
    private HwndSource? _source;
    private bool _disposed;

    public event EventHandler<int>? HotkeyPressed;

    public IReadOnlyList<string> LastErrors { get; private set; } = Array.Empty<string>();

    public IReadOnlyList<string> Apply(HotkeySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EnsureMessageWindow();
        UnregisterAll();

        var errors = new List<string>();
        TryRegister(TranslateSelectionId, "Перевод выделенного", settings.TranslateSelection, errors);
        TryRegister(ToggleAiModeId, "AI-режим", settings.ToggleAiMode, errors);
        TryRegister(OpenSettingsId, "Настройки", settings.OpenSettings, errors);
        LastErrors = errors;
        return errors;
    }

    public static IReadOnlyList<string> Validate(HotkeySettings settings)
    {
        var errors = new List<string>();
        var seen = new HashSet<HotkeyGesture>();

        void Check(string name, string value)
        {
            if (!HotkeyGesture.TryParse(value, out var gesture, out var error))
            {
                errors.Add($"{name}: {error}");
                return;
            }

            if (gesture is not null && !seen.Add(gesture))
            {
                errors.Add($"{name}: сочетание {gesture} уже используется другим действием.");
            }
        }

        Check("Перевод выделенного", settings.TranslateSelection);
        Check("AI-режим", settings.ToggleAiMode);
        Check("Настройки", settings.OpenSettings);
        return errors;
    }

    private void TryRegister(int id, string actionName, string text, List<string> errors)
    {
        if (!HotkeyGesture.TryParse(text, out var gesture, out var parseError) || gesture is null)
        {
            errors.Add($"{actionName}: {parseError}");
            return;
        }

        if (gesture.VirtualKey == 0)
        {
            errors.Add($"{actionName}: клавиша {text} не поддерживается.");
            return;
        }

        if (!NativeMethods.RegisterHotKey(_source!.Handle, id, gesture.Win32Modifiers, gesture.VirtualKey))
        {
            var error = Marshal.GetLastWin32Error();
            errors.Add($"{actionName}: не удалось зарегистрировать {gesture} (код {error}). Возможно, сочетание занято.");
            return;
        }

        _registered[id] = gesture;
    }

    private void EnsureMessageWindow()
    {
        if (_source is not null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("LiveFastTranslationHotkeys")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            ParentWindow = new IntPtr(-3) // HWND_MESSAGE
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (_registered.ContainsKey(id))
            {
                handled = true;
                HotkeyPressed?.Invoke(this, id);
            }
        }

        return IntPtr.Zero;
    }

    private void UnregisterAll()
    {
        if (_source is null)
        {
            return;
        }

        foreach (var id in _registered.Keys)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        }

        _registered.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }

        AppLogger.Info("Служба глобальных горячих клавиш остановлена.");
    }
}
