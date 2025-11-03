namespace Translarr.Core.Application.Models;

public class GeminiSettingsDto
{
    public required string ApiKey { get; init; }
    public required string Model { get; init; }
    public required string SystemPrompt { get; init; }
    public float Temperature { get; init; }
    public required string PreferredSubsLang { get; init; }
}
