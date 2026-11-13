using System.Runtime.InteropServices;
using LiveFastTranslation.Interop;

namespace LiveFastTranslation.Services.TextReplace;

internal static class NativeInput
{
    public static void SendCtrlC() => SendVirtualKeyChord(new ushort[] { 0x43 });

    public static void SendCtrlV() => SendVirtualKeyChord(new ushort[] { 0x56 });

    public static void SendUnicodeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var inputs = new List<NativeMethods.INPUT>(text.Length * 2);
        foreach (var character in text)
        {
            inputs.Add(CreateKeyboardInput(0, character, NativeMethods.KEYEVENTF_UNICODE));
            inputs.Add(CreateKeyboardInput(0, character, NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP));        }

        Send(inputs);
    }

    public static void SendKey(ushort virtualKey)
    {
        Send(new List<NativeMethods.INPUT>
        {
            CreateKeyboardInput(virtualKey, 0, 0),
            CreateKeyboardInput(virtualKey, 0, NativeMethods.KEYEVENTF_KEYUP)
        });
    }

    private static void SendVirtualKeyChord(IReadOnlyList<ushort> keys)
    {
        var inputs = new List<NativeMethods.INPUT>
        {
            CreateKeyboardInput(NativeMethods.VK_CONTROL, 0, 0)
        };

        foreach (var key in keys)
        {
            inputs.Add(CreateKeyboardInput(key, 0, 0));
        }

        for (var index = keys.Count - 1; index >= 0; index--)
        {
            inputs.Add(CreateKeyboardInput(keys[index], 0, NativeMethods.KEYEVENTF_KEYUP));
        }

        inputs.Add(CreateKeyboardInput(NativeMethods.VK_CONTROL, 0, NativeMethods.KEYEVENTF_KEYUP));
        Send(inputs);
    }

    private static NativeMethods.INPUT CreateKeyboardInput(ushort virtualKey, ushort scanCharacter, uint flags)
    {
        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.INPUTUNION
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = scanCharacter,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    private static void Send(List<NativeMethods.INPUT> inputs)
    {
        if (inputs.Count == 0)
        {
            return;
        }

        var array = inputs.ToArray();
        var size = Marshal.SizeOf<NativeMethods.INPUT>();
        var sent = NativeMethods.SendInput((uint)array.Length, array, size);
        if (sent == 0)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"SendInput завершился с ошибкой {error}.");
        }
    }
}
