using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Translarr.Core.Application.Abstractions.Repositories;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Services;

public class MediaScannerService(
    ISubtitleEntryRepository repository,
    ISettingsService settingsService,
    ISeriesWatchService seriesWatchService,
    ILogger<MediaScannerService> logger,
    IConfiguration configuration) : IMediaScannerService
{
    private readonly string _mediaRootPath = configuration.GetValue<string>("MediaRootPath") ?? throw new ArgumentException("MediaRootPath configuration not found");
    private readonly string[] _videoExtensions = [".mkv", ".mp4", ".avi", ".mov", ".m4v", ".webm", ".flv"];

    public async Task<ScanResultDto> ScanLibraryAsync()
    {
        logger.LogInformation("Starting media scan");
        var startTime = DateTime.UtcNow;
        var result = new ScanResultDto();
        var errors = new List<string>();

        try
        {
            var preferredLang = await settingsService.GetSettingAsync("PreferredSubsLang") 
                ?? throw new ArgumentException("PreferredSubsLang setting not found");

            var videoFiles = ScanFilesystemAsync(_mediaRootPath);
            await RemoveMissingEntriesAsync(videoFiles, result, errors);
            await AnalyzeVideoFilesAsync(videoFiles, preferredLang, result, errors);

            result.Errors = errors;
            result.Duration = DateTime.UtcNow - startTime;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Critical error during scan: {msg}", ex.Message);
            errors.Add($"Critical error during scan: {ex.Message}");
            result.Errors = errors;
            result.Duration = DateTime.UtcNow - startTime;
        }

        logger.LogInformation("Media scan complete");
        return result;
    }

    private List<VideoFile> ScanFilesystemAsync(string mediaRootPath)
    {
        logger.LogInformation("Scanning media files");
        var videoFiles = new List<VideoFile>();

        if (!Directory.Exists(mediaRootPath))
        {
            throw new DirectoryNotFoundException($"Media root path does not exist: {mediaRootPath}");
        }

        // Recursively find all video files
        var allFiles = Directory.GetFiles(mediaRootPath, "*.*", SearchOption.AllDirectories)
            .Where(f => _videoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

        foreach (var filePath in allFiles)
        {
            try
            {
                // Get relative path from mediaRootPath
                var relativePath = Path.GetRelativePath(mediaRootPath, filePath);
                var pathParts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                // Assume structure: .../SeriesName/SeasonName/Episode.ext
                // Need at least 3 parts: [Series, Season, File]
                if (pathParts.Length < 3)
                {
                    // If less than 3 levels, use first folder as series
                    var seriesName = pathParts.Length >= 2 ? pathParts[0] : "Unknown";
                    var seasonName = pathParts.Length >= 2 ? pathParts[^2] : "Unknown";

                    videoFiles.Add(new VideoFile
                    {
                        FilePath = filePath,
                        FileName = Path.GetFileName(filePath),
                        SeriesNumber = seriesName,
                        SeasonNumber = seasonName
                    });
                }
                else
                {
                    // For structure with category (e.g. Anime/Series/Season/Episode.ext)
                    // Take second-to-last folder as season, and folder before it as series
                    var seasonName = pathParts[^2]; // Second-to-last element (folder directly above file)
                    var seriesName = pathParts[^3]; // Folder above season

                    videoFiles.Add(new VideoFile
                    {
                        FilePath = filePath,
                        FileName = Path.GetFileName(filePath),
                        SeriesNumber = seriesName,
                        SeasonNumber = seasonName
                    });
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not process file {file}: {msg}", filePath, ex.Message);
            }
        }

        return videoFiles;
    }

    private async Task AnalyzeVideoFilesAsync(List<VideoFile> videoFiles, string preferredLang, ScanResultDto result, List<string> errors)
    {
        logger.LogInformation("Analyzing video files");
        foreach (var videoFile in videoFiles)
        {
            try
            {
                var existingEntry = await repository.GetByFilePathAsync(videoFile.FilePath);
                var subtitleFileName = $"{Path.GetFileNameWithoutExtension(videoFile.FileName)}.{preferredLang}.srt";
                var subtitlePath = Path.Combine(Path.GetDirectoryName(videoFile.FilePath)!, subtitleFileName);
                var alreadyHas = File.Exists(subtitlePath);

                if (existingEntry == null)
                {
                    // Check if series/season is watched for auto-marking
                    var shouldAutoMarkWanted = await seriesWatchService.ShouldAutoMarkWantedAsync(
                        videoFile.SeriesNumber,
                        videoFile.SeasonNumber);

                    // New file
                    var newEntry = new SubtitleEntryDto
                    {
                        FilePath = videoFile.FilePath,
                        FileName = videoFile.FileName,
                        Series = videoFile.SeriesNumber,
                        Season = videoFile.SeasonNumber,
                        IsProcessed = false,
                        IsWanted = shouldAutoMarkWanted,
                        ForceProcess = false,
                        AlreadyHad = alreadyHas,
                        LastScanned = DateTime.UtcNow
                    };

                    await repository.AddAsync(newEntry);
                    result.NewFiles++;
                }
                else
                {
                    // Update existing
                    var hadSubtitles = existingEntry.AlreadyHad;
                    existingEntry.AlreadyHad = alreadyHas;
                    existingEntry.LastScanned = DateTime.UtcNow;
                    
                    // If subtitles disappeared, reset processing status
                    if (hadSubtitles && !alreadyHas)
                    {
                        existingEntry.IsProcessed = false;
                        existingEntry.ErrorMessage = null;
                    }
                    
                    await repository.UpdateAsync(existingEntry);
                    result.UpdatedFiles++;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing {file}: {msg}", videoFile.FileName, ex.Message);
                errors.Add($"Error processing {videoFile.FileName}: {ex.Message}");
                result.ErrorFiles++;
            }
        }
    }

    private async Task RemoveMissingEntriesAsync(List<VideoFile> videoFiles, ScanResultDto result, List<string> errors)
    {
        logger.LogInformation("Removing stale entries");
        try
        {
            var existingEntries = await repository.GetAllAsync();

            if (existingEntries.Count == 0)
                return;

            var existingFilePaths = videoFiles
                .Select(v => v.FilePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var entriesToRemove = existingEntries
                .Where(entry => !existingFilePaths.Contains(entry.FilePath))
                .ToList();

            if (entriesToRemove.Count == 0)
                return;

            var removedCount = await repository.DeleteByIdsAsync(entriesToRemove.Select(e => e.Id));
            result.RemovedFiles += removedCount;

            if (removedCount < entriesToRemove.Count)
            {
                logger.LogWarning("Some database entries could not be removed during cleanup.");
                errors.Add("Some database entries could not be removed during cleanup.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove stale entries: {msg}", ex.Message);
            errors.Add($"Failed to remove stale entries: {ex.Message}");
        }
    }
}

