using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Abstractions.Services;

public interface IGeminiClient
{
    Task<string> TranslateSubtitlesAsync(string subtitlesContent, GeminiSettingsDto settings);
}

