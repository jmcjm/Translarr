# Bitmap Subtitle OCR Translation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a separate pipeline that extracts text from bitmap subtitles (PGS) via LLM vision OCR and translates them in a single step.

**Architecture:** PGS .sup files are extracted via FFmpeg, parsed into PNG frames with timestamps, batched and sent to the LLM with a prompt containing timestamps. LLM returns translated SRT fragments which are assembled into the final file. Separate from text translation pipeline - user triggers it explicitly from the dashboard.

**Tech Stack:** SixLabors.ImageSharp, OpenAI SDK (existing), FFmpeg (existing), EF Core migrations

**Spec:** `docs/superpowers/specs/2026-03-22-bitmap-ocr-design.md`
**POC reference:** `sub-poc/PgsExtract.cs` (PGS parser + renderer), `sub-poc/PgsOcrOpenAi.cs` (OCR via OpenAI SDK)

---

### Task 1: Add ImageSharp package + DB migration

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `Core/Infrastructure/Infrastructure.csproj`
- Modify: `Core/Infrastructure/Persistence/Daos/SubtitleEntryDao.cs`
- Modify: `Core/Infrastructure/Persistence/Configurations/SubtitleEntryConfiguration.cs`
- Modify: `Core/Application/Models/SubtitleEntryDto.cs`
- Create: new EF Core migration

- [ ] **Step 1: Add ImageSharp to Directory.Packages.props**

Add to the Media Processing section:
```xml
  <ItemGroup Label="Media Processing">
    <PackageVersion Include="FFMpegCore" Version="5.4.0" />
    <PackageVersion Include="SixLabors.ImageSharp" Version="3.1.7" />
  </ItemGroup>
```

Add to `Core/Infrastructure/Infrastructure.csproj`:
```xml
<PackageReference Include="SixLabors.ImageSharp" />
```

- [ ] **Step 2: Add HasBitmapSubtitlesOnly to DAO and DTO**

In `Core/Infrastructure/Persistence/Daos/SubtitleEntryDao.cs`, add property:
```csharp
public bool HasBitmapSubtitlesOnly { get; set; }
```

In `Core/Infrastructure/Persistence/Configurations/SubtitleEntryConfiguration.cs`, add configuration:
```csharp
builder.Property(e => e.HasBitmapSubtitlesOnly).HasDefaultValue(false);
```

In `Core/Application/Models/SubtitleEntryDto.cs`, add property:
```csharp
public bool HasBitmapSubtitlesOnly { get; set; }
```

- [ ] **Step 3: Create EF Core migration**

```bash
cd Core/Infrastructure
dotnet ef migrations add AddHasBitmapSubtitlesOnly --startup-project ../Api
```

- [ ] **Step 4: Verify build**

```bash
dotnet build --configuration Release
```

- [ ] **Step 5: Commit**

```
git commit -m "Add ImageSharp package and HasBitmapSubtitlesOnly DB column"
```

---

### Task 2: PGS Parser + Renderer

**Files:**
- Create: `Core/Infrastructure/Services/PgsParser.cs`
- Create: `Core/Infrastructure/Services/PgsRenderer.cs`

- [ ] **Step 1: Create PgsParser**

Port from `sub-poc/PgsExtract.cs`. Create `Core/Infrastructure/Services/PgsParser.cs`:

```csharp
using SixLabors.ImageSharp.PixelFormats;

namespace Translarr.Core.Infrastructure.Services;

public class PgsSubtitle
{
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public byte[]? Bitmap { get; set; }
    public Rgba32[]? Palette { get; set; }
}

public static class PgsParser
{
    // Port segment type constants, Parse method, ParsePcs, ParsePds, ParseOds,
    // ReadUInt32BE, ReadUInt16BE from sub-poc/PgsExtract.cs
    // The full implementation is in the POC file - copy it verbatim,
    // changing class to static, namespace to Translarr.Core.Infrastructure.Services
}
```

Copy the full parser logic from the POC. Key parts:
- Segment types: PCS (0x16), PDS (0x14), ODS (0x15), END (0x80)
- PCS: detect show/clear events, pair for start/end times
- PDS: parse YCbCr palette entries to Rgba32
- ODS: accumulate RLE data with width/height
- Index 0xFF is always transparent by spec
- Filter out empty subtitles (clear events)

- [ ] **Step 2: Create PgsRenderer**

Create `Core/Infrastructure/Services/PgsRenderer.cs`:

```csharp
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Translarr.Core.Infrastructure.Services;

public record RenderedFrame(string FilePath, TimeSpan StartTime, TimeSpan EndTime, int Index);

public static class PgsRenderer
{
    public static List<RenderedFrame> RenderAll(List<PgsSubtitle> subtitles, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var frames = new List<RenderedFrame>();
        var index = 0;

        foreach (var sub in subtitles)
        {
            if (sub.Bitmap == null || sub.Palette == null) continue;

            index++;
            var startStr = FormatTimestamp(sub.StartTime);
            var endStr = FormatTimestamp(sub.EndTime);
            var filename = $"{index:D4}_{startStr}_{endStr}.png";
            var outPath = Path.Combine(outputDir, filename);

            using var image = RleToBitmap(sub);
            image.SaveAsPng(outPath);

            frames.Add(new RenderedFrame(outPath, sub.StartTime, sub.EndTime, index));
        }

        return frames;
    }

    // Port FormatTimestamp, RleToBitmap, DecodeRle from sub-poc/PgsExtract.cs
    // FormatTimestamp: HHMMSSMMM format
    // RleToBitmap: creates Image<Rgba32> with black background, decodes RLE pixels
    // DecodeRle: handles all RLE cases (single pixel, short/long transparent, short/long colored, end-of-line)
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build Core/Infrastructure/Infrastructure.csproj
```

- [ ] **Step 4: Commit**

```
git commit -m "Add PGS parser and renderer (ported from POC)"
```

---

### Task 3: Extend ISubtitleTranslator with image support

**Files:**
- Modify: `Core/Application/Abstractions/Services/ISubtitleTranslator.cs`
- Modify: `Core/Infrastructure/Services/OpenAiSubtitleTranslator.cs`

- [ ] **Step 1: Add TranslateWithImagesAsync to interface**

In `Core/Application/Abstractions/Services/ISubtitleTranslator.cs`, add:
```csharp
Task<string> TranslateWithImagesAsync(string prompt, List<byte[]> images, LlmSettingsDto settings);
```

- [ ] **Step 2: Implement in OpenAiSubtitleTranslator**

In `Core/Infrastructure/Services/OpenAiSubtitleTranslator.cs`, add method:

```csharp
public async Task<string> TranslateWithImagesAsync(string prompt, List<byte[]> images, LlmSettingsDto settings)
{
    if (string.IsNullOrEmpty(settings.ApiKey))
        throw new InvalidOperationException("LLM API key is not configured");

    var client = new OpenAIClient(
        new ApiKeyCredential(settings.ApiKey),
        new OpenAIClientOptions { Endpoint = new Uri(settings.BaseUrl) });

    var chatClient = client.GetChatClient(settings.Model);

    logger.LogInformation("Sending OCR request with {count} images to {model}", images.Count, settings.Model);

    var parts = new List<ChatMessageContentPart>();
    parts.Add(ChatMessageContentPart.CreateTextPart(prompt));

    foreach (var imageBytes in images)
    {
        var binaryData = BinaryData.FromBytes(imageBytes);
        parts.Add(ChatMessageContentPart.CreateImagePart(binaryData, "image/png"));
    }

    var response = await chatClient.CompleteChatAsync(
        [new UserChatMessage(parts)],
        new ChatCompletionOptions { Temperature = settings.Temperature, MaxOutputTokenCount = settings.MaxOutputTokens });

    if (response.Value.FinishReason == ChatFinishReason.ContentFilter)
    {
        throw new InvalidOperationException("LLM blocked this request due to content filtering policy.");
    }

    var text = response.Value.Content[0].Text;

    if (string.IsNullOrWhiteSpace(text))
    {
        throw new InvalidOperationException("LLM returned empty response");
    }

    return MarkdownCodeBlockRegex().Replace(text, "").Trim();
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build Core/Infrastructure/Infrastructure.csproj
```

- [ ] **Step 4: Commit**

```
git commit -m "Add TranslateWithImagesAsync to ISubtitleTranslator for OCR"
```

---

### Task 4: FfmpegService extensions

**Files:**
- Modify: `Core/Application/Abstractions/Services/IFfmpegService.cs`
- Modify: `Core/Infrastructure/Services/FfmpegService.cs`

- [ ] **Step 1: Add new methods to IFfmpegService**

```csharp
Task<bool> ExtractSupAsync(string videoPath, int streamIndex, string outputPath);
Task<SubtitleStreamInfo?> FindBestBitmapSubtitleStreamAsync(string videoPath);
```

- [ ] **Step 2: Implement ExtractSupAsync**

In `FfmpegService.cs`:
```csharp
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
        logger.LogError(ex, "Error extracting PGS subtitles: {ex}", ex.Message);
        return false;
    }
}
```

- [ ] **Step 3: Implement FindBestBitmapSubtitleStreamAsync**

Same logic as `FindBestSubtitleStreamAsync` but filters FOR bitmap codecs instead of against them:

```csharp
public async Task<SubtitleStreamInfo?> FindBestBitmapSubtitleStreamAsync(string videoPath)
{
    var mediaInfo = await FFProbe.AnalyseAsync(videoPath);
    var allSubtitleStreams = mediaInfo.SubtitleStreams.ToList();

    var bitmapStreams = allSubtitleStreams
        .Where(s => BitmapCodecs.Contains(s.CodecName ?? ""))
        .ToList();

    if (bitmapStreams.Count == 0) return null;

    // Same priority logic: prefer English, non-SDH, dialog
    // (reuse/extract common logic from FindBestSubtitleStreamAsync)
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
```

- [ ] **Step 4: Verify build**

```bash
dotnet build --configuration Release
```

- [ ] **Step 5: Commit**

```
git commit -m "Add ExtractSupAsync and FindBestBitmapSubtitleStreamAsync to FfmpegService"
```

---

### Task 5: BitmapSubtitleTranslator + IBitmapSubtitleTranslator

**Files:**
- Create: `Core/Application/Abstractions/Services/IBitmapSubtitleTranslator.cs`
- Create: `Core/Infrastructure/Services/BitmapSubtitleTranslator.cs`
- Modify: `Core/Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Create interface**

Create `Core/Application/Abstractions/Services/IBitmapSubtitleTranslator.cs`:
```csharp
using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Abstractions.Services;

public interface IBitmapSubtitleTranslator
{
    Task<string> TranslateBitmapSubtitlesAsync(
        string videoPath, int streamIndex, LlmSettingsDto settings);
}
```

- [ ] **Step 2: Create BitmapSubtitleTranslator**

Create `Core/Infrastructure/Services/BitmapSubtitleTranslator.cs`:

```csharp
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
        string videoPath, int streamIndex, LlmSettingsDto settings)
    {
        var hash = Path.GetFileNameWithoutExtension(videoPath).GetHashCode().ToString("X8");
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
                var batch = frames.Skip(i).Take(batchSize).ToList();
                logger.LogInformation("Processing OCR batch {batchNum}/{totalBatches} ({count} frames)",
                    i / batchSize + 1, (frames.Count + batchSize - 1) / batchSize, batch.Count);

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

        // Build timestamp list
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
        // Simple renumbering: find lines that are just a number followed by timestamp line
        var lines = srt.Split('\n');
        var result = new StringBuilder();
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i].Trim();
            // Check if this is a subtitle number (just digits, followed by a timestamp line)
            if (int.TryParse(line, out _) && i + 1 < lines.Length && lines[i + 1].Contains("-->"))
            {
                result.AppendLine(startIndex.ToString());
                startIndex++;
            }
            else
            {
                result.AppendLine(lines[i].TrimEnd());
            }
            i++;
        }

        return result.ToString();
    }
}
```

- [ ] **Step 3: Register in DI**

In `Core/Infrastructure/DependencyInjection.cs`, add:
```csharp
services.AddScoped<IBitmapSubtitleTranslator, BitmapSubtitleTranslator>();
```

- [ ] **Step 4: Verify build**

```bash
dotnet build --configuration Release
```

- [ ] **Step 5: Commit**

```
git commit -m "Add BitmapSubtitleTranslator: PGS extract → parse → render → batch OCR → SRT"
```

---

### Task 6: Update scan logic + settings + BitmapTranslationService

**Files:**
- Modify: `Core/Infrastructure/Services/FfmpegService.cs` (FindBestSubtitleStreamAsync)
- Modify: `Core/Application/Services/SubtitleTranslationService.cs`
- Modify: `Core/Application/Models/LlmSettingsDto.cs`
- Modify: `Core/Application/Services/SettingsService.cs`
- Modify: `Core/Infrastructure/Services/TranslarrDatabaseInitializer.cs`
- Create: `Core/Application/Services/BitmapTranslationService.cs`
- Create: `Core/Application/Abstractions/Services/IBitmapTranslationService.cs`

- [ ] **Step 1: Change scan logic for bitmap-only files**

In `Core/Application/Services/SubtitleTranslationService.cs`, in `ProcessEntryAsync`, change the bitmap-only handling. Where it currently sets `IsProcessed = true` and skips, instead:

```csharp
if (searchResult.Stream == null)
{
    // Check if file has bitmap subtitles
    var bitmapStream = await ffmpegService.FindBestBitmapSubtitleStreamAsync(entry.FilePath);
    if (bitmapStream != null)
    {
        // Mark for bitmap OCR pipeline instead of skipping
        entry.HasBitmapSubtitlesOnly = true;
        entry.IsProcessed = false;
        entry.ErrorMessage = null;
        await repository.UpdateAsync(entry);
        await unitOfWork.SaveChangesAsync();
        result.SkippedNoSubtitles++;
        return;
    }

    // No subtitles at all
    entry.IsProcessed = true;
    entry.ProcessedAt = DateTime.UtcNow;
    entry.ErrorMessage = searchResult.SkipReason ?? "No suitable embedded subtitles found";
    await repository.UpdateAsync(entry);
    await unitOfWork.SaveChangesAsync();
    result.SkippedNoSubtitles++;
    return;
}
```

- [ ] **Step 2: Add OCR fields to LlmSettingsDto**

In `Core/Application/Models/LlmSettingsDto.cs`, add:
```csharp
public int OcrBatchSize { get; init; } = 15;
public string OcrSystemPrompt { get; init; } = "";
```

- [ ] **Step 3: Update SettingsService.GetLlmSettingsAsync**

Read new settings in `Core/Application/Services/SettingsService.cs`:
```csharp
var ocrBatchSizeStr = await GetSettingAsync("OcrBatchSize");
var ocrBatchSize = int.TryParse(ocrBatchSizeStr, out var obs) ? obs : 15;

var ocrSystemPrompt = await GetSettingAsync("OcrSystemPrompt") ?? "";
```

Add to returned DTO:
```csharp
OcrBatchSize = ocrBatchSize,
OcrSystemPrompt = ocrSystemPrompt,
```

- [ ] **Step 4: Add defaults to TranslarrDatabaseInitializer**

In the `defaultSettings` list, add:
```csharp
("OcrBatchSize", "15", "Number of subtitle frames per OCR API request"),
("OcrSystemPrompt",
    "### You are an OCR and subtitle translator (English → {preferredLang}).\n" +
    "You receive numbered images containing embedded subtitles along with their exact timestamps.\n\n" +
    "{timestamps}\n\n" +
    "For each image:\n" +
    "1. Extract the text from the image\n" +
    "2. Translate it to {preferredLang}\n" +
    "3. Use EXACTLY the provided timestamps\n\n" +
    "Rules:\n" +
    "- Output **MUST** always be in valid **SRT** format\n" +
    "- **Remove** any formatting tags\n" +
    "- Number subtitles sequentially starting from 1\n" +
    "- Preserve line breaks within subtitles\n" +
    "- Return ONLY the SRT subtitles, nothing else",
    "System prompt template for bitmap OCR+translation. Use {timestamps} and {preferredLang} placeholders."),
```

- [ ] **Step 5: Create IBitmapTranslationService + BitmapTranslationService**

Create `Core/Application/Abstractions/Services/IBitmapTranslationService.cs`:
```csharp
using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Abstractions.Services;

public interface IBitmapTranslationService
{
    Task<TranslationResultDto> TranslateBitmapBatchAsync(
        int batchSize = 100,
        Action<TranslationProgressUpdate>? onProgressUpdate = null,
        CancellationToken cancellationToken = default);
}
```

Create `Core/Application/Services/BitmapTranslationService.cs`:
Similar to `SubtitleTranslationService` but filters `IsWanted && HasBitmapSubtitlesOnly && !IsProcessed`. For each entry: find bitmap stream, call `IBitmapSubtitleTranslator.TranslateBitmapSubtitlesAsync`, save SRT, mark processed.

Add to repository: method to get unprocessed bitmap entries (or filter in service).

Register in DI: `services.AddScoped<IBitmapTranslationService, BitmapTranslationService>();`

- [ ] **Step 6: Verify build**

```bash
dotnet build --configuration Release
```

- [ ] **Step 7: Commit**

```
git commit -m "Add bitmap scan logic, OCR settings, BitmapTranslationService"
```

---

### Task 7: API endpoints for bitmap translation

**Files:**
- Modify: `Core/Api/Endpoints/TranslationEndpoints.cs`
- Modify: `Core/Api/Endpoints/StatsEndpoints.cs`
- Modify: `Core/Api/Models/TranslationStatus.cs` (or create separate bitmap status)

- [ ] **Step 1: Add bitmap translation endpoints**

In `TranslationEndpoints.cs`, add new endpoints:

```csharp
group.MapPost("/translate-bitmap", StartBitmapTranslation)
    .WithName("StartBitmapTranslation")
    .Produces<TranslationResultDto>()
    .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

group.MapGet("/bitmap-status", GetBitmapTranslationStatus)
    .WithName("GetBitmapTranslationStatus")
    .Produces<TranslationStatus>();

group.MapPost("/cancel-bitmap", CancelBitmapTranslation)
    .WithName("CancelBitmapTranslation")
    .Produces(StatusCodes.Status200OK);
```

Implementation: same pattern as existing `StartTranslation` - separate static fields for bitmap status/lock/CTS, fire-and-forget Task.Run, progress callback.

- [ ] **Step 2: Add bitmap count to stats**

In `StatsEndpoints.cs`, add `BitmapFiles` to library stats query.

In the stats model (likely `LibraryStats` or similar), add:
```csharp
public int BitmapFiles { get; set; }
```

Query: count entries where `HasBitmapSubtitlesOnly && IsWanted && !IsProcessed`.

- [ ] **Step 3: Verify build**

```bash
dotnet build --configuration Release
```

- [ ] **Step 4: Commit**

```
git commit -m "Add bitmap translation API endpoints and stats"
```

---

### Task 8: Frontend - dashboard + settings

**Files:**
- Modify: `Frontend/HavitWebApp/Components/Pages/Home.razor`
- Modify: `Frontend/HavitWebApp/Components/Pages/Settings.razor`
- Modify: `Frontend/HavitWebApp/Services/TranslationApiService.cs`

- [ ] **Step 1: Add bitmap API methods to TranslationApiService**

```csharp
public async Task<bool> StartBitmapTranslationAsync(int batchSize = 100)
{
    var client = apiClientFactory.CreateClient();
    var response = await client.PostAsync($"/api/translation/translate-bitmap?batchSize={batchSize}", null);
    return response.IsSuccessStatusCode;
}

public async Task<bool> CancelBitmapTranslationAsync()
{
    var client = apiClientFactory.CreateClient();
    var response = await client.PostAsync("/api/translation/cancel-bitmap", null);
    return response.IsSuccessStatusCode;
}

public async Task<TranslationStatus?> GetBitmapTranslationStatusAsync()
{
    var client = apiClientFactory.CreateClient();
    var response = await client.GetAsync("/api/translation/bitmap-status");
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<TranslationStatus>();
}
```

- [ ] **Step 2: Add bitmap stat card + button to Home.razor**

New stat card showing `_stats.BitmapFiles` count.

New button "Start Bitmap OCR Translation" / "Stop Bitmap OCR Translation" with same toggle pattern as existing translation button.

Add bitmap status polling to the existing timer. Add bitmap progress card.

- [ ] **Step 3: Add OCR settings to Settings.razor**

New card "Bitmap OCR Settings" in right column:

```razor
<HxCard CssClass="mb-4">
    <HeaderTemplate>
        <h5 class="mb-0">Bitmap OCR Settings</h5>
    </HeaderTemplate>
    <BodyTemplate>
        <div class="mb-3">
            <label class="form-label">OCR Batch Size</label>
            <input type="number" class="form-control" min="1" max="50" @bind="_ocrBatchSize" />
            <div class="form-text">Number of subtitle frames sent per API request (default: 15)</div>
        </div>
        <div class="mb-3">
            <label class="form-label">OCR System Prompt</label>
            <textarea class="form-control" rows="8" @bind="_ocrSystemPrompt"></textarea>
            <div class="form-text">
                Use {timestamps} for the timestamp list and {preferredLang} for the target language.
            </div>
        </div>
    </BodyTemplate>
</HxCard>
```

Add `_ocrBatchSize` and `_ocrSystemPrompt` fields, load/save in `LoadSettings`/`SaveAllSettings` with keys `OcrBatchSize` and `OcrSystemPrompt`.

- [ ] **Step 4: Verify build**

```bash
dotnet build --configuration Release
```

- [ ] **Step 5: Commit**

```
git commit -m "Add bitmap OCR UI: dashboard button/stats, settings page OCR config"
```

---

### Task 9: Integration verification

- [ ] **Step 1: Full solution build**

```bash
dotnet build --configuration Release
```
Expected: 0 errors

- [ ] **Step 2: Launch with Aspire and test flow**

1. Start Aspire
2. Scan library - files with bitmap-only subtitles should show `HasBitmapSubtitlesOnly = true`
3. Dashboard should show bitmap file count
4. Configure LLM settings (API key, model)
5. Click "Start Bitmap OCR Translation"
6. Verify PGS parsing, PNG rendering, OCR requests, SRT output

- [ ] **Step 3: Commit any fixes**

```
git commit -m "Fix integration issues from bitmap OCR testing"
```
