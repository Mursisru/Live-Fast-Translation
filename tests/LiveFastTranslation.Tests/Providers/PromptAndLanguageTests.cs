using LiveFastTranslation.Models;
using LiveFastTranslation.Services.Translation;
using Xunit;

namespace LiveFastTranslation.Tests.Providers;

public sealed class PromptBuilderTests
{
    [Fact]
    public void BuildSystemPrompt_ReplacesAllPlaceholders()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(
            "Translate from {source_language} ({source_language_code}) into {target_language} ({target_language_code}).",
            "de",
            "en");

        Assert.Equal("Translate from English (en) into German (de).", prompt);
        Assert.DoesNotContain("{", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSystemPrompt_UsesAutoDetectedSource()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(
            "Source: {source_language}; target: {target_language}.",
            "ru",
            "auto");

        Assert.Equal("Source: auto-detected; target: Russian.", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_AppendsTargetWhenPlaceholderIsMissing()
    {
        var prompt = PromptBuilder.BuildSystemPrompt("Translate the text.", "ja", "en");

        Assert.StartsWith("Translate the text.", prompt, StringComparison.Ordinal);
        Assert.Contains("Target language: Japanese (ja).", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSystemPrompt_FallsBackToDefaultTemplate()
    {
        var prompt = PromptBuilder.BuildSystemPrompt("   ", "fr", "auto");

        Assert.Contains("professional translator", prompt, StringComparison.Ordinal);
        Assert.Contains("French", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("{target_language}", prompt, StringComparison.Ordinal);
    }
}

public sealed class LanguageDetectorTests
{
    [Theory]
    [InlineData("Hello world, this is a simple test of the language detector.", "en")]
    [InlineData("Привет, как у тебя дела сегодня?", "ru")]
    [InlineData("Привіт, як у тебе справи сьогодні?", "uk")]
    [InlineData("こんにちは世界、これはテストです。", "ja")]
    [InlineData("你好世界，这是一个测试。", "zh-Hans")]
    [InlineData("안녕하세요, 세계!", "ko")]
    [InlineData("Καλημέρα κόσμε, αυτό είναι ένα τεστ.", "el")]
    [InlineData("مرحبا بالعالم، هذا اختبار.", "ar")]
    [InlineData("El perro está en la casa con el gato y la niña.", "es")]
    [InlineData("Der Hund ist mit dem Ball im Garten und spielt.", "de")]
    [InlineData("Le chien est dans le jardin avec la balle et il joue.", "fr")]
    public void Detect_RecognizesCommonLanguages(string text, string expected)
    {
        Assert.Equal(expected, LanguageDetector.Detect(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Detect_ReturnsAutoForEmptyText(string? text)
    {
        Assert.Equal("auto", LanguageDetector.Detect(text));
    }

    [Fact]
    public void Detect_ReturnsEnglishForPlainLatinWithoutStopwords()
    {
        Assert.Equal("en", LanguageDetector.Detect("Launchpad"));
    }
}
