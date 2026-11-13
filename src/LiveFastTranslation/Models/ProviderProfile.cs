using System.Text.Json.Serialization;

namespace LiveFastTranslation.Models;

/// <summary>Per-provider credentials and generation settings.</summary>
public sealed class ProviderProfile
{
    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public double Temperature { get; set; } = 0.2;

    public string SystemPrompt { get; set; } = string.Empty;

    [JsonIgnore]
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    public ProviderProfile Clone()
    {
        return new ProviderProfile
        {
            ApiKey = ApiKey,
            Model = Model,
            BaseUrl = BaseUrl,
            Temperature = Temperature,
            SystemPrompt = SystemPrompt
        };
    }
}
