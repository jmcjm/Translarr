namespace Translarr.Core.Application.Models;

public enum TranslationStep
{
    Starting,
    CheckingRateLimit,
    FindingSubtitles,
    ExtractingSubtitles,
    CleaningSubtitles,
    ValidatingSize,
    TranslatingWithLlm,
    SavingSubtitles,
    Completed
}
