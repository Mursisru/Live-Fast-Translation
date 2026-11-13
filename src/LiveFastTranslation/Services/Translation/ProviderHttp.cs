using System.Text;
using System.Text.Json;
using LiveFastTranslation.Models;

namespace LiveFastTranslation.Services.Translation;

public static class PromptBuilder
{
    public static string BuildSystemPrompt(string? template, string targetLanguageCode, string sourceLanguageCode)
    {
        var source = string.IsNullOrWhiteSpace(template) ? PromptDefaults.DefaultSystemPrompt : template.Trim();
        var targetName = LanguageCatalog.GetPromptName(targetLanguageCode);
        var targetCode = targetLanguageCode;
        var sourceName = sourceLanguageCode is null or "auto" or ""
            ? "auto-detected"
            : LanguageCatalog.GetPromptName(sourceLanguageCode);

        var containsTargetPlaceholder = source.Contains("{target_language}", StringComparison.Ordinal);

        var prompt = source
            .Replace("{target_language_code}", targetCode, StringComparison.Ordinal)
            .Replace("{target_language}", targetName, StringComparison.Ordinal)
            .Replace("{source_language_code}", sourceLanguageCode ?? "auto", StringComparison.Ordinal)
            .Replace("{source_language}", sourceName, StringComparison.Ordinal);

        if (!containsTargetPlaceholder)
        {
            prompt += $"{Environment.NewLine}Target language: {targetName} ({targetCode}).";
        }

        return prompt;
    }
}

internal static class EndpointBuilder
{
    public static string Combine(string baseUrl, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new TranslationException("Не задан Base URL провайдера.");
        }

        return $"{baseUrl.Trim().TrimEnd('/')}/{relativePath.TrimStart('/')}";
    }

    public static string ChatCompletions(string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        return trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : Combine(trimmed, "chat/completions");
    }

    public static string ClaudeMessages(string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/v1/messages", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? Combine(trimmed, "messages")
            : Combine(trimmed, "v1/messages");
    }

    public static string GeminiGenerate(string baseUrl, string model)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        var cleanModel = model.Trim().TrimStart('/');
        if (cleanModel.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
        {
            cleanModel = cleanModel["models/".Length..];
        }

        if (trimmed.Contains(":generateContent", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (trimmed.EndsWith("/v1beta", StringComparison.OrdinalIgnoreCase)
            || trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return Combine(trimmed, $"models/{cleanModel}:generateContent");
        }

        return Combine(trimmed, $"v1beta/models/{cleanModel}:generateContent");
    }

    // (No HTTP endpoint builder needed for fully local Argos Translate provider.)
}

public static class ProviderHttp
{
    private static readonly JsonSerializerOptions RequestOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<List<string>> FetchModelsAsync(HttpClient httpClient, string baseUrl, string? apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new TranslationException("Не задан Base URL провайдера.");
        }

        var trimmed = baseUrl.Trim().TrimEnd('/');
        var modelsUrl = trimmed.EndsWith("/models", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? $"{trimmed}/models"
                : $"{trimmed}/v1/models";

        using var request = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey.Trim());
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslationException("Превышено время ожидания ответа при получении списка моделей.");
        }
        catch (HttpRequestException exception)
        {
            throw new TranslationException("Не удалось получить список моделей. Проверьте Base URL и доступность сервиса.", null, exception);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateHttpException("Модели", response.StatusCode, body);
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                return new List<string>();
            }

            var models = new List<string>();
            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;

                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in data.EnumerateArray())
                    {
                        if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                        {
                            var value = id.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                models.Add(value.Trim());
                            }
                        }
                    }
                }

                if (models.Count == 0 && root.TryGetProperty("models", out var modelsArr) && modelsArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in modelsArr.EnumerateArray())
                    {
                        if (item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                        {
                            var value = name.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                models.Add(value.Trim());
                            }
                        }
                        else if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                        {
                            var value = id.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                models.Add(value.Trim());
                            }
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Ignore parse errors, return what we found
            }

            return models
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public static StringContent JsonContent(object payload)
    {
        var json = JsonSerializer.Serialize(payload, RequestOptions);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    public static async Task<string> SendAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        string providerName,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslationException($"Превышено время ожидания ответа от {providerName}. Попробуйте ещё раз.");
        }
        catch (HttpRequestException exception)
        {
            throw new TranslationException(
                $"Не удалось подключиться к {providerName}. Проверьте интернет, Base URL и доступность сервиса.",
                null,
                exception);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateHttpException(providerName, response.StatusCode, body);
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                throw new TranslationException($"{providerName} вернул пустой ответ.", response.StatusCode);
            }

            return body;
        }
    }

    private static TranslationException CreateHttpException(
        string providerName,
        System.Net.HttpStatusCode statusCode,
        string body)
    {
        var detail = ExtractErrorMessage(body);
        var status = (int)statusCode;
        var message = status switch
        {
            400 => $"{providerName}: запрос отклонён. {detail}",
            401 => $"{providerName}: неверный или отсутствующий API-ключ. {detail}",
            403 => $"{providerName}: доступ запрещён. Проверьте ключ, тариф и страну аккаунта. {detail}",
            404 => $"{providerName}: модель или адрес API не найдены. {detail}",
            429 => $"{providerName}: превышен лимит запросов. Подождите и повторите попытку. {detail}",
            >= 500 => $"{providerName}: сервис временно недоступен (HTTP {status}). {detail}",
            _ => $"{providerName}: ошибка HTTP {status}. {detail}"
        };

        return new TranslationException(message.Trim(), statusCode);
    }

    private static string ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var trimmed = body.Trim();
        if (trimmed.Length > 600)
        {
            trimmed = trimmed[..600];
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString() ?? string.Empty;
                }

                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message))
                {
                    return message.GetString() ?? string.Empty;
                }
            }

            if (root.TryGetProperty("message", out var rootMessage))
            {
                return rootMessage.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("detail", out var detail))
            {
                return detail.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // The body is not JSON; return a compact raw representation.
        }

        return trimmed;
    }
}

public static class JsonResponse
{
    public static JsonDocument Parse(string json, string providerName)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new TranslationException($"{providerName} вернул некорректный JSON.", null, exception);
        }
    }
}
