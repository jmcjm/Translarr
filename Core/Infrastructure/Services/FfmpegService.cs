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
        logger.LogInformation("Finding best subtitle stream for {file}", videoPath);
        var mediaInfo = await FFProbe.AnalyseAsync(videoPath);
        var subtitleStreams = mediaInfo.SubtitleStreams.ToList();

        if (subtitleStreams.Count == 0)
        {
            logger.LogWarning("No subtitle streams found for {file}", videoPath);
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
            logger.LogInformation("Extracting subtitles for {file} stream {stream}", videoPath, streamIndex);
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