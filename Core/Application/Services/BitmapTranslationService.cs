using Microsoft.Extensions.Logging;
using Translarr.Core.Application.Abstractions.Repositories;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Services;

public class BitmapTranslationService(
    ISubtitleEntryRepository repository,
    IUnitOfWork unitOfWork,
    ISettingsService settingsService,
    IApiUsageService apiUsageService,
    IFfmpegService ffmpegService,
    IBitmapSubtitleTranslator bitmapSubtitleTranslator,
    ISubtitleTranslator subtitleTranslator,
    ILogger<BitmapTranslationService> logger,
    IFileService fileService)
    : IBitmapTranslationService
{
    public async Task<TranslationResultDto> TranslateBitmapBatchAsync(
        int batchSize = 100,
        Action<TranslationProgressUpdate>? onProgressUpdate = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting bitmap OCR translation");
        var startTime = DateTime.UtcNow;
        var result = new TranslationResultDto();
        var errors = new List<string>();

        try
        {
            var entries = await repository.GetUnprocessedWantedBitmapAsync(batchSize);

            if (entries.Count == 0)
            {
                logger.LogInformation("No unprocessed bitmap entries found");
                return result;
            }

            logger.LogInformation("Found {count} unprocessed bitmap entries", entries.Count);

            // Get separate settings for OCR and translation
            var ocrSettings = await settingsService.GetOcrLlmSettingsAsync();
            var translationSettings = await settingsService.GetLlmSettingsAsync();
            logger.LogInformation("Using OCR model {ocrModel} and translation model {transModel}", ocrSettings.Model, translationSettings.Model);

            var totalFiles = entries.Count;
            var processedCount = 0;

            foreach (var entry in entries)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    logger.LogInformation("Bitmap OCR translation cancelled by user after {count}/{total} files", processedCount, totalFiles);
                    errors.Add("Translation was cancelled by user");
                    break;
                }

                try
                {
                    await ProcessBitmapEntryAsync(entry, result, ocrSettings, translationSettings, processedCount, totalFiles, onProgressUpdate, cancellationToken);
                    processedCount++;
                }
                catch (OperationCanceledException)
                {
                    logger.LogInformation("Bitmap OCR translation cancelled by user during processing of {file}", entry.FileName);
                    errors.Add("Translation was cancelled by user");
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing bitmap entry {file}: {msg}", entry.FileName, ex.Message);
                    errors.Add($"Error processing {entry.FileName}: {ex.Message}");
                    result.ErrorCount++;

                    // Don't mark as processed - save only error so the file can be retried
                    entry.ErrorMessage = ex.Message;
                    await repository.UpdateAsync(entry);
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    processedCount++;
                }
            }

            result.Errors = errors;
            result.Duration = DateTime.UtcNow - startTime;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Bitmap OCR translation cancelled by user");
            errors.Add("Translation was cancelled by user");
            result.Errors = errors;
            result.Duration = DateTime.UtcNow - startTime;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Critical error during bitmap OCR translation: {msg}", ex.Message);
            errors.Add($"Critical error during translation: {ex.Message}");
            result.Errors = errors;
            result.Duration = DateTime.UtcNow - startTime;
        }

        logger.LogInformation("Bitmap OCR translation complete");
        return result;
    }

    private async Task ProcessBitmapEntryAsync(
        SubtitleEntryDto entry,
        TranslationResultDto result,
        LlmSettingsDto ocrSettings,
        LlmSettingsDto translationSettings,
        int currentIndex,
        int totalFiles,
        Action<TranslationProgressUpdate>? onProgressUpdate,
        CancellationToken cancellationToken = default)
    {
        // 1. Check rate limit for OCR model
        ReportProgress(TranslationStep.CheckingRateLimit);
        if (!await apiUsageService.CanMakeRequestAsync(ocrSettings.Model))
        {
            throw new InvalidOperationException("API rate limit exceeded for OCR model");
        }

        // 2. Find best bitmap subtitle stream
        ReportProgress(TranslationStep.FindingSubtitles);
        var stream = await ffmpegService.FindBestBitmapSubtitleStreamAsync(entry.FilePath);

        if (stream == null)
        {
            logger.LogWarning("No bitmap subtitle stream found for {file}, skipping", entry.FileName);
            entry.IsProcessed = true;
            entry.ProcessedAt = DateTime.UtcNow;
            entry.ErrorMessage = "No bitmap subtitle stream found";
            await repository.UpdateAsync(entry);
            await unitOfWork.SaveChangesAsync();
            result.SkippedNoSubtitles++;
            return;
        }

        // 3. Stage 1: OCR — extract text from bitmap subtitles
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(TranslationStep.OcrExtraction);
        logger.LogInformation("Sending bitmap subtitles for OCR extraction: {file}", entry.FileName);
        var ocrResult = await bitmapSubtitleTranslator.ExtractBitmapSubtitlesAsync(
            entry.FilePath, stream.StreamIndex, ocrSettings,
            onBatchProgress: (current, total) =>
            {
                onProgressUpdate?.Invoke(new TranslationProgressUpdate(
                    TotalFiles: totalFiles,
                    ProcessedFiles: currentIndex,
                    CurrentFileName: entry.FileName,
                    CurrentStep: TranslationStep.OcrExtraction,
                    CurrentBatch: current,
                    TotalBatches: total));
            },
            cancellationToken: cancellationToken);
        logger.LogInformation("OCR extraction complete for {file}", entry.FileName);

        // 4. Check rate limit for translation model
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(TranslationStep.CheckingRateLimit);
        if (!await apiUsageService.CanMakeRequestAsync(translationSettings.Model))
        {
            throw new InvalidOperationException("API rate limit exceeded for translation model");
        }

        // 5. Stage 2: Translation — translate extracted text
        ReportProgress(TranslationStep.TranslatingOcrResult);
        logger.LogInformation("Translating OCR result for {file}", entry.FileName);
        string translatedContent;
        try
        {
            translatedContent = await subtitleTranslator.TranslateSubtitlesAsync(ocrResult, translationSettings);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"OCR succeeded but translation failed: {ex.Message}", ex);
        }
        logger.LogInformation("Translation complete for {file}", entry.FileName);

        // 6. Save translated SRT
        ReportProgress(TranslationStep.SavingSubtitles);
        var baseFileName = Path.GetFileNameWithoutExtension(entry.FileName);
        var outputFileName = $"{baseFileName}.{translationSettings.PreferredSubsLang}.srt";
        var outputPath = Path.Combine(Path.GetDirectoryName(entry.FilePath)!, outputFileName);
        await fileService.WriteTextAsync(outputPath, translatedContent);

        // 7. Update record
        entry.IsProcessed = true;
        entry.AlreadyHad = true;
        entry.ProcessedAt = DateTime.UtcNow;
        entry.ErrorMessage = null;
        await repository.UpdateAsync(entry);

        // 8. Record API usage for both models
        await apiUsageService.RecordUsageAsync(new ApiUsageDto
        {
            Model = ocrSettings.Model,
            Date = DateTime.UtcNow
        });
        await apiUsageService.RecordUsageAsync(new ApiUsageDto
        {
            Model = translationSettings.Model,
            Date = DateTime.UtcNow
        });

        await unitOfWork.SaveChangesAsync();

        result.SuccessCount++;

        return;

        void ReportProgress(TranslationStep step)
        {
            onProgressUpdate?.Invoke(new TranslationProgressUpdate(
                TotalFiles: totalFiles,
                ProcessedFiles: currentIndex,
                CurrentFileName: entry.FileName,
                CurrentStep: step
            ));
        }
    }
}
