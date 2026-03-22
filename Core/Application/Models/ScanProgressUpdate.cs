namespace Translarr.Core.Application.Models;

public record ScanProgressUpdate(
    int TotalFiles,
    int ProcessedFiles,
    string CurrentFileName,
    ScanStep CurrentStep
);
