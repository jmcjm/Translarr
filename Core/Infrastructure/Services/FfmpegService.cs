using System.Text.RegularExpressions;
using FFMpegCore;
using Microsoft.Extensions.Logging;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Infrastructure.Services;

public class FfmpegService(ILogger<FfmpegService> logger) : IFfmpegService
{
    public async Task<object> GetVideoStreamsAsync(string videoPath)
    {
        var mediaInfo = await FFProbe.AnalyseAsync(videoPath);
        
        return new
        {
            VideoStreams = mediaInfo.VideoStreams.Select(v => new
            {
                v.Index,
                v.CodecName,
                v.Width,
                v.Height,
                v.Duration
            }),
            AudioStreams = mediaInfo.AudioStreams.Select(a => new
            {
                a.Index,
                a.CodecName,
                a.Language,
                a.Channels
            }),
            SubtitleStreams = mediaInfo.SubtitleStreams.Select(s => new
            {
                s.Index,
                s.CodecName,
                s.Language
            })
        };
    }

    private static readonly HashSet<string> BitmapCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "hdmv_pgs_subtitle", "pgssub", "dvd_subtitle", "dvdsub", "xsub"
    };

    public async Task<SubtitleSearchResult> FindBestSubtitleStreamAsync(string videoPath)
    {
        logger.LogInformation("Finding best subtitle stream for {file}", videoPath);
        var mediaInfo = await FFProbe.AnalyseAsync(videoPath);
        var allSubtitleStreams = mediaInfo.SubtitleStreams.ToList();

        if (allSubtitleStreams.Count == 0)
        {
            logger.LogWarning("No subtitle streams found for {file}", videoPath);
            return new SubtitleSearchResult(null, "No embedded subtitle streams found");
        }

        // Filter out bitmap-based subtitles (PGS, VobSub, XSUB) - these can't be extracted as text without OCR
        var subtitleStreams = allSubtitleStreams
            .Where(s => !BitmapCodecs.Contains(s.CodecName ?? ""))
            .ToList();

        if (subtitleStreams.Count == 0)
        {
            var codecs = string.Join(", ", allSubtitleStreams.Select(s => s.CodecName).Distinct());
            logger.LogWarning(
                "File {file} has {count} subtitle stream(s), but all are bitmap-based ({codecs}) which cannot be extracted as text. OCR would be required",
                videoPath, allSubtitleStreams.Count, codecs);
            return new SubtitleSearchResult(null,
                $"All {allSubtitleStreams.Count} subtitle stream(s) are bitmap-based ({codecs}) and cannot be extracted as text. OCR support is not yet available");
        }

        // Prefer English subtitles
        var englishStreams = subtitleStreams
            .Where(s => s.Language?.ToLowerInvariant() == "eng" || 
                        s.Language?.ToLowerInvariant() == "en")
            .ToList();

        // Filter SDH (Subtitles for Deaf and Hard of hearing)
        var sdhKeywords = new[] { "sdh", "hearing impaired", "descriptive", "cc", "closed caption" };
        
        var nonSdhStreams = (englishStreams.Count > 0 ? englishStreams : subtitleStreams)
            .Where(s => !sdhKeywords.Any(keyword => 
                s.Language?.ToLowerInvariant().Contains(keyword, StringComparison.InvariantCultureIgnoreCase) ?? false))
            .ToList();

        // First we want to prioritize streams with Dialog in title so we won't take any Songs and Signs or other non-dialog subtitles by mistake
        var dialogSubtitles = nonSdhStreams.Where(t => t.Tags?.Values.Any(v => v.Contains("Dialog", StringComparison.OrdinalIgnoreCase)) ?? false).ToList();
        
        SubtitleStream selectedStream;
        
        if (dialogSubtitles.Count > 0)
        {
            // Prefer dialog subtitles
            selectedStream = dialogSubtitles.First();
        }
        else
        {
            // If no dialog subtitles, filter out known non-dialog subtitles and take first available
            var knownNonDialogTags = new[] { "S&S", "Honorifics", "Signs", "Songs" };
            var dialogOrUnknown = nonSdhStreams
                .Where(t => !knownNonDialogTags.Any(tag => t.Tags?.Values.Any(v => v.Contains(tag, StringComparison.OrdinalIgnoreCase)) ?? false))
                .ToList();
            
            selectedStream = dialogOrUnknown.FirstOrDefault() ?? 
                             nonSdhStreams.FirstOrDefault() ?? 
                             (englishStreams.FirstOrDefault() ?? subtitleStreams.First())!;
        }

        return new SubtitleSearchResult(new SubtitleStreamInfo
        {
            StreamIndex = selectedStream.Index,
            Language = selectedStream.Language ?? "und",
            CodecName = selectedStream.CodecName,
            IsSdh = sdhKeywords.Any(keyword =>
                selectedStream.Language?.ToLowerInvariant().Contains(keyword, StringComparison.InvariantCultureIgnoreCase) ?? false)
        });
    }

    public async Task<bool> ExtractSubtitlesAsync(string videoPath, int streamIndex, string outputPath, string codecName)
    {
        try
        {
            logger.LogInformation("Extracting subtitles for {file} stream {stream} with codec {codec}", videoPath, streamIndex, codecName);

            // Map codec name to ffmpeg output format
            var outputFormat = codecName.ToLowerInvariant() switch
            {
                "subrip" => "srt",
                "ass" => "ass",
                "ssa" => "ass",
                "mov_text" => "srt",
                "webvtt" => "webvtt",
                // Bitmap codecs (dvd_subtitle, dvdsub, hdmv_pgs_subtitle) are filtered out in FindBestSubtitleStreamAsync
                _ => "srt" // Default fallback to SRT
            };

            logger.LogInformation("Using output format: {format}", outputFormat);

            await FFMpegArguments
                .FromFileInput(videoPath)
                .OutputToFile(outputPath, true, options => options
                    .SelectStream(streamIndex)
                    .ForceFormat(outputFormat))
                .ProcessAsynchronously();

            return File.Exists(outputPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error extracting subtitles: {ex}", ex.Message);
            return false;
        }
    }

    public async Task<bool> ConvertToSrt(string inputPath, string outputPath)
    {
        try
        {
            logger.LogInformation("Converting {input} to SRT format at {output}", inputPath, outputPath);

            await FFMpegArguments
                .FromFileInput(inputPath)
                .OutputToFile(outputPath, true, options => options
                    .ForceFormat("srt"))
                .ProcessAsynchronously();

            return File.Exists(outputPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error converting to SRT: {ex}", ex.Message);
            return false;
        }
    }

    public async Task<bool> ExtractSupAsync(string videoPath, int streamIndex, string outputPath)
    {
        try
        {
            logger.LogInformation("Extracting PGS subtitle stream {stream} from {file}", streamIndex, videoPath);

            await FFMpegArguments
                .FromFileInput(videoPath)
                .OutputToFile(outputPath, true, options => options
                    .SelectStream(streamIndex)
                    .ForceFormat("sup"))
                .ProcessAsynchronously();

            return File.Exists(outputPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error extracting PGS subtitles: {msg}", ex.Message);
            return false;
        }
    }

    public async Task<SubtitleStreamInfo?> FindBestBitmapSubtitleStreamAsync(string videoPath)
    {
        logger.LogInformation("Finding best bitmap subtitle stream for {file}", videoPath);
        var mediaInfo = await FFProbe.AnalyseAsync(videoPath);
        var allSubtitleStreams = mediaInfo.SubtitleStreams.ToList();

        // Filter FOR bitmap codecs (opposite of FindBestSubtitleStreamAsync)
        var bitmapStreams = allSubtitleStreams
            .Where(s => BitmapCodecs.Contains(s.CodecName ?? ""))
            .ToList();

        if (bitmapStreams.Count == 0)
        {
            logger.LogWarning("No bitmap subtitle streams found for {file}", videoPath);
            return null;
        }

        // Prefer English
        var englishStreams = bitmapStreams
            .Where(s => s.Language?.ToLowerInvariant() is "eng" or "en")
            .ToList();

        var selectedStream = englishStreams.FirstOrDefault() ?? bitmapStreams.First();

        return new SubtitleStreamInfo
        {
            StreamIndex = selectedStream.Index,
            Language = selectedStream.Language ?? "und",
            CodecName = selectedStream.CodecName,
            IsSdh = false
        };
    }
}