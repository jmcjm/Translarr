using Translarr.Core.Application.Abstractions.Repositories;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Services;

public class SubtitleTranslationService(
    ISubtitleEntryRepository repository,
    ISettingsService settingsService,
    IApiUsageService apiUsageService,
    IFfmpegService ffmpegService,
    IGeminiClient geminiClient)
    : ISubtitleTranslationService
{
    private const string WorkDir = "/tmp/translarr";

    public async Task<TranslationResultDto> TranslateNextBatchAsync(int batchSize = 1)
    {
        var startTime = DateTime.UtcNow;
        var result = new TranslationResultDto();
        var errors = new List<string>();

        try
        {
            // Get files to process
            var entries = await repository.GetUnprocessedWantedAsync(batchSize);

            foreach (var entry in entries)
            {
                try
                {
                    await ProcessEntryAsync(entry, result, errors);
                }
                catch (Exception ex)
                {
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
            errors.Add($"Critical error during translation: {ex.Message}");
            result.Errors = errors;
            result.Duration = DateTime.UtcNow - startTime;
        }

        return result;
    }

    private async Task ProcessEntryAsync(SubtitleEntryDto entry, TranslationResultDto result, List<string> errors)
    {
        var model = await settingsService.GetSettingAsync("GeminiModel") ?? throw new ArgumentException("GeminiModel setting not found");
        
        // 1. Check rate limit
        if (!await apiUsageService.CanMakeRequestAsync(model))
        {
            throw new InvalidOperationException("API rate limit exceeded");
        }
        
        // 3. Find best subtitle stream
        var subtitleStream = await ffmpegService.FindBestSubtitleStreamAsync(entry.FilePath);
        
        if (subtitleStream == null)
        {
            // CASE: No suitable subtitles - mark as processed
            // (this is a permanent state, no point in trying again)
            entry.IsProcessed = true;
            entry.ProcessedAt = DateTime.UtcNow;
            entry.ErrorMessage = "No suitable embedded subtitles found - skipped";
            await repository.UpdateAsync(entry);
            result.SkippedNoSubtitles++;
            return;
        }

        // 4. Extract subtitles to WorkDir
        var baseFileName = Path.GetFileNameWithoutExtension(entry.FileName);
        var extractedSubtitlePath = Path.Combine(WorkDir, $"{baseFileName}.{subtitleStream.Language}.srt");
        
        // Make sure WorkDir exists
        Directory.CreateDirectory(WorkDir);
        
        var extractionSuccess = await ffmpegService.ExtractSubtitlesAsync(entry.FilePath, subtitleStream.StreamIndex, extractedSubtitlePath);
        
        if (!extractionSuccess)
        {
            throw new InvalidOperationException("Failed to extract subtitles from video file");
        }

        try
        {
            // 5. Get transcoding settings
            var systemPrompt = await settingsService.GetSettingAsync("SystemPrompt") ?? throw new ArgumentException("SystemPrompt setting not found");
            var temperatureStr = await settingsService.GetSettingAsync("Temperature") ?? throw new ArgumentException("Temperature setting not found");
            var temperature = float.TryParse(temperatureStr, out var temp) ? temp : throw new ArgumentException($"Invalid value for setting Temperature, value: {temperatureStr}");
            var preferredLang = await settingsService.GetSettingAsync("PreferredSubsLang") ?? throw new ArgumentException("PreferredSubsLang setting not found");
            
            // 6. Read subtitles and call Gemini API
            var subtitleContent = await File.ReadAllTextAsync(extractedSubtitlePath);
            var translatedContent = await geminiClient.TranslateSubtitlesAsync(subtitleContent, systemPrompt, temperature, model);

            // 7. Save translated subtitles
            var outputFileName = $"{baseFileName}.{preferredLang}.srt";
            var outputPath = Path.Combine(Path.GetDirectoryName(entry.FilePath)!, outputFileName);
            await File.WriteAllTextAsync(outputPath, translatedContent);

            // 8. Update record
            entry.IsProcessed = true;
            entry.ProcessedAt = DateTime.UtcNow;
            entry.ErrorMessage = null;
            await repository.UpdateAsync(entry);

            // 9. Log API usage
            await apiUsageService.RecordUsageAsync(new ApiUsageDto
            {
                Model = model,
                Date = DateTime.UtcNow
            });

            result.SuccessCount++;
        }
        finally
        {
            // 10. Remove temporary files
            if (File.Exists(extractedSubtitlePath))
            {
                File.Delete(extractedSubtitlePath);
            }
        }
    }
}

