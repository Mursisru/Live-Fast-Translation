using LiveFastTranslation.Services.Input;
using Xunit;

namespace LiveFastTranslation.Tests.Input;

public sealed class SentenceCompletionDetectorTests
{
    private static SentenceCompletionDetector CreateDetector(int minLength = 4)
    {
        return new SentenceCompletionDetector
        {
            MinSentenceLength = minLength,
            MaxBufferLength = 4000
        };
    }

    private static string? Type(
        SentenceCompletionDetector detector,
        string text,
        params char[] extra)
    {
        string? completed = null;
        foreach (var character in text)
        {
            completed = detector.Append(character);
        }

        foreach (var character in extra)
        {
            completed = detector.Append(character);
        }

        return completed;
    }

    [Theory]
    [InlineData("Hello world. ")]
    [InlineData("Hello world! ")]
    [InlineData("How are you? ")]
    [InlineData("Это тест… ")]
    public void Append_CompletesSentenceAfterTerminatorAndSpace(string text)
    {
        var detector = CreateDetector();

        var completed = Type(detector, text);

        Assert.Equal(text.Trim(), completed);
        Assert.Equal(string.Empty, detector.Buffer);
    }

    [Fact]
    public void Append_UnderstandsClosingQuoteAfterTerminator()
    {
        var detector = CreateDetector();

        var completed = Type(detector, "\"Hello world.\" ");

        Assert.Equal("\"Hello world.\"", completed);
    }

    [Fact]
    public void ProcessEnter_CompletesSentenceWithoutTrailingSpace()
    {
        var detector = CreateDetector();
        Type(detector, "Hello world.");

        var completed = detector.ProcessEnter();

        Assert.Equal("Hello world.", completed);
        Assert.Equal(string.Empty, detector.Buffer);
    }

    [Fact]
    public void ProcessEnter_RequiresTerminatingPunctuation()
    {
        var detector = CreateDetector();
        Type(detector, "Hello world");

        Assert.Null(detector.ProcessEnter());
        Assert.Equal(string.Empty, detector.Buffer);
    }

    [Fact]
    public void Append_DoesNotCompleteWithoutPunctuation()
    {
        var detector = CreateDetector();

        Assert.Null(Type(detector, "Hello world "));
        Assert.Equal("Hello world ", detector.Buffer);
    }

    [Fact]
    public void Append_DiscardsSentenceShorterThanMinimum()
    {
        var detector = CreateDetector(minLength: 10);

        var completed = Type(detector, "Hi. ");

        Assert.Null(completed);
        Assert.Equal(string.Empty, detector.Buffer);
    }

    [Fact]
    public void Newline_BehavesLikeEnter()
    {
        var detector = CreateDetector();
        Type(detector, "Hello world.");

        var completed = detector.Append('\n');

        Assert.Equal("Hello world.", completed);
    }

    [Fact]
    public void Backspace_RemovesLastCharacter()
    {
        var detector = CreateDetector();
        Type(detector, "Hello?");

        detector.Backspace();
        var completed = Type(detector, "! ");

        Assert.Equal("Hello!", completed);
    }

    [Fact]
    public void Backspace_OnEmptyBufferIsSafe()
    {
        var detector = CreateDetector();

        detector.Backspace();
        detector.Backspace();

        Assert.Equal(string.Empty, detector.Buffer);
    }

    [Fact]
    public void Append_IgnoresControlCharacters()
    {
        var detector = CreateDetector();

        Assert.Null(detector.Append('\u0001'));
        Assert.Equal(string.Empty, detector.Buffer);
    }

    [Fact]
    public void MaxBufferLength_KeepsOnlyRecentText()
    {
        var detector = CreateDetector(minLength: 1);
        detector.MaxBufferLength = 10;

        Type(detector, "0123456789ABCDEF");

        Assert.Equal("6789ABCDEF", detector.Buffer);
    }

    [Fact]
    public void Reset_ClearsBuffer()
    {
        var detector = CreateDetector();
        Type(detector, "Hello");

        detector.Reset();

        Assert.Equal(string.Empty, detector.Buffer);
    }

    [Theory]
    [InlineData("Hello.", true)]
    [InlineData("Hello!", true)]
    [InlineData("Hello?", true)]
    [InlineData("Hello…", true)]
    [InlineData("Hello。", true)]
    [InlineData("Hello.\"", true)]
    [InlineData("Hello.  ", true)]
    [InlineData("Hello", false)]
    [InlineData("", false)]
    public void IsCompleteSentence_ChecksTrailingPunctuation(string text, bool expected)
    {
        Assert.Equal(expected, SentenceCompletionDetector.IsCompleteSentence(text));
    }
}
