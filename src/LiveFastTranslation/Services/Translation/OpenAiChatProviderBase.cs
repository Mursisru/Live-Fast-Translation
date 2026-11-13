using System.Net.Http.Headers;
using System.Text.Json;
using LiveFastTranslation.Models;

namespace LiveFastTranslation.Services.Translation;

/// <summary>Shared implementation for OpenAI Chat Completions compatible backends.</summary>
public abstract class OpenAiChatProviderBase : ITranslationProvider
{
    private readonly HttpClient _httpClient;

    protected OpenAiChatProviderBase(
        HttpClient httpClient,
        ProviderKind kind,
        string providerName,
        ProviderProfile profile,
        bool requiresApiKey,
        string? defaultBaseUrl = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        Profile = profile?.Clone() ?? throw new ArgumentNullException(nameof(profile));
        Kind = kind;
        ProviderName = providerName;
        RequiresApiKey = requiresApiKey;
        BaseUrl = ProviderDefaults.GetBaseUrl(kind, profile.BaseUrl, profile.ApiKey);
        if (!string.IsNullOrWhiteSpace(defaultBaseUrl) && string.IsNullOrWhiteSpace(BaseUrl))
        {
            BaseUrl = defaultBaseUrl;
        }
    }

    protected ProviderProfile Profile { get; }

    protected string BaseUrl { get; }

    protected bool RequiresApiKey { get; }

    public ProviderKind Kind { get; }

    public string ProviderName { get; }

    public async Task<TranslationResult> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        if (RequiresApiKey && !Profile.HasApiKey)
        {
            throw new TranslationException($"Укажите API-ключ для {ProviderName} в настройках.");
        }

        if (string.IsNullOrWhiteSpace(Profile.Model))
        {
            throw new TranslationException($"Укажите модель для {ProviderName} в настройках.");
        }

        var endpoint = EndpointBuilder.ChatCompletions(BaseUrl);
        var systemPrompt = PromptBuilder.BuildSystemPrompt(
            Profile.SystemPrompt,
            request.TargetLanguage,
            request.SourceLanguage);

        var payload = new
        {
            model = Profile.Model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = request.Text }
            },
            temperature = Math.Clamp(Profile.Temperature, 0, 2),
            stream = false
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = ProviderHttp.JsonContent(payload)
        };

        if (Profile.HasApiKey)
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Profile.ApiKey);
        }

        var json = await ProviderHttp.SendAsync(_httpClient, httpRequest, ProviderName, cancellationToken)
            .ConfigureAwait(false);
        var text = ParseContent(json);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new TranslationException($"{ProviderName} вернул пустой перевод.");
        }

        return new TranslationResult(
            text.Trim(),
            ProviderName,
            request.TargetLanguage,
            request.SourceLanguage);
    }

    protected virtual string ParseContent(string json)
    {
        using var document = JsonResponse.Parse(json, ProviderName);
        if (!document.RootElement.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            throw new TranslationException($"{ProviderName} вернул ответ без поля choices.");
        }

        var choice = choices[0];
        if (choice.TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content))
        {
            if (content.ValueKind == JsonValueKind.String)
            {
                return content.GetString() ?? string.Empty;
            }

            if (content.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var partText) && partText.ValueKind == JsonValueKind.String)
                    {
                        parts.Add(partText.GetString() ?? string.Empty);
                    }
                }

                return string.Concat(parts);
            }
        }

        if (choice.TryGetProperty("text", out var legacyText) && legacyText.ValueKind == JsonValueKind.String)
        {
            return legacyText.GetString() ?? string.Empty;
        }

        throw new TranslationException($"{ProviderName} вернул ответ без текста перевода.");
    }

    public static void ValidateRequest(TranslationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new TranslationException("Нет текста для перевода.");
        }

        if (!LanguageCatalog.IsSupported(request.TargetLanguage))
        {
            throw new TranslationException($"Неподдерживаемый язык перевода: {request.TargetLanguage}.");
        }
    }
}

public sealed class OpenAiProvider : OpenAiChatProviderBase
{
    public OpenAiProvider(HttpClient httpClient, ProviderProfile profile)
        : base(httpClient, ProviderKind.OpenAi, "OpenAI", profile, requiresApiKey: true)
    {
    }
}

public sealed class OpenCodeProvider : OpenAiChatProviderBase
{
    public OpenCodeProvider(HttpClient httpClient, ProviderProfile profile)
        : base(
            httpClient,
            ProviderKind.OpenCode,
            "OpenCode Zen",
            profile,
            requiresApiKey: false,
            defaultBaseUrl: "https://opencode.ai/zen/v1")
    {
    }
}

public sealed class OpenAiCompatibleProvider : OpenAiChatProviderBase
{
    public OpenAiCompatibleProvider(HttpClient httpClient, ProviderProfile profile)
        : base(
            httpClient,
            ProviderKind.OpenAiCompatible,
            "OpenAI-совместимый сервис",
            profile,
            requiresApiKey: false,
            defaultBaseUrl: "http://localhost:11434/v1")
    {
    }
}
