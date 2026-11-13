using System.Diagnostics;
using LiveFastTranslation.Models;
using LiveFastTranslation.Services.Infrastructure;
using LiveFastTranslation.Services.Settings;

namespace LiveFastTranslation.Services.Translation;

/// <summary>Resolves the active provider profile and performs a translation.</summary>
public sealed class TranslationService
{
    private readonly SettingsService _settings;
    private readonly HttpClient _httpClient;

    public TranslationService(SettingsService settings, HttpClient httpClient)
    {
        _settings = settings;
        _httpClient = httpClient;
    }

    public async Task<TranslationResult> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new TranslationException("Нет текста для перевода.");
        }

        var settings = _settings.Current;
        var kind = settings.Provider.GetEffectiveProvider(settings.AiMode.Enabled);
        var profile = settings.Provider.GetProfile(kind).Clone();
        var provider = TranslationProviderFactory.Create(kind, profile, _httpClient);
        var localDetection = LanguageDetector.Detect(request.Text);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await provider.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            AppLogger.Info($"Перевод через {provider.ProviderName}: {request.Text.Length} симв. за {stopwatch.ElapsedMilliseconds} мс.");

            var detected = result.DetectedSourceLanguage;
            if (string.IsNullOrWhiteSpace(detected) || detected.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                detected = localDetection;
            }

            return result with { DetectedSourceLanguage = detected };
        }
        catch (TranslationException exception)
        {
            AppLogger.Warn($"Ошибка перевода ({provider.ProviderName}): {exception.Message}");
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            AppLogger.Error($"Неожиданная ошибка перевода ({provider.ProviderName}).", exception);
            throw new TranslationException(
                $"Не удалось выполнить перевод через {provider.ProviderName}. Подробности записаны в журнал.",
                null,
                exception);
        }
    }

    public ITranslationProvider CreateProvider(ProviderKind kind, ProviderProfile profile)
    {
        return TranslationProviderFactory.Create(kind, profile, _httpClient);
    }
}
