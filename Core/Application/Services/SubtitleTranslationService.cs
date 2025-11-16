using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Translarr.Core.Application.Abstractions.Repositories;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Services;

public partial class SubtitleTranslationService(
    ISubtitleEntryRepository repository,
    IUnitOfWork unitOfWork,
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
                    await unitOfWork.SaveChangesAsync();
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
            await unitOfWork.SaveChangesAsync();
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
            // Not only is the overall token limit a problem for us, but the output token limit is also an issue, as it maxes out at 65,536.
            // Earlier I set the max input limit based on ASS subtitles, which contained a lot of junk that wasn’t present in the translated subtitles,
            // so the input token count could be much higher than the max output limit.
            // But when translating pure SRT subtitles, if the input token count was higher than the max output limit, Gemini truncated the output once it reached 65,536 tokens (including thinking tokens).
            // So we need to set the max input slightly lower than the max output and split the input into multiple requests if necessary.
            // This will use more API calls (we only have 100 per day on the free tier), but it's still better than ending up with truncated subtitles ;).
            
            // The problem is that we need to split the subtitles into sensible chunks, not in the middle of a subtitle line.
            // We also need to include a few previous lines in the next request, so Gemini has context for the translation,
            // and afterward we need to merge all chunks back together.
            // Maybe we could instead send Gemini the entire conversation history and ask it to continue from the last line,
            // but this is still a WIP.
            var maxSizeBytes = 59000;
            
            // 5. Clean ASS files if needed
            if (codecName.Equals("ass", StringComparison.OrdinalIgnoreCase) ||
                codecName.Equals("ssa", StringComparison.OrdinalIgnoreCase))
            {
                ReportProgress(TranslationStep.CleaningSubtitles);
                logger.LogInformation("Detected ASS/SSA subtitle format, cleaning file before conversion");
                await CleanAssFile(extractedSubtitlePath);
                
                // We still don’t have a good way to translate long subtitle files without risking truncation. However,
                // we know that if the subtitles are in ASS/SSA format, we can safely allow a higher input size
                // because ASS/SSA subtitles convert to much shorter SRT text.
                logger.LogInformation("Detected ASS/SSA subtitle format, increasing the input token limit as they will be much smaller after conversion to SRT by the model");
                maxSizeBytes = 100000;
            }

            // 6. Read subtitles and validate size
            ReportProgress(TranslationStep.ValidatingSize);
            var subtitleContent = await File.ReadAllTextAsync(extractedSubtitlePath);
            
            if (subtitleContent.Length > maxSizeBytes)
            {
                throw new InvalidOperationException(
                    $"Subtitle file too large: {subtitleContent.Length} bytes (max: {maxSizeBytes} bytes). " +
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

            await unitOfWork.SaveChangesAsync();

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
    
    private async Task CleanAssFile(string assFilePath)
    {
        try
        {
            logger.LogInformation("Cleaning ASS file: {file}", assFilePath);

            var content = await File.ReadAllTextAsync(assFilePath);
            var lines = content.Split('\n');
            var cleanedLines = new List<string>();
            var currentSection = "";
            var skipSection = false;

            var sectionsToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "[V4+ Styles]",
                "[V4 Styles]",
                "[Aegisub Project Garbage]",
                "[Aegisub Extradata]",
                "[Fonts]",
                "[Graphics]"
            };

            var styleBlacklist = new[] { "ED", "OP", "Romaji", "Kanji", "FX", "fx", "KFX", "karaoke" };

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.StartsWith('[') && trimmedLine.EndsWith(']'))
                {
                    currentSection = trimmedLine;
                    skipSection = sectionsToRemove.Contains(currentSection);

                    if (!skipSection)
                        cleanedLines.Add(line);

                    continue;
                }

                if (skipSection)
                    continue;

                if (currentSection.Equals("[Events]", StringComparison.OrdinalIgnoreCase))
                {
                    if (trimmedLine.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
                    {
                        // Usuń tagi formatujące
                        var cleanedLine = Regex.Replace(line, @"\{[^}]+\}", string.Empty);

                        // Rozbij po przecinkach (ASS ma 9 pól przed tekstem)
                        var parts = cleanedLine.Split(',', 10);
                        if (parts.Length < 10)
                            continue;

                        var style = parts[3].Trim();
                        var effect = parts[8].Trim();
                        var text = parts[9].Trim();

                        // Pomijaj linie z niechcianymi stylami lub efektami
                        if (styleBlacklist.Any(bad => style.Contains(bad, StringComparison.OrdinalIgnoreCase)) ||
                            styleBlacklist.Any(bad => effect.Contains(bad, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        // Pomijaj linie bez liter (czyli krzaki typu "fx,s" albo puste)
                        if (!Regex.IsMatch(text, @"\p{L}"))
                            continue;

                        cleanedLines.Add(cleanedLine);
                        continue;
                    }

                    if (trimmedLine.StartsWith("Comment:", StringComparison.OrdinalIgnoreCase))
                    {
                        // Pomijamy komentarze
                        continue;
                    }
                }

                cleanedLines.Add(line);
            }

            var cleanedContent = string.Join('\n', cleanedLines);
            await File.WriteAllTextAsync(assFilePath, cleanedContent);

            var originalSize = content.Length;
            var cleanedSize = cleanedContent.Length;
            var reduction = originalSize > 0 ? (1 - (double)cleanedSize / originalSize) * 100 : 0;

            logger.LogInformation("ASS file cleaned. Size reduced from {original}B to {cleaned}B ({reduction:F1}% reduction)", originalSize, cleanedSize, reduction);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error cleaning ASS file: {ex}", ex.Message);
            throw;
        }
    }

    [GeneratedRegex(@"^```[\w-]*\n|```$", RegexOptions.Multiline)]
    private static partial Regex MyRegex();
}