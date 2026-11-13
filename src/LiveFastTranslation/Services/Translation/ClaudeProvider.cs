using System.Net.Http.Headers;
using System.Text.Json;
using LiveFastTranslation.Models;

namespace LiveFastTranslation.Services.Translation;

public sealed class ClaudeProvider : ITranslationProvider
{
    private readonly HttpClient _httpClient;
    private readonly ProviderProfile _profile;
    private readonly string _baseUrl;

    public ClaudeProvider(HttpClient httpClient, ProviderProfile profile)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _profile = profile?.Clone() ?? throw new ArgumentNullException(nameof(profile));
        _baseUrl = ProviderDefaults.GetBaseUrl(ProviderKind.Claude, profile.BaseUrl, profile.ApiKey);
    }

    public ProviderKind Kind => ProviderKind.Claude;

    public string ProviderName => "Anthropic Claude";

    public async Task<TranslationResult> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        OpenAiChatProviderBase.ValidateRequest(request);
        if (!_profile.HasApiKey)
        {
            throw new TranslationException("Укажите API-ключ Anthropic в настройках.");
        }

        if (string.IsNullOrWhiteSpace(_profile.Model))
        {
            throw new TranslationException("Укажите модель Claude в настройках.");
        }

        var systemPrompt = PromptBuilder.BuildSystemPrompt(
            _profile.SystemPrompt,
            request.TargetLanguage,
            request.SourceLanguage);

        var payload = new
        {
            model = _profile.Model,
            max_tokens = 4096,
            temperature = Math.Clamp(_profile.Temperature, 0, 1),
            system = systemPrompt,
            messages = new object[]
            {
                new { role = "user", content = request.Text }
            }
        };

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            EndpointBuilder.ClaudeMessages(_baseUrl))
        {
            Content = ProviderHttp.JsonContent(payload)
        };
        httpRequest.Headers.TryAddWithoutValidation("x-api-key", _profile.ApiKey);
        httpRequest.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var json = await ProviderHttp.SendAsync(_httpClient, httpRequest, ProviderName, cancellationToken)
            .ConfigureAwait(false);

        using var document = JsonResponse.Parse(json, ProviderName);
        if (!document.RootElement.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            throw new TranslationException("Claude вернул ответ без содержимого.");
        }

        var parts = new List<string>();
        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type)
                && type.GetString() == "text"
                && block.TryGetProperty("text", out var text)
                && text.ValueKind == JsonValueKind.String)
            {
                parts.Add(text.GetString() ?? string.Empty);
            }
        }

        var translation = string.Concat(parts).Trim();
        if (translation.Length == 0)
        {
            throw new TranslationException("Claude вернул пустой перевод.");
        }

        return new TranslationResult(translation, ProviderName, request.TargetLanguage, request.SourceLanguage);
    }
}
