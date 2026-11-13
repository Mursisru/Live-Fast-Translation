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
}
