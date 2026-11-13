namespace LiveFastTranslation.Models;

public sealed class ProviderSettings
{
    /// <summary>AI provider used when AI mode is enabled. Argos Translate is used as the standard provider when AI mode is off.</summary>
    public ProviderKind AiProvider { get; set; } = ProviderKind.OpenAi;

    /// <summary>Legacy compatibility field; mirrors AiProvider after normalization.</summary>
    public ProviderKind ActiveProvider
    {
        get => AiProvider;
        set => AiProvider = value;
    }

    public Dictionary<ProviderKind, ProviderProfile> Profiles { get; set; } = ProviderDefaults.CreateProfiles();

    public ProviderProfile GetProfile(ProviderKind kind)
    {
        if (!Profiles.TryGetValue(kind, out var profile) || profile is null)
        {
            var info = ProviderCatalog.Get(kind);
            profile = new ProviderProfile
            {
                Model = info.DefaultModel,
                BaseUrl = info.DefaultBaseUrl,
                SystemPrompt = PromptDefaults.DefaultSystemPrompt
            };
            Profiles[kind] = profile;
        }

        return profile;
    }

    public ProviderKind GetEffectiveProvider(bool aiModeEnabled)
    {
        return aiModeEnabled ? NormalizeAiProvider(AiProvider) : ProviderKind.ArgosTranslate;
    }

    public static ProviderKind NormalizeAiProvider(ProviderKind kind)
    {
        return kind == ProviderKind.ArgosTranslate || !Enum.IsDefined(kind)
            ? ProviderKind.OpenAi
            : kind;
    }
}
