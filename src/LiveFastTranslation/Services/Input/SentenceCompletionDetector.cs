using System.Text;

namespace LiveFastTranslation.Services.Input;

/// <summary>
/// Accumulates locally typed characters and reports a sentence once terminating punctuation is
/// followed by Space or Enter. Pure logic, covered by unit tests.
/// </summary>
public sealed class SentenceCompletionDetector
{
    private static readonly char[] SentenceTerminators = { '.', '!', '?', '…', '。', '！', '？' };
    private static readonly char[] ClosingCharacters = { '"', '\'', '»', ')', ']', '}', '”', '’' };

    private readonly StringBuilder _buffer = new();

    public int MinSentenceLength { get; set; } = 4;

    public int MaxBufferLength { get; set; } = 4000;

    public string Buffer => _buffer.ToString();

    public void Reset() => _buffer.Clear();

    public string? Append(char character)
    {
        if (character == '\r')
        {
            return null;
        }

        if (character == '\n')
        {
            return ProcessEnter();
        }

        if (char.IsControl(character) && character != '\t')
        {
            return null;
        }

        var completed = character == ' ' && IsCompleteSentence(_buffer.ToString());
        _buffer.Append(character);
        if (_buffer.Length > MaxBufferLength)
        {
            _buffer.Remove(0, _buffer.Length - MaxBufferLength);
        }

        if (!completed)
        {
            return null;
        }

        var sentence = _buffer.ToString().Trim();
        _buffer.Clear();
        return IsLongEnough(sentence) ? sentence : null;
    }

    public string? ProcessEnter()
    {
        var completed = IsCompleteSentence(_buffer.ToString());
        var sentence = _buffer.ToString().Trim();
        _buffer.Clear();
        return completed && IsLongEnough(sentence) ? sentence : null;
    }

    public void Backspace()
    {
        if (_buffer.Length > 0)
        {
            _buffer.Length--;
        }
    }

    public static bool IsCompleteSentence(string text)
    {
        var index = text.Length - 1;
        while (index >= 0 && (char.IsWhiteSpace(text[index]) || ClosingCharacters.Contains(text[index])))
        {
            index--;
        }

        return index >= 0 && SentenceTerminators.Contains(text[index]);
    }

    private bool IsLongEnough(string sentence)
    {
        return sentence.Trim().Length >= Math.Max(1, MinSentenceLength);
    }
}
