namespace LiveFastTranslation.Models;

/// <summary>Supported translation backends.</summary>
public enum ProviderKind
{
    OpenAi,
    Claude,
    Gemini,
    ArgosTranslate,
    OpenCode,
    OpenAiCompatible
}
