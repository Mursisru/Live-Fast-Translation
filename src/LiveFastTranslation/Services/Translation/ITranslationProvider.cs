using LiveFastTranslation.Models;

namespace LiveFastTranslation.Services.Translation;

public interface ITranslationProvider
{
    ProviderKind Kind { get; }

    string ProviderName { get; }

    Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default);
}
