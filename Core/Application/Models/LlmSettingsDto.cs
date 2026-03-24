namespace Translarr.Core.Application.Models;

public class LlmSettingsDto
{
    public required string ApiKey { get; init; }
    public required string BaseUrl { get; init; }
    public required string Model { get; init; }
    public required string SystemPrompt { get; init; }
    public float Temperature { get; init; }
    public int MaxOutputTokens { get; init; } = 65535;
    public required string PreferredSubsLang { get; init; }
    public int OcrBatchSize { get; init; } = 15;
}
