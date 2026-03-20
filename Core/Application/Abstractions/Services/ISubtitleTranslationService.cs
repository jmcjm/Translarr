using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Abstractions.Services;

public interface ISubtitleTranslationService
{
    Task<TranslationResultDto> TranslateNextBatchAsync(
        int batchSize = 1,
        Action<TranslationProgressUpdate>? onProgressUpdate = null,
        CancellationToken cancellationToken = default);
}

