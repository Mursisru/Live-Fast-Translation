using System.Text.Json;
using LiveFastTranslation.Models;

namespace LiveFastTranslation.Services.Translation;

public sealed class GeminiProvider : ITranslationProvider
{
    private readonly HttpClient _httpClient;
    private readonly ProviderProfile _profile;
    private readonly string _baseUrl;

    public GeminiProvider(HttpClient httpClient, ProviderProfile profile)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _profile = profile?.Clone() ?? throw new ArgumentNullException(nameof(profile));
        _baseUrl = ProviderDefaults.GetBaseUrl(ProviderKind.Gemini, profile.BaseUrl, profile.ApiKey);
    }

    public ProviderKind Kind => ProviderKind.Gemini;

    public string ProviderName => "Google Gemini";

    public async Task<TranslationResult> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        OpenAiChatProviderBase.ValidateRequest(request);
        if (!_profile.HasApiKey)
        {
            throw new TranslationException("Укажите API-ключ Google Gemini в настройках.");
        }

        if (string.IsNullOrWhiteSpace(_profile.Model))
        {
            throw new TranslationException("Укажите модель Gemini в настройках.");
        }

        var systemPrompt = PromptBuilder.BuildSystemPrompt(
            _profile.SystemPrompt,
            request.TargetLanguage,
            request.SourceLanguage);

        var payload = new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = request.Text } }
                }
            },
            generationConfig = new
            {
                temperature = Math.Clamp(_profile.Temperature, 0, 2)
            }
        };

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            EndpointBuilder.GeminiGenerate(_baseUrl, _profile.Model))
        {
            Content = ProviderHttp.JsonContent(payload)
        };
        httpRequest.Headers.TryAddWithoutValidation("x-goog-api-key", _profile.ApiKey);

        var json = await ProviderHttp.SendAsync(_httpClient, httpRequest, ProviderName, cancellationToken)
            .ConfigureAwait(false);

        using var document = JsonResponse.Parse(json, ProviderName);
        var root = document.RootElement;
        if (!root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            var reason = TryReadBlockReason(root);
            throw new TranslationException(
                reason is null
                    ? "Gemini не вернул вариантов перевода."
                    : $"Gemini заблокировал запрос: {reason}.");
        }

        var parts = new List<string>();
        if (candidates[0].TryGetProperty("content", out var content)
            && content.TryGetProperty("parts", out var contentParts)
            && contentParts.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in contentParts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    parts.Add(text.GetString() ?? string.Empty);
                }
            }
        }

        var translation = string.Concat(parts).Trim();
        if (translation.Length == 0)
        {
            throw new TranslationException("Gemini вернул пустой перевод.");
        }

        return new TranslationResult(translation, ProviderName, request.TargetLanguage, request.SourceLanguage);
    }

    private static string? TryReadBlockReason(JsonElement root)
    {
        if (root.TryGetProperty("promptFeedback", out var feedback)
            && feedback.TryGetProperty("blockReason", out var reason)
            && reason.ValueKind == JsonValueKind.String)
        {
            return reason.GetString();
        }

        return null;
    }
}
