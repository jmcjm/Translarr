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

            // Get LLM settings once before processing batch
            var llmSettings = await settingsService.GetLlmSettingsAsync();
            logger.LogInformation("Using LLM model {model} at {baseUrl} for bitmap OCR", llmSettings.Model, llmSettings.BaseUrl);

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
                    await ProcessBitmapEntryAsync(entry, result, llmSettings, processedCount, totalFiles, onProgressUpdate, cancellationToken);
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
        LlmSettingsDto settings,
        int currentIndex,
        int totalFiles,
        Action<TranslationProgressUpdate>? onProgressUpdate,
        CancellationToken cancellationToken = default)
    {
        // 1. Check rate limit
        ReportProgress(TranslationStep.CheckingRateLimit);
        if (!await apiUsageService.CanMakeRequestAsync(settings.Model))
        {
            throw new InvalidOperationException("API rate limit exceeded");
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

        // 3. Translate bitmap subtitles via OCR
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(TranslationStep.TranslatingWithLlm);
        logger.LogInformation("Sending bitmap subtitles for OCR translation: {file}", entry.FileName);
        var translatedContent = await bitmapSubtitleTranslator.TranslateBitmapSubtitlesAsync(
            entry.FilePath, stream.StreamIndex, settings, cancellationToken);
        logger.LogInformation("Received OCR translation for {file}", entry.FileName);

        // 4. Save translated SRT
        ReportProgress(TranslationStep.SavingSubtitles);
        var baseFileName = Path.GetFileNameWithoutExtension(entry.FileName);
        var outputFileName = $"{baseFileName}.{settings.PreferredSubsLang}.srt";
        var outputPath = Path.Combine(Path.GetDirectoryName(entry.FilePath)!, outputFileName);
        await fileService.WriteTextAsync(outputPath, translatedContent);

        // 5. Update record
        entry.IsProcessed = true;
        entry.ProcessedAt = DateTime.UtcNow;
        entry.ErrorMessage = null;
        await repository.UpdateAsync(entry);

        // 6. Record API usage
        await apiUsageService.RecordUsageAsync(new ApiUsageDto
        {
            Model = settings.Model,
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
