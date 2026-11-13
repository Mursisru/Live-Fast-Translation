using System.Net.Http;
using LiveFastTranslation.Models;
using LiveFastTranslation.Services.Settings;
using LiveFastTranslation.Services.Translation;
using LiveFastTranslation.Tests.Fakes;
using Xunit;

namespace LiveFastTranslation.Tests.Services;

public sealed class TranslationServiceTests
{
    [Fact]
    public async Task TranslateAsync_FillsDetectedLanguageWithLocalDetector()
    {
        using var directory = new TempDirectory();
        var settings = new SettingsService(directory.Path);
        settings.Load();
        settings.Current.AiMode.Enabled = true;
        settings.Current.Provider.AiProvider = ProviderKind.OpenAiCompatible;
        var profile = settings.Current.Provider.GetProfile(ProviderKind.OpenAiCompatible);
        profile.BaseUrl = "http://fake.local/v1";
        profile.Model = "test-model";

        var handler = RecordingHttpMessageHandler.Json(
            """{"choices":[{"message":{"content":"Hallo"}}]}""");
        using var httpClient = new HttpClient(handler);
        var service = new TranslationService(settings, httpClient);

        var result = await service.TranslateAsync(new TranslationRequest("Привет, как дела?", "de"));

        Assert.Equal("Hallo", result.TranslatedText);
        Assert.Equal("OpenAI-совместимый сервис", result.Provider);
        Assert.Equal("ru", result.DetectedSourceLanguage);
        Assert.Equal("de", result.TargetLanguage);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://fake.local/v1/chat/completions", request.Uri?.ToString());
    }

    [Fact]
    public async Task TranslateAsync_KeepsExplicitSourceLanguage()
    {
        using var directory = new TempDirectory();
        var settings = new SettingsService(directory.Path);
        settings.Load();
        settings.Current.AiMode.Enabled = true;
        settings.Current.Provider.AiProvider = ProviderKind.OpenAiCompatible;
        var profile = settings.Current.Provider.GetProfile(ProviderKind.OpenAiCompatible);
        profile.BaseUrl = "http://fake.local/v1";
        profile.Model = "test-model";

        var handler = RecordingHttpMessageHandler.Json(
            """{"choices":[{"message":{"content":"Hallo"}}]}""");
        using var httpClient = new HttpClient(handler);
        var service = new TranslationService(settings, httpClient);

        var result = await service.TranslateAsync(new TranslationRequest("Привет", "de", "en"));

        Assert.Equal("en", result.DetectedSourceLanguage);
    }

    [Fact]
    public async Task TranslateAsync_RejectsEmptyTextWithoutHttpCall()
    {
        using var directory = new TempDirectory();
        var settings = new SettingsService(directory.Path);
        settings.Load();

        var handler = RecordingHttpMessageHandler.Json("""{"choices":[]}""");
        using var httpClient = new HttpClient(handler);
        var service = new TranslationService(settings, httpClient);

        var exception = await Assert.ThrowsAsync<TranslationException>(
            () => service.TranslateAsync(new TranslationRequest("   ", "de")));

        Assert.Contains("Нет текста", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(ProviderKind.OpenAi, typeof(OpenAiProvider))]
    [InlineData(ProviderKind.Claude, typeof(ClaudeProvider))]
    [InlineData(ProviderKind.Gemini, typeof(GeminiProvider))]
    [InlineData(ProviderKind.ArgosTranslate, typeof(ArgosTranslateProvider))]
    [InlineData(ProviderKind.OpenCode, typeof(OpenCodeProvider))]
    [InlineData(ProviderKind.OpenAiCompatible, typeof(OpenAiCompatibleProvider))]
    public void Factory_CreatesProviderForEachKind(ProviderKind kind, Type expectedType)
    {
        using var httpClient = new HttpClient(new RecordingHttpMessageHandler(
            _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
        var profile = ProviderCatalog.Get(kind).DefaultModel.Length > 0
            ? new ProviderProfile
            {
                Model = ProviderCatalog.Get(kind).DefaultModel,
                BaseUrl = ProviderCatalog.Get(kind).DefaultBaseUrl,
                ApiKey = "key"
            }
            : new ProviderProfile { ApiKey = "key" };

        var provider = TranslationProviderFactory.Create(kind, profile, httpClient);

        Assert.IsType(expectedType, provider);
        Assert.Equal(kind, provider.Kind);
    }
}
