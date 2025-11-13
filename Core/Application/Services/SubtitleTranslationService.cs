using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Translarr.Core.Application.Abstractions.Repositories;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Services;

public partial class SubtitleTranslationService(
    ISubtitleEntryRepository repository,
    ISettingsService settingsService,
    IApiUsageService apiUsageService,
    IFfmpegService ffmpegService,
    ILogger<SubtitleTranslationService> logger,
    IGeminiClient geminiClient)
    : ISubtitleTranslationService
{
    private const string WorkDir = "/tmp/translarr";

    public async Task<TranslationResultDto> TranslateNextBatchAsync(
        int batchSize = 100,
        Action<TranslationProgressUpdate>? onProgressUpdate = null)
    {
        logger.LogInformation("Starting translation");
        var startTime = DateTime.UtcNow;
        var result = new TranslationResultDto();
        var errors = new List<string>();

        try
        {
            // Get files to process
            var entries = await repository.GetUnprocessedWantedAsync(batchSize);

            if (entries.Count == 0)
            {
                logger.LogInformation("No unprocessed entries found");
                return result;
            }

            logger.LogInformation("Found {count} unprocessed entries", entries.Count);

            // Get Gemini settings once before processing batch
            var geminiSettings = await settingsService.GetGeminiSettingsAsync();
            logger.LogInformation("Using Gemini model {model}", geminiSettings.Model);

            var totalFiles = entries.Count;
            var processedCount = 0;

            foreach (var entry in entries)
            {
                try
                {
                    await ProcessEntryAsync(entry, result, geminiSettings, processedCount, totalFiles, onProgressUpdate);
                    processedCount++;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing {file}: {msg}", entry.FileName, ex.Message);
                    errors.Add($"Error processing {entry.FileName}: {ex.Message}");
                    result.ErrorCount++;

                    // DON'T mark as processed - save only error
                    // File will be processed again in next attempt
                    entry.ErrorMessage = ex.Message;
                    await repository.UpdateAsync(entry);
                    processedCount++;
                }
            }

            result.Errors = errors;
            result.Duration = DateTime.UtcNow - startTime;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Critical error during translation: {msg}", ex.Message);
            errors.Add($"Critical error during translation: {ex.Message}");
            result.Errors = errors;
            result.Duration = DateTime.UtcNow - startTime;
        }

        logger.LogInformation("Translation complete");
        return result;
    }

    private async Task ProcessEntryAsync(
        SubtitleEntryDto entry,
        TranslationResultDto result,
        GeminiSettingsDto settings,
        int currentIndex,
        int totalFiles,
        Action<TranslationProgressUpdate>? onProgressUpdate)
    {
        // 1. Check rate limit
        ReportProgress(TranslationStep.CheckingRateLimit);
        if (!await apiUsageService.CanMakeRequestAsync(settings.Model))
        {
            throw new InvalidOperationException("API rate limit exceeded");
        }

        // 3. Find best subtitle stream
        ReportProgress(TranslationStep.FindingSubtitles);
        var subtitleStream = await ffmpegService.FindBestSubtitleStreamAsync(entry.FilePath);
        
        if (subtitleStream == null)
        {
            // CASE: No suitable subtitles - mark as processed
            // (this is a permanent state, no point in trying again)
            logger.LogWarning("No suitable subtitles found for {file}, skipping", entry.FileName);
            entry.IsProcessed = true;
            entry.ProcessedAt = DateTime.UtcNow;
            entry.ErrorMessage = "No suitable embedded subtitles found - skipped";
            await repository.UpdateAsync(entry);
            result.SkippedNoSubtitles++;
            return;
        }

        // 4. Extract subtitles to WorkDir
        ReportProgress(TranslationStep.ExtractingSubtitles);
        var baseFileName = Path.GetFileNameWithoutExtension(entry.FileName);

        var codecName = subtitleStream.CodecName.ToLowerInvariant();

        var extractedSubtitlePath = Path.Combine(WorkDir, $"{baseFileName}.{subtitleStream.Language}.{codecName}");

        // Make sure WorkDir exists
        Directory.CreateDirectory(WorkDir);

        var extractionSuccess = await ffmpegService.ExtractSubtitlesAsync(entry.FilePath, subtitleStream.StreamIndex, extractedSubtitlePath, codecName);

        if (!extractionSuccess)
        {
            throw new InvalidOperationException("Failed to extract subtitles from video file");
        }

        try
        {
            // 5. Clean ASS files if needed
            if (codecName.Equals("ass", StringComparison.OrdinalIgnoreCase) ||
                codecName.Equals("ssa", StringComparison.OrdinalIgnoreCase))
            {
                ReportProgress(TranslationStep.CleaningSubtitles);
                logger.LogInformation("Detected ASS/SSA subtitle format, cleaning file before conversion");
                await ffmpegService.CleanAssFile(extractedSubtitlePath);
            }

            // 6. Read subtitles and validate size
            ReportProgress(TranslationStep.ValidatingSize);
            var subtitleContent = await File.ReadAllTextAsync(extractedSubtitlePath);

            // Size validation - Gemini free tier has token limits
            // TODO - make it configurable
            const int maxSizeBytes = 100 * 2548;
            if (subtitleContent.Length > maxSizeBytes)
            {
                throw new InvalidOperationException(
                    $"Subtitle file too large after cleaning: {subtitleContent.Length} bytes (max: {maxSizeBytes} bytes). " +
                    "This file cannot be processed with the current Gemini API limits.");
            }

            logger.LogInformation("Sending subtitles to Gemini API");
            // 7. Call Gemini API
            ReportProgress(TranslationStep.TranslatingWithGemini);
            var translatedContent = await geminiClient.TranslateSubtitlesAsync(subtitleContent, settings);
            logger.LogInformation("Received translated subtitles from Gemini API");

            // If the model returned answer in markdown code block or {} format, remove it
            translatedContent = MyRegex().Replace(translatedContent, "").Trim();

            // 8. Save translated subtitles
            ReportProgress(TranslationStep.SavingSubtitles);
            var outputFileName = $"{baseFileName}.{settings.PreferredSubsLang}.srt";
            var outputPath = Path.Combine(Path.GetDirectoryName(entry.FilePath)!, outputFileName);
            await File.WriteAllTextAsync(outputPath, translatedContent);

            // 10. Update record
            entry.IsProcessed = true;
            entry.ProcessedAt = DateTime.UtcNow;
            entry.ErrorMessage = null;
            await repository.UpdateAsync(entry);

            // 9. Log API usage
            await apiUsageService.RecordUsageAsync(new ApiUsageDto
            {
                Model = settings.Model,
                Date = DateTime.UtcNow
            });

            result.SuccessCount++;
        }
        finally
        {
            logger.LogInformation("Removing temporary files");
            // 10. Remove temporary files
            if (File.Exists(extractedSubtitlePath))
            {
                File.Delete(extractedSubtitlePath);
            }
        }

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

    [GeneratedRegex(@"^```[\w-]*\n|```$", RegexOptions.Multiline)]
    private static partial Regex MyRegex();
}