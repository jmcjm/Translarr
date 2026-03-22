using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Abstractions.Services;

public interface IMediaScannerService
{
    Task<ScanResultDto> ScanLibraryAsync(
        Action<ScanProgressUpdate>? onProgressUpdate = null,
        CancellationToken cancellationToken = default);
}
