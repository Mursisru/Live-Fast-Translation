namespace LiveFastTranslation.Models;

public sealed record ProviderInfo(
    ProviderKind Kind,
    string DisplayName,
    string Description,
    bool RequiresApiKey,
    string DefaultBaseUrl,
    string DefaultModel,
    IReadOnlyList<string> SuggestedModels,
    string ModelHint,
    string BaseUrlHint);

/// <summary>Static metadata describing each backend, used by settings and the provider factory.</summary>
public static class ProviderCatalog
{
    public static IReadOnlyList<ProviderInfo> All { get; } = new List<ProviderInfo>
    {
        new(
            ProviderKind.OpenAi,
            "OpenAI",
            "ChatGPT через официальный API OpenAI.",
            true,
            "https://api.openai.com/v1",
            "gpt-4o-mini",
            new[] { "gpt-4o-mini", "gpt-4o", "gpt-4.1-mini", "gpt-4.1", "o4-mini" },
            "Название модели, например gpt-4o-mini.",
            "Базовый URL API. Обычно https://api.openai.com/v1."),
        new(
            ProviderKind.Claude,
            "Anthropic Claude",
            "Модели Claude через Messages API.",
            true,
            "https://api.anthropic.com",
            "claude-3-5-haiku-latest",
            new[] { "claude-3-5-haiku-latest", "claude-sonnet-4-5", "claude-haiku-4-5", "claude-opus-4-1" },
            "Идентификатор модели Claude.",
            "Базовый URL API Anthropic. Обычно https://api.anthropic.com."),
        new(
            ProviderKind.Gemini,
            "Google Gemini",
            "Gemini через Generative Language API.",
            true,
            "https://generativelanguage.googleapis.com",
            "gemini-2.0-flash",
            new[] { "gemini-2.0-flash", "gemini-2.5-flash", "gemini-2.5-pro" },
            "Идентификатор модели Gemini, без префикса models/.",
            "Базовый URL API Gemini."),
        new(
            ProviderKind.ArgosTranslate,
            "Argos Translate",
            "Полностью локальный офлайн-перевод через Argos Translate (Python). Требуются установленные Python, argostranslate и языковые пакеты.",
            false,
            string.Empty,
            string.Empty,
            new[] { string.Empty, "py -3", "python", "python3" },
            "Необязательно: команда запуска Python (например, py -3 или python).",
            "Необязательно: команда Python для запуска Argos Translate. Оставьте пустым для автоопределения (py -3 / python)."),
        new(
            ProviderKind.OpenCode,
            "OpenCode (Zen)",
            "OpenCode Zen — OpenAI-совместимый шлюз к разным моделям.",
            false,
            "https://opencode.ai/zen/v1",
            "big-pickle",
            new[] { "big-pickle", "gpt-5-codex", "claude-sonnet-4-5", "qwen3-coder", "kimi-k2", "glm-4.6" },
            "Идентификатор модели OpenCode Zen.",
            "Базовый URL OpenAI-совместимого шлюза OpenCode Zen."),
        new(
            ProviderKind.OpenAiCompatible,
            "OpenAI-совместимый",
            "Любой локальный или сторонний OpenAI-совместимый сервер: Ollama, LM Studio, vLLM и другие.",
            false,
            "http://localhost:11434/v1",
            "llama3.1",
            new[] { "llama3.1", "qwen2.5", "mistral-nemo", "gpt-oss:20b" },
            "Имя модели, загруженной на вашем сервере.",
            "Базовый URL с суффиксом /v1, например http://localhost:11434/v1.")
    };

    public static ProviderInfo Get(ProviderKind kind)
    {
        return All.First(info => info.Kind == kind);
    }

    public static string ExampleApiKey(ProviderKind kind)
    {
        return kind switch
        {
            ProviderKind.OpenAi => "sk-...",
            ProviderKind.Claude => "sk-ant-...",
            ProviderKind.Gemini => "AIza...",
            ProviderKind.ArgosTranslate => "Не требуется (локальный перевод)",
            ProviderKind.OpenCode => "Ключ OpenCode Zen (необязательно)",
            _ => "Для локального сервера обычно не требуется"
        };
    }
}

/// <summary>Defaults and URL resolution shared by the app and provider implementations.</summary>
public static class ProviderDefaults
{
    public static string GetBaseUrl(ProviderKind kind, string configuredBaseUrl, string apiKey)
    {
        if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            return configuredBaseUrl.Trim().TrimEnd('/');
        }

        return ProviderCatalog.Get(kind).DefaultBaseUrl;
    }

    public static Dictionary<ProviderKind, ProviderProfile> CreateProfiles()
    {
        var profiles = new Dictionary<ProviderKind, ProviderProfile>();
        foreach (var info in ProviderCatalog.All)
        {
            profiles[info.Kind] = new ProviderProfile
            {
                Model = info.DefaultModel,
                BaseUrl = info.DefaultBaseUrl,
                Temperature = 0.2,
                SystemPrompt = PromptDefaults.DefaultSystemPrompt
            };
        }

        return profiles;
    }
}
