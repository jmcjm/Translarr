namespace Translarr.Core.Application.Models;

public enum TranslationStep
{
    Starting,
    CheckingRateLimit,
    FindingSubtitles,
    ExtractingSubtitles,
    CleaningSubtitles,
    ValidatingSize,
    TranslatingWithGemini,
    SavingSubtitles,
    Completed
}
