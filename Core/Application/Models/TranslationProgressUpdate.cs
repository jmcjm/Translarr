namespace Translarr.Core.Application.Models;

public record TranslationProgressUpdate(
    int TotalFiles,
    int ProcessedFiles,
    string CurrentFileName,
    TranslationStep CurrentStep,
    int CurrentBatch = 0,
    int TotalBatches = 0
);
