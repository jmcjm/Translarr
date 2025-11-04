namespace Translarr.Core.Application.Models;

public record TranslationProgressUpdate(
    int TotalFiles,
    int ProcessedFiles,
    string CurrentFileName,
    TranslationStep CurrentStep
);
