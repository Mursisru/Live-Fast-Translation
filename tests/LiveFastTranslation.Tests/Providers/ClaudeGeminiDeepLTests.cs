using System.Net;
using System.Net.Http;
using LiveFastTranslation.Models;
using LiveFastTranslation.Services.Translation;
using LiveFastTranslation.Tests.Fakes;
using Xunit;

namespace LiveFastTranslation.Tests.Providers;

public sealed class ClaudeProviderTests
{
    [Fact]
    public async Task TranslateAsync_SendsMessagesRequestAndJoinsTextBlocks()
    {
        var handler = RecordingHttpMessageHandler.Json(
            """{"content":[{"type":"text","text":"Bonjour"},{"type":"tool_use","id":"x"},{"type":"text","text":" le monde"}]}""");
        using var httpClient = new HttpClient(handler);
        var provider = new ClaudeProvider(httpClient, new ProviderProfile
        {
            ApiKey = "sk-ant-test",
            Model = "claude-3-5-haiku-latest",
            SystemPrompt = "Translate to {target_language}.",
            Temperature = 0.4
        });

        var result = await provider.TranslateAsync(new TranslationRequest("Hello world", "fr"));

        Assert.Equal("Bonjour le monde", result.TranslatedText);
        Assert.Equal("Anthropic Claude", result.Provider);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.anthropic.com/v1/messages", request.Uri?.ToString());
        Assert.Equal("sk-ant-test", request.Headers["x-api-key"]);
        Assert.Equal("2023-06-01", request.Headers["anthropic-version"]);
        Assert.Contains("\"max_tokens\":4096", request.Body, StringComparison.Ordinal);
        Assert.Contains("Translate to French.", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslateAsync_ThrowsWhenResponseHasNoText()
    {
        var handler = RecordingHttpMessageHandler.Json("""{"content":[]}""");
        using var httpClient = new HttpClient(handler);
        var provider = new ClaudeProvider(httpClient, new ProviderProfile
        {
            ApiKey = "sk-ant-test",
            Model = "claude-3-5-haiku-latest",
            SystemPrompt = "Translate to {target_language}."
        });

        var exception = await Assert.ThrowsAsync<TranslationException>(
            () => provider.TranslateAsync(new TranslationRequest("Hello", "fr")));

        Assert.Contains("пустой перевод", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslateAsync_HandlesRateLimitError()
    {
        var handler = RecordingHttpMessageHandler.Json(
            """{"error":{"type":"rate_limit_error","message":"Number of requests exceeded"}}""",
            HttpStatusCode.TooManyRequests);
        using var httpClient = new HttpClient(handler);
        var provider = new ClaudeProvider(httpClient, new ProviderProfile
        {
            ApiKey = "sk-ant-test",
            Model = "claude-3-5-haiku-latest",
            SystemPrompt = "Translate to {target_language}."
        });

        var exception = await Assert.ThrowsAsync<TranslationException>(
            () => provider.TranslateAsync(new TranslationRequest("Hello", "fr")));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.Contains("лимит", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Number of requests exceeded", exception.Message, StringComparison.Ordinal);
    }
}

public sealed class GeminiProviderTests
{
    [Fact]
    public async Task TranslateAsync_BuildsGenerateContentRequestAndJoinsParts()
    {
        var handler = RecordingHttpMessageHandler.Json(
            """{"candidates":[{"content":{"parts":[{"text":"Hallo"},{"text":" Welt"}]}}]}""");
        using var httpClient = new HttpClient(handler);
        var provider = new GeminiProvider(httpClient, new ProviderProfile
        {
            ApiKey = "AIza-test",
            Model = "gemini-2.0-flash",
            SystemPrompt = "Translate to {target_language}.",
            Temperature = 0.3
        });

        var result = await provider.TranslateAsync(new TranslationRequest("Hello world", "de"));

        Assert.Equal("Hallo Welt", result.TranslatedText);
        Assert.Equal("Google Gemini", result.Provider);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent",
            request.Uri?.ToString());
        Assert.Equal("AIza-test", request.Headers["x-goog-api-key"]);
        Assert.Contains("systemInstruction", request.Body, StringComparison.Ordinal);
        Assert.Contains("contents", request.Body, StringComparison.Ordinal);
        Assert.Contains("Translate to German.", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslateAsync_StripsModelsPrefixFromModel()
    {
        var handler = RecordingHttpMessageHandler.Json(
            """{"candidates":[{"content":{"parts":[{"text":"Привет"}]}}]}""");
        using var httpClient = new HttpClient(handler);
        var provider = new GeminiProvider(httpClient, new ProviderProfile
        {
            ApiKey = "AIza-test",
            Model = "models/gemini-2.5-flash",
            SystemPrompt = "Translate to {target_language}."
        });

        await provider.TranslateAsync(new TranslationRequest("Hello", "ru"));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent",
            request.Uri?.ToString());
    }

    [Fact]
    public async Task TranslateAsync_ReportsBlockedPrompt()
    {
        var handler = RecordingHttpMessageHandler.Json(
            """{"promptFeedback":{"blockReason":"SAFETY"}}""");
        using var httpClient = new HttpClient(handler);
        var provider = new GeminiProvider(httpClient, new ProviderProfile
        {
            ApiKey = "AIza-test",
            Model = "gemini-2.0-flash",
            SystemPrompt = "Translate to {target_language}."
        });

        var exception = await Assert.ThrowsAsync<TranslationException>(
            () => provider.TranslateAsync(new TranslationRequest("Hello", "de")));

        Assert.Contains("SAFETY", exception.Message, StringComparison.Ordinal);
    }
}

public sealed class ArgosTranslateProviderTests
{
    private sealed class TestArgosProvider : ArgosTranslateProvider
    {
        private readonly Func<string, string, string, string> _translate;

        public TestArgosProvider(Func<string, string, string, string> translate)
            : base(new HttpClient(), new ProviderProfile())
        {
            _translate = translate;
        }

        protected override Task<string> RunPythonTranslateAsync(string text, string from, string to, CancellationToken cancellationToken)
        {
            return Task.FromResult(_translate(text, from, to));
        }
    }

    [Fact]
    public async Task TranslateAsync_TranslatesLocally()
    {
        var provider = new TestArgosProvider((text, from, to) => $"[{from}->{to}] {text}");
        var result = await provider.TranslateAsync(new TranslationRequest("Hello", "ru"));

        Assert.Equal("[en->ru] Hello", result.TranslatedText);
        Assert.Equal("Argos Translate", result.Provider);
        Assert.Equal("en", result.DetectedSourceLanguage);
    }

    [Fact]
    public async Task TranslateAsync_DetectsSourceWhenAuto()
    {
        var provider = new TestArgosProvider((text, from, to) => "Привет");
        var result = await provider.TranslateAsync(new TranslationRequest("Hello", "ru", "auto"));

        Assert.Equal("en", result.DetectedSourceLanguage);
    }

    [Fact]
    public async Task TranslateAsync_ReturnsTextUnchangedWhenSameLanguage()
    {
        var provider = new TestArgosProvider((text, from, to) => throw new InvalidOperationException("should not run"));
        var result = await provider.TranslateAsync(new TranslationRequest("Hello", "en", "en"));

        Assert.Equal("Hello", result.TranslatedText);
        Assert.Equal("Argos Translate", result.Provider);
    }
}
