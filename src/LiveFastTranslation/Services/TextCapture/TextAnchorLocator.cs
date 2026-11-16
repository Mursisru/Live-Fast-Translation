using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using LiveFastTranslation.Interop;
using LiveFastTranslation.Services.TextReplace;

namespace LiveFastTranslation.Services.TextCapture;

/// <summary>
/// Finds the screen rectangle of the focused input field so the overlay can sit directly
/// above it. UI Automation bounds are preferred; native edit controls, the caret and the
/// target window are progressively weaker fallbacks.
/// </summary>
internal static class TextAnchorLocator
{
    public static Rect? TryGetAnchor(IntPtr targetWindow)
    {
        if (TryGetFocusedControlRect(out var rect)
            || TryGetCaretRect(targetWindow, out rect)
            || TryGetWindowRect(targetWindow, out rect))
        {
            return rect;
        }

        return null;
    }

    public static Rect? TryGetElementBounds(AutomationElement? element)
    {
        if (element is null)
        {
            return null;
        }

        try
        {
            var bounds = element.Current.BoundingRectangle;
            return IsUsable(bounds) ? bounds : null;
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool TryGetFocusedControlRect(out Rect rect)
    {
        rect = Rect.Empty;
        var focused = Win32Focus.GetFocusedControlHwnd();
        if (focused == IntPtr.Zero || !NativeMethods.GetWindowRect(focused, out var native))
        {
            return false;
        }

        return TryConvert(native, out rect);
    }

    private static bool TryGetCaretRect(IntPtr targetWindow, out Rect rect)
    {
        rect = Rect.Empty;
        if (targetWindow == IntPtr.Zero)
        {
            return false;
        }

        var threadId = NativeMethods.GetWindowThreadProcessId(targetWindow, out _);
        var info = new NativeMethods.GUITHREADINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>()
        };

        if (!NativeMethods.GetGUIThreadInfo(threadId, ref info) || info.hwndCaret == IntPtr.Zero)
        {
            return false;
        }

        var topLeft = new NativeMethods.POINT { X = info.rcCaret.Left, Y = info.rcCaret.Top };
        var bottomRight = new NativeMethods.POINT { X = info.rcCaret.Right, Y = info.rcCaret.Bottom };
        if (!NativeMethods.ClientToScreen(info.hwndCaret, ref topLeft)
            || !NativeMethods.ClientToScreen(info.hwndCaret, ref bottomRight))
        {
            return false;
        }

        // Carets are one or two pixels wide; widen the anchor so the overlay lines up
        // with the line rather than the exact insertion point.
        var width = Math.Max(2, bottomRight.X - topLeft.X);
        var height = Math.Max(2, bottomRight.Y - topLeft.Y);
        rect = new Rect(topLeft.X, topLeft.Y, width, height);
        return true;
    }

    private static bool TryGetWindowRect(IntPtr targetWindow, out Rect rect)
    {
        rect = Rect.Empty;
        if (targetWindow == IntPtr.Zero || !NativeMethods.GetWindowRect(targetWindow, out var native))
        {
            return false;
        }

        return TryConvert(native, out rect);
    }

    private static bool TryConvert(NativeMethods.RECT native, out Rect rect)
    {
        var width = native.Right - native.Left;
        var height = native.Bottom - native.Top;
        if (width <= 0 || height <= 0)
        {
            rect = Rect.Empty;
            return false;
        }

        rect = new Rect(native.Left, native.Top, width, height);
        return true;
    }

    private static bool IsUsable(Rect rect) => !rect.IsEmpty && rect.Width > 0 && rect.Height > 0;
}
