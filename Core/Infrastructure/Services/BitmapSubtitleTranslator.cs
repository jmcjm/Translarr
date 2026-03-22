using System.Text;
using Microsoft.Extensions.Logging;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Infrastructure.Services;

public class BitmapSubtitleTranslator(
    IFfmpegService ffmpegService,
    ISubtitleTranslator subtitleTranslator,
    ILogger<BitmapSubtitleTranslator> logger) : IBitmapSubtitleTranslator
{
    private const string WorkDir = "/tmp/translarr";

    public async Task<string> TranslateBitmapSubtitlesAsync(
        string videoPath, int streamIndex, LlmSettingsDto settings,
        Action<int, int>? onBatchProgress = null,
        CancellationToken cancellationToken = default)
    {
        var hash = Guid.NewGuid().ToString("N")[..12];
        var supPath = Path.Combine(WorkDir, $"{hash}.sup");
        var framesDir = Path.Combine(WorkDir, $"{hash}_frames");

        try
        {
            Directory.CreateDirectory(WorkDir);

            // 1. Extract .sup
            logger.LogInformation("Extracting PGS stream from {file}", videoPath);
            var extracted = await ffmpegService.ExtractSupAsync(videoPath, streamIndex, supPath);
            if (!extracted)
                throw new InvalidOperationException("Failed to extract PGS subtitle stream");

            // 2. Parse PGS
            logger.LogInformation("Parsing PGS subtitle data");
            var subtitles = PgsParser.Parse(supPath);
            logger.LogInformation("Found {count} subtitle events", subtitles.Count);

            if (subtitles.Count == 0)
                throw new InvalidOperationException("No subtitle events found in PGS stream");

            // 3. Render to PNG
            logger.LogInformation("Rendering subtitle frames to PNG");
            var frames = PgsRenderer.RenderAll(subtitles, framesDir);
            logger.LogInformation("Rendered {count} frames", frames.Count);

            // 4. Batch OCR + translate
            var batchSize = settings.OcrBatchSize > 0 ? settings.OcrBatchSize : 15;
            var srtBuilder = new StringBuilder();
            var globalSubIndex = 1;

            for (var i = 0; i < frames.Count; i += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = frames.Skip(i).Take(batchSize).ToList();
                var batchNum = i / batchSize + 1;
                var totalBatches = (frames.Count + batchSize - 1) / batchSize;
                logger.LogInformation("Processing OCR batch {batchNum}/{totalBatches} ({count} frames)",
                    batchNum, totalBatches, batch.Count);
                onBatchProgress?.Invoke(batchNum, totalBatches);

                var prompt = BuildPrompt(batch, settings);
                var images = batch.Select(f => File.ReadAllBytes(f.FilePath)).ToList();

                var srtFragment = await subtitleTranslator.TranslateWithImagesAsync(prompt, images, settings);

                // Renumber subtitles in fragment
                srtFragment = RenumberSrt(srtFragment, ref globalSubIndex);
                srtBuilder.Append(srtFragment);
                if (!srtFragment.EndsWith('\n')) srtBuilder.AppendLine();
                srtBuilder.AppendLine();
            }

            return srtBuilder.ToString().Trim();
        }
        finally
        {
            // 5. Cleanup
            if (File.Exists(supPath)) File.Delete(supPath);
            if (Directory.Exists(framesDir)) Directory.Delete(framesDir, true);
        }
    }

    private static string BuildPrompt(List<RenderedFrame> batch, LlmSettingsDto settings)
    {
        var prompt = settings.OcrSystemPrompt ?? "";

        var timestampList = new StringBuilder();
        for (var j = 0; j < batch.Count; j++)
        {
            var f = batch[j];
            timestampList.AppendLine($"{j + 1} | {FormatSrt(f.StartTime)} --> {FormatSrt(f.EndTime)}");
        }

        prompt = prompt.Replace("{timestamps}", timestampList.ToString().TrimEnd());
        prompt = prompt.Replace("{preferredLang}", settings.PreferredSubsLang);

        return prompt;
    }

    private static string FormatSrt(TimeSpan ts)
        => $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";

    private static string RenumberSrt(string srt, ref int startIndex)
    {
        var lines = srt.Split('\n');
        var result = new StringBuilder();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (int.TryParse(line, out _) && i + 1 < lines.Length && lines[i + 1].Contains("-->"))
            {
                result.AppendLine(startIndex.ToString());
                startIndex++;
            }
            else
            {
                result.AppendLine(lines[i].TrimEnd());
            }
        }

        return result.ToString();
    }
}
