using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Abstractions.Services;

public interface ISubtitleTranslator
{
    Task<string> TranslateSubtitlesAsync(string subtitlesContent, LlmSettingsDto settings);
    Task<string> TranslateWithImagesAsync(string prompt, List<byte[]> images, LlmSettingsDto settings);
}
