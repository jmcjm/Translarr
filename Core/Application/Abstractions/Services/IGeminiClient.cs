using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Abstractions.Services;

public interface IGeminiClient
{
    /// <summary>
    /// Translates the provided subtitles content.
    /// </summary>
    /// <param name="subtitlesContent">The content of the subtitles to be translated.</param>
    /// <param name="settings">Object with Gemini settings.</param>
    /// <returns>The translated subtitles content.</returns>
    Task<string> TranslateSubtitlesAsync(string subtitlesContent, GeminiSettingsDto settings);

    /// <summary>
    /// Translates the provided subtitles using a chat approach to get around the max output token limit problem.
    /// </summary>
    /// <param name="subtitlesContent">The content of the subtitles to be translated.</param>
    /// <param name="settings">Object with Gemini settings.</param>
    /// <returns>The translated subtitles content.</returns>
    Task<string> TranslateLargeSubtitlesAsync(string subtitlesContent, GeminiSettingsDto settings);
}

