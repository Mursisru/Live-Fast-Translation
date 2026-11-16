using System.Text;
using System.Windows.Forms;
using LiveFastTranslation.Models;

namespace LiveFastTranslation.Services.TextReplace;

/// <summary>
/// Sends the translation as keyboard input. ASCII text goes through WinForms SendKeys; Unicode text
/// uses SendInput with KEYEVENTF_UNICODE, because SendKeys cannot type arbitrary Unicode reliably.
/// </summary>
public sealed class SendKeysTextReplacer : ITextReplacer
{
    public string Name => "SendKeys / Unicode input";

    public async Task<ReplaceOutcome> ReplaceAsync(
        ReplaceContext context,
        string replacement,
        ReplaceOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // An empty replacement is used by animated insertion to clear the selection.
        // Typing an empty string does nothing: the first typed character will replace
        // the still-active selection.
        if (string.IsNullOrEmpty(replacement))
        {
            return ReplaceOutcome.Ok(Name);
        }

        try
        {
            if (replacement.All(character => character < 128 && (character >= 32 || character is '\r' or '\n' or '\t')))
            {
                var escaped = EscapeForSendKeys(replacement);
                if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
                {
                    await dispatcher.InvokeAsync(() => SendKeys.SendWait(escaped));
                }
                else
                {
                    SendKeys.SendWait(escaped);
                }
            }
            else
            {
                NativeInput.SendUnicodeText(replacement);
            }

            await Task.Delay(60, cancellationToken).ConfigureAwait(true);

            if (options.MoveCaretAfterReplace && options.CursorPlacement != CursorPlacement.End)
            {
                var focused = Win32Focus.GetFocusedControlHwnd();
                Win32Focus.TrySetCaret(focused, replacement.Length, options.CursorPlacement);
            }

            return ReplaceOutcome.Ok(Name);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return ReplaceOutcome.Fail(Name, $"SendKeys: {exception.Message}");
        }
    }

    internal static string EscapeForSendKeys(string text)
    {
        var builder = new StringBuilder(text.Length + 16);        foreach (var character in text)
        {
            switch (character)
            {
                case '\r':
                    break;
                case '\n':
                    builder.Append("{ENTER}");
                    break;
                case '\t':
                    builder.Append("{TAB}");
                    break;
                case '+':
                    builder.Append("{+}");
                    break;
                case '^':
                    builder.Append("{^}");
                    break;
                case '%':
                    builder.Append("{%}");
                    break;
                case '~':
                    builder.Append("{~}");
                    break;
                case '(':
                    builder.Append("{(}");
                    break;
                case ')':
                    builder.Append("{)}");
                    break;
                case '[':
                    builder.Append("{[}");
                    break;
                case ']':
                    builder.Append("{]}");
                    break;
                case '{':
                    builder.Append("{{}");
                    break;
                case '}':
                    builder.Append("{}}");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }
}
