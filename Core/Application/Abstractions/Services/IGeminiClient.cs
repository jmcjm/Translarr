namespace Translarr.Core.Application.Abstractions.Services;

public interface IGeminiClient
{
    Task<string> TranslateSubtitlesAsync(string subtitlesContent, string systemPrompt, float temperature, string model);
}

