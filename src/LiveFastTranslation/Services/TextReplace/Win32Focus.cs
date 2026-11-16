using System.Runtime.InteropServices;
using System.Text;
using LiveFastTranslation.Interop;

namespace LiveFastTranslation.Services.TextReplace;

/// <summary>Helpers around the focused native control, shared by capture and replacement.</summary>
internal static class Win32Focus
{
    public static IntPtr GetFocusedControlHwnd()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var threadId = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var info = new NativeMethods.GUITHREADINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>()
        };

        if (NativeMethods.GetGUIThreadInfo(threadId, ref info) && info.hwndFocus != IntPtr.Zero)
        {
            return info.hwndFocus;
        }

        return foreground;
    }

    public static string GetClassName(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(256);
        var length = NativeMethods.GetClassName(hWnd, builder, builder.Capacity);
        return length > 0 ? builder.ToString() : string.Empty;
    }

    public static bool IsEditLike(IntPtr hWnd)
    {
        var className = GetClassName(hWnd);
        return IsEditLikeClass(className);
    }

    public static bool IsEditLikeClass(string className)
    {
        return className.StartsWith("Edit", StringComparison.OrdinalIgnoreCase)
               || className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase)
               || className.StartsWith("TEdit", StringComparison.OrdinalIgnoreCase)
               || className.StartsWith("TFloatSpinEdit", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPasswordField(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        var style = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_STYLE).ToInt64();
        return (style & NativeMethods.ES_PASSWORD) != 0;
    }

    public static bool IsReadOnly(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        var style = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_STYLE).ToInt64();
        return (style & NativeMethods.ES_READONLY) != 0;
    }

    public static (int Start, int End) GetSelection(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return (0, 0);
        }

        var packed = NativeMethods.SendMessage(hWnd, NativeMethods.EM_GETSEL, IntPtr.Zero, IntPtr.Zero).ToInt64();
        var start = (int)(packed & 0xFFFF);
        var end = (int)((packed >> 16) & 0xFFFF);
        return start <= end ? (start, end) : (end, start);
    }

    public static string? GetControlText(IntPtr hWnd, int maxLength = 200_000)
    {
        if (hWnd == IntPtr.Zero)
        {
            return null;
        }

        var length = NativeMethods.SendMessage(hWnd, NativeMethods.WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero).ToInt32();
        if (length <= 0)
        {
            return string.Empty;
        }

        length = Math.Min(length, maxLength);
        var builder = new StringBuilder(length + 1);
        NativeMethods.SendMessage(hWnd, NativeMethods.WM_GETTEXT, new IntPtr(length + 1), builder);
        return builder.ToString();
    }

    public static bool TryReadSelectedText(out string text, out IntPtr focusedControl)
    {
        text = string.Empty;
        focusedControl = GetFocusedControlHwnd();
        if (focusedControl == IntPtr.Zero || !IsEditLike(focusedControl) || IsPasswordField(focusedControl))
        {
            return false;
        }

        var (start, end) = GetSelection(focusedControl);
        if (start == end)
        {
            return false;
        }

        var fullText = GetControlText(focusedControl);
        if (fullText is null || start >= fullText.Length)
        {
            return false;
        }

        end = Math.Min(end, fullText.Length);
        if (end <= start)
        {
            return false;
        }

        text = fullText[start..end];
        return !string.IsNullOrEmpty(text);
    }

    public static bool TryReplaceSelection(IntPtr hWnd, string replacement)
    {
        if (hWnd == IntPtr.Zero || !IsEditLike(hWnd) || IsPasswordField(hWnd) || IsReadOnly(hWnd))
        {
            return false;
        }

        NativeMethods.SendMessage(hWnd, NativeMethods.EM_REPLACESEL, new IntPtr(1), replacement);
        return true;
    }

    public static void TrySetCaret(IntPtr hWnd, int replacementLength, Models.CursorPlacement placement)
    {
        if (hWnd == IntPtr.Zero || !IsEditLike(hWnd))
        {
            return;
        }

        switch (placement)
        {
            case Models.CursorPlacement.Start:
                NativeMethods.SendMessage(hWnd, NativeMethods.EM_SETSEL, IntPtr.Zero, IntPtr.Zero);
                break;
            case Models.CursorPlacement.SelectAll:
                NativeMethods.SendMessage(hWnd, NativeMethods.EM_SETSEL, IntPtr.Zero, new IntPtr(-1));
                break;
            case Models.CursorPlacement.End:
                NativeMethods.SendMessage(
                    hWnd,
                    NativeMethods.EM_SETSEL,
                    new IntPtr(replacementLength),
                    new IntPtr(replacementLength));
                break;
        }
    }

    public static void ActivateTargetWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        if (NativeMethods.GetForegroundWindow() != hWnd)
        {
            NativeMethods.SetForegroundWindow(hWnd);
        }
    }
}
