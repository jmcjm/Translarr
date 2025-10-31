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

    public async Task<SubtitleStreamInfo?> FindBestSubtitleStreamAsync(string videoPath)
    {
        var mediaInfo = await FFProbe.AnalyseAsync(videoPath);
        var subtitleStreams = mediaInfo.SubtitleStreams.ToList();

        if (subtitleStreams.Count == 0)
        {
            return null;
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
                s.Language?.ToLowerInvariant().Contains(keyword) ?? false))
            .ToList();

        // If no subtitles without SDH, take first available
        var selectedStream = nonSdhStreams.FirstOrDefault() ?? 
                             (englishStreams.FirstOrDefault() ?? subtitleStreams.First());

        return new SubtitleStreamInfo
        {
            StreamIndex = selectedStream.Index,
            Language = selectedStream.Language ?? "und",
            CodecName = selectedStream.CodecName,
            IsSdh = sdhKeywords.Any(keyword => 
                selectedStream.Language?.ToLowerInvariant().Contains(keyword, StringComparison.InvariantCultureIgnoreCase) ?? false)
        };
    }

    public async Task<bool> ExtractSubtitlesAsync(string videoPath, int streamIndex, string outputPath)
    {
        try
        {
            // FFMpegCore doesn't have direct method for subtitle extraction
            // We need to use FFMpegArguments
            await FFMpegArguments
                .FromFileInput(videoPath)
                .OutputToFile(outputPath, true, options => options
                    .SelectStream(streamIndex)
                    .ForceFormat("srt"))
                .ProcessAsynchronously();

            return File.Exists(outputPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing video: {ex}", ex.Message);
            return false;
        }
    }
}