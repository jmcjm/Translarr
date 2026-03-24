using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Abstractions.Services;

public interface IBitmapSubtitleTranslator
{
    Task<string> ExtractBitmapSubtitlesAsync(
        string videoPath, int streamIndex, LlmSettingsDto settings,
        Action<int, int>? onBatchProgress = null,
        CancellationToken cancellationToken = default);
}
