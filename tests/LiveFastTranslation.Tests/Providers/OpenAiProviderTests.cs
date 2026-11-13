using System.Net;
using System.Net.Http;
using LiveFastTranslation.Models;
using LiveFastTranslation.Services.Translation;
using LiveFastTranslation.Tests.Fakes;
using Xunit;

namespace LiveFastTranslation.Tests.Providers;

public sealed class OpenAiProviderTests
{
    private static ProviderProfile CreateProfile(
        string model = "gpt-4o-mini",
        string apiKey = "sk-test",
        string baseUrl = "")
    {
        return new ProviderProfile
        {
            ApiKey = apiKey,
            Model = model,
            BaseUrl = baseUrl,
            Temperature = 0.2,
            SystemPrompt = "Translate into {target_language}."
        };
    }

    [Fact]
    public async Task TranslateAsync_SendsChatCompletionsRequestAndParsesContent()
    {
        var handler = RecordingHttpMessageHandler.Json(
            """{"choices":[{"message":{"content":"  Привет  "}}]}""");
        using var httpClient = new HttpClient(handler);
        var provider = new OpenAiProvider(httpClient, CreateProfile());

        var result = await provider.TranslateAsync(new TranslationRequest("Hello", "ru"));

        Assert.Equal("Привет", result.TranslatedText);
        Assert.Equal("OpenAI", result.Provider);
        Assert.Equal("auto", result.DetectedSourceLanguage);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.openai.com/v1/chat/completions", request.Uri?.ToString());
        Assert.Equal("Bearer sk-test", request.Headers["Authorization"]);
        Assert.Contains("\"model\":\"gpt-4o-mini\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("Translate into Russian.", request.Body, StringComparison.Ordinal);
        Assert.Contains("Hello", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"temperature\":0.2", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslateAsync_UsesCompatibleArrayContent()
    {
        var handler = RecordingHttpMessageHandler.Json(
            """{"choices":[{"message":{"content":[{"type":"text","text":"Hallo"},{"type":"text","text":" Welt"}]}}]}""");
        using var httpClient = new HttpClient(handler);
        var provider = new OpenAiProvider(httpClient, CreateProfile());

        var result = await provider.TranslateAsync(new TranslationRequest("Hello", "de"));

        Assert.Equal("Hallo Welt", result.TranslatedText);
    }

    [Fact]
    public async Task TranslateAsync_ThrowsReadableErrorForUnauthorizedResponse()
    {
        var handler = RecordingHttpMessageHandler.Json(
            """{"error":{"message":"Incorrect API key provided"}}""",
            HttpStatusCode.Unauthorized);
        using var httpClient = new HttpClient(handler);
        var provider = new OpenAiProvider(httpClient, CreateProfile());

        var exception = await Assert.ThrowsAsync<TranslationException>(
            () => provider.TranslateAsync(new TranslationRequest("Hello", "ru")));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Contains("неверный", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Incorrect API key provided", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslateAsync_RequiresApiKey()
    {
        var handler = RecordingHttpMessageHandler.Json("""{"choices":[]}""");
        using var httpClient = new HttpClient(handler);
        var provider = new OpenAiProvider(httpClient, CreateProfile(apiKey: string.Empty));

        var exception = await Assert.ThrowsAsync<TranslationException>(
            () => provider.TranslateAsync(new TranslationRequest("Hello", "ru")));

        Assert.Contains("API-ключ", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task TranslateAsync_ThrowsWhenContentIsEmpty()
    {
        var handler = RecordingHttpMessageHandler.Json(
            """{"choices":[{"message":{"content":"   "}}]}""");
        using var httpClient = new HttpClient(handler);
        var provider = new OpenAiProvider(httpClient, CreateProfile());

        var exception = await Assert.ThrowsAsync<TranslationException>(
            () => provider.TranslateAsync(new TranslationRequest("Hello", "ru")));

        Assert.Contains("пустой перевод", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenCode_UsesDefaultZenEndpoint()
    {
        var handler = RecordingHttpMessageHandler.Json(
            """{"choices":[{"message":{"content":"Привет"}}]}""");
        using var httpClient = new HttpClient(handler);
        var provider = new OpenCodeProvider(
            httpClient,
            CreateProfile(model: "big-pickle", apiKey: "zen-key"));

        var result = await provider.TranslateAsync(new TranslationRequest("Hello", "ru"));

        Assert.Equal("OpenCode Zen", result.Provider);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://opencode.ai/zen/v1/chat/completions", request.Uri?.ToString());
    }

    [Fact]
    public async Task OpenAiCompatible_AllowsMissingKeyAndCustomBaseUrl()
    {
        var handler = RecordingHttpMessageHandler.Json(
            """{"choices":[{"message":{"content":"Hallo"}}]}""");
        using var httpClient = new HttpClient(handler);
        var provider = new OpenAiCompatibleProvider(
            httpClient,
            CreateProfile(model: "llama3.1", apiKey: string.Empty, baseUrl: "http://localhost:11434/v1"));

        var result = await provider.TranslateAsync(new TranslationRequest("Hello", "de"));

        Assert.Equal("Hallo", result.TranslatedText);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://localhost:11434/v1/chat/completions", request.Uri?.ToString());
        Assert.False(request.Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public void ProviderDefaults_ResolveArgosTranslateBaseUrl()
    {
        Assert.Equal(
            string.Empty,
            ProviderDefaults.GetBaseUrl(ProviderKind.ArgosTranslate, string.Empty, string.Empty));
        Assert.Equal(
            "py -3",
            ProviderDefaults.GetBaseUrl(ProviderKind.ArgosTranslate, "py -3", "some-key"));
    }
}
