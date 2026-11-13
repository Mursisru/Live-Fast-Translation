namespace LiveFastTranslation.Models;

public sealed record TranslationRequest(
    string Text,
    string TargetLanguage,
    string SourceLanguage = "auto");

public sealed record TranslationResult(
    string TranslatedText,
    string Provider,
    string TargetLanguage,
    string DetectedSourceLanguage);
