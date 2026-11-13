using LiveFastTranslation.Models;

namespace LiveFastTranslation.Services.Translation;

public static class TranslationProviderFactory
{
    public static ITranslationProvider Create(ProviderKind kind, ProviderProfile profile, HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(httpClient);

        return kind switch
        {
            ProviderKind.OpenAi => new OpenAiProvider(httpClient, profile),
            ProviderKind.Claude => new ClaudeProvider(httpClient, profile),
            ProviderKind.Gemini => new GeminiProvider(httpClient, profile),
            ProviderKind.ArgosTranslate => new ArgosTranslateProvider(httpClient, profile),
            ProviderKind.OpenCode => new OpenCodeProvider(httpClient, profile),
            ProviderKind.OpenAiCompatible => new OpenAiCompatibleProvider(httpClient, profile),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Неизвестный провайдер перевода.")
        };
    }
}
