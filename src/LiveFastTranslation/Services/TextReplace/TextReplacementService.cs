using LiveFastTranslation.Models;

namespace LiveFastTranslation.Services.TextReplace;

public static class TextReplacerFactory
{
    private static readonly ITextReplacer UiAutomationReplacer = new UiAutomationTextReplacer();
    private static readonly ITextReplacer Win32Replacer = new Win32MessageTextReplacer();
    private static readonly ITextReplacer SendKeysReplacer = new SendKeysTextReplacer();
    private static readonly ITextReplacer ClipboardReplacer = new ClipboardTextReplacer();

    public static IReadOnlyList<ITextReplacer> Create(ReplacementStrategy strategy)
    {
        return strategy switch
        {
            ReplacementStrategy.UiAutomation => new[] { UiAutomationReplacer },
            ReplacementStrategy.Win32Message => new[] { Win32Replacer },
            ReplacementStrategy.SendKeys => new[] { SendKeysReplacer },
            ReplacementStrategy.Clipboard => new[] { ClipboardReplacer },
            _ => new[] { UiAutomationReplacer, Win32Replacer, SendKeysReplacer, ClipboardReplacer }
        };
    }
}

/// <summary>Runs the configured replacement strategy, applying delays, focus and fallbacks.</summary>
public sealed class TextReplacementService
{
    public async Task<ReplaceOutcome> ReplaceAsync(
        ReplacementStrategy strategy,
        ReplaceContext context,
        string replacement,
        ReplaceOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(replacement))
        {
            return ReplaceOutcome.Fail("Замена", "Перевод пуст.");
        }

        if (options.ActivateTargetWindow)
        {
            Win32Focus.ActivateTargetWindow(context.TargetWindow);
        }

        if (options.DelayBeforeReplaceMs > 0)
        {
            await Task.Delay(options.DelayBeforeReplaceMs, cancellationToken).ConfigureAwait(true);
        }

        if (options.AnimateInsertion)
        {
            return await InsertAnimatedAsync(strategy, context, replacement, options, cancellationToken)
                .ConfigureAwait(true);
        }

        return await ReplaceCoreAsync(strategy, context, replacement, options, cancellationToken)
            .ConfigureAwait(true);
    }

    private static async Task<ReplaceOutcome> ReplaceCoreAsync(
        ReplacementStrategy strategy,
        ReplaceContext context,
        string replacement,
        ReplaceOptions options,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var replacer in TextReplacerFactory.Create(strategy))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var outcome = await replacer.ReplaceAsync(context, replacement, options, cancellationToken)
                    .ConfigureAwait(true);
                if (outcome.Success)
                {
                    return outcome;
                }

                errors.Add($"{replacer.Name}: {outcome.Error}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                errors.Add($"{replacer.Name}: {exception.Message}");
            }
        }

        return ReplaceOutcome.Fail(
            "Замена текста",
            errors.Count > 0
                ? string.Join(Environment.NewLine, errors)
                : "Не удалось заменить текст.");
    }

    /// <summary>
    /// Types the translation one character at a time. When text is selected, that selection is
    /// cleared first with the configured strategy, so only the selected range is ever touched
    /// and the surrounding text stays intact.
    /// </summary>
    private static async Task<ReplaceOutcome> InsertAnimatedAsync(
        ReplacementStrategy strategy,
        ReplaceContext context,
        string replacement,
        ReplaceOptions options,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(context.SelectedText))
        {
            var cleared = await ReplaceCoreAsync(strategy, context, string.Empty, options, cancellationToken)
                .ConfigureAwait(true);
            if (!cleared.Success)
            {
                return cleared;
            }
        }

        FocusTarget(context);

        var characterDelay = Math.Clamp(1200 / Math.Max(1, replacement.Length), 2, 18);
        foreach (var character in replacement)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NativeInput.SendUnicodeText(character.ToString());
            await Task.Delay(characterDelay, cancellationToken).ConfigureAwait(true);
        }

        if (options.MoveCaretAfterReplace && options.CursorPlacement != CursorPlacement.End)
        {
            var handle = context.Element is null
                ? IntPtr.Zero
                : new IntPtr(context.Element.Current.NativeWindowHandle);
            if (handle != IntPtr.Zero)
            {
                Win32Focus.TrySetCaret(handle, replacement.Length, options.CursorPlacement);
            }
        }

        return ReplaceOutcome.Ok("Побуквенная вставка");
    }

    private static void FocusTarget(ReplaceContext context)
    {
        Win32Focus.ActivateTargetWindow(context.TargetWindow);
        try
        {
            context.Element?.SetFocus();
        }
        catch
        {
            // Some providers refuse focus; SendInput still reaches the foreground control.
        }
    }
}
