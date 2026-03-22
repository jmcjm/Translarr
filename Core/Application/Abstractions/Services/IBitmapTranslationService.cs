using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Abstractions.Services;

public interface IBitmapTranslationService
{
    Task<TranslationResultDto> TranslateBitmapBatchAsync(
        int batchSize = 100,
        Action<TranslationProgressUpdate>? onProgressUpdate = null,
        CancellationToken cancellationToken = default);
}
