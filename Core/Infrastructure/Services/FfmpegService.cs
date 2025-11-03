using System.Text;
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

    public async Task<bool> ExtractSubtitlesAsync(string videoPath, int streamIndex, string outputPath, string codecName)
    {
        try
        {
            logger.LogInformation("Extracting subtitles for {file} stream {stream} with codec {codec}", videoPath, streamIndex, codecName);

            await FFMpegArguments
                .FromFileInput(videoPath)
                .OutputToFile(outputPath, true, options => options
                    .SelectStream(streamIndex)
                    .ForceFormat(codecName))
                .ProcessAsynchronously();

            return File.Exists(outputPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error extracting subtitles: {ex}", ex.Message);
            return false;
        }
    }

    public async Task CleanAssFile(string assFilePath)
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
                    else if (trimmedLine.StartsWith("Comment:", StringComparison.OrdinalIgnoreCase))
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
}