using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Abstractions.Services;

public interface IMediaScannerService
{
    Task<ScanResultDto> ScanLibraryAsync();
}

