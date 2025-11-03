using Microsoft.Extensions.Logging;
using Translarr.Core.Application.Abstractions.Repositories;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Services;

public class SubtitleTranslationService(
    ISubtitleEntryRepository repository,
    ISettingsService settingsService,
    IApiUsageService apiUsageService,
    IFfmpegService ffmpegService,
    ILogger<SubtitleTranslationService> logger,
    IGeminiClient geminiClient)
    : ISubtitleTranslationService
{
    private const string WorkDir = "/tmp/translarr";

    public async Task<TranslationResultDto> TranslateNextBatchAsync(int batchSize = 100)
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

            foreach (var entry in entries)
            {
                try
                {
                    await ProcessEntryAsync(entry, result, geminiSettings);
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

    private async Task ProcessEntryAsync(SubtitleEntryDto entry, TranslationResultDto result, GeminiSettingsDto settings)
    {
        // 1. Check rate limit
        if (!await apiUsageService.CanMakeRequestAsync(settings.Model))
        {
            throw new InvalidOperationException("API rate limit exceeded");
        }
        
        // 3. Find best subtitle stream
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
            var subtitlePathForTranslation = extractedSubtitlePath;

            if (codecName.Equals("ass", StringComparison.OrdinalIgnoreCase) ||
                codecName.Equals("ssa", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("Detected ASS/SSA subtitle format, cleaning file before conversion");
                await ffmpegService.CleanAssFile(extractedSubtitlePath);

                // Convert cleaned ASS to SRT for translation
                var srtPath = Path.ChangeExtension(extractedSubtitlePath, ".srt");
                var conversionSuccess = await ffmpegService.ConvertToSrt(extractedSubtitlePath, srtPath);

                if (!conversionSuccess)
                {
                    throw new InvalidOperationException("Failed to convert cleaned ASS to SRT");
                }

                subtitlePathForTranslation = srtPath;
            }

            // 6. Read subtitles and validate size
            var subtitleContent = await File.ReadAllTextAsync(subtitlePathForTranslation);

            // Size validation - Gemini free tier has token limits
            // TODO - make it configurable
            const int maxSizeBytes = 100 * 2048;
            if (subtitleContent.Length > maxSizeBytes)
            {
                throw new InvalidOperationException(
                    $"Subtitle file too large after cleaning: {subtitleContent.Length} bytes (max: {maxSizeBytes} bytes). " +
                    "This file cannot be processed with the current Gemini API limits.");
            }

            // 7. Call Gemini API
            var translatedContent = await geminiClient.TranslateSubtitlesAsync(subtitleContent, settings);

            // 8. Save translated subtitles
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
    }
}