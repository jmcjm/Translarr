using Microsoft.Extensions.Configuration;
using Translarr.Core.Application.Abstractions.Repositories;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Services;

public class MediaScannerService : IMediaScannerService
{
    private readonly ISubtitleEntryRepository _repository;
    private readonly ISettingsService _settingsService;
    private readonly string _mediaRootPath;
    private readonly string[] _videoExtensions = [".mkv", ".mp4", ".avi", ".mov", ".m4v", ".webm", ".flv"];

    public MediaScannerService(
        ISubtitleEntryRepository repository, 
        ISettingsService settingsService,
        IConfiguration configuration)
    {
        _repository = repository;
        _settingsService = settingsService;
        _mediaRootPath = configuration.GetValue<string>("MediaRootPath") 
            ?? throw new ArgumentException("MediaRootPath configuration not found");
    }

    public async Task<ScanResultDto> ScanLibraryAsync()
    {
        var startTime = DateTime.UtcNow;
        var result = new ScanResultDto();
        var errors = new List<string>();

        try
        {
            var preferredLang = await _settingsService.GetSettingAsync("PreferredSubsLang") 
                ?? throw new ArgumentException("PreferredSubsLang setting not found");

            var videoFiles = ScanFilesystemAsync(_mediaRootPath);
            await AnalyzeVideoFilesAsync(videoFiles, preferredLang, result, errors);

            result.Errors = errors;
            result.Duration = DateTime.UtcNow - startTime;
        }
        catch (Exception ex)
        {
            errors.Add($"Critical error during scan: {ex.Message}");
            result.Errors = errors;
            result.Duration = DateTime.UtcNow - startTime;
        }

        return result;
    }

    private List<VideoFile> ScanFilesystemAsync(string mediaRootPath)
    {
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
                // Skip files that cause errors
                Console.WriteLine($"Warning: Could not process file {filePath}: {ex.Message}");
            }
        }

        return videoFiles;
    }

    private async Task AnalyzeVideoFilesAsync(List<VideoFile> videoFiles, string preferredLang, ScanResultDto result, List<string> errors)
    {
        foreach (var videoFile in videoFiles)
        {
            try
            {
                var existingEntry = await _repository.GetByFilePathAsync(videoFile.FilePath);
                var subtitleFileName = $"{Path.GetFileNameWithoutExtension(videoFile.FileName)}.{preferredLang}.srt";
                var subtitlePath = Path.Combine(Path.GetDirectoryName(videoFile.FilePath)!, subtitleFileName);
                var alreadyHas = File.Exists(subtitlePath);

                if (existingEntry == null)
                {
                    // New file
                    var newEntry = new SubtitleEntryDto
                    {
                        FilePath = videoFile.FilePath,
                        FileName = videoFile.FileName,
                        Series = videoFile.SeriesNumber,
                        Season = videoFile.SeasonNumber,
                        IsProcessed = false,
                        IsWanted = false,
                        AlreadyHas = alreadyHas,
                        LastScanned = DateTime.UtcNow
                    };

                    await _repository.AddAsync(newEntry);
                    result.NewFiles++;
                }
                else
                {
                    // Update existing
                    var hadSubtitles = existingEntry.AlreadyHas;
                    existingEntry.AlreadyHas = alreadyHas;
                    existingEntry.LastScanned = DateTime.UtcNow;
                    
                    // If subtitles disappeared, reset processing status
                    if (hadSubtitles && !alreadyHas)
                    {
                        existingEntry.IsProcessed = false;
                        existingEntry.ErrorMessage = null;
                    }
                    
                    await _repository.UpdateAsync(existingEntry);
                    result.UpdatedFiles++;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Error processing {videoFile.FileName}: {ex.Message}");
                result.ErrorFiles++;
            }
        }
    }
}

