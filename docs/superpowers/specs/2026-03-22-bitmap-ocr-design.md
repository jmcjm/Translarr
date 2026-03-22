# Bitmap Subtitle OCR Translation Design

## Context

Translarr currently skips files with bitmap-only subtitles (PGS, VobSub) because they can't be extracted as text. POC confirmed that PGS frames can be parsed to PNG and sent to an LLM (via OpenAI-compatible API) for OCR + translation in a single request with good accuracy.

This feature adds a separate pipeline for bitmap subtitle translation, independent from the existing text subtitle pipeline. It's intentionally separated because OCR is significantly more expensive in API tokens (sending images vs text).

## Architecture

### Two Independent Pipelines

**Existing text pipeline:** Scan → mark wanted → "Start Translation" button → extract text subtitles → send text to LLM → save SRT

**New bitmap pipeline:** Scan → detect bitmap-only → set `HasBitmapSubtitlesOnly` flag → dashboard shows count → "Start Bitmap OCR Translation" button → extract .sup → parse PGS → render PNG to disk → batch OCR+translate via LLM → assemble SRT → save

User consciously decides when to run bitmap OCR (separate button on dashboard) because of higher API cost.

### Scan Logic Change

Current behavior: bitmap-only files → `IsProcessed = true`, `ErrorMessage = "bitmap..."`, skipped forever.

New behavior: bitmap-only files → `HasBitmapSubtitlesOnly = true`, `IsProcessed = false`, `IsWanted = true`. File waits for bitmap OCR pipeline.

## Data Layer

### New DB Column

Migration adds `HasBitmapSubtitlesOnly` (bool, default false) to `subtitle_entries` table.

`SubtitleEntryDto` gets matching property.

### New Settings

| Key | Default | Description |
|---|---|---|
| `OcrBatchSize` | `15` | Number of subtitle frames per OCR API request |
| `OcrSystemPrompt` | *(see below)* | System prompt template for bitmap OCR+translation |

Default OCR prompt:
```
### You are an OCR and subtitle translator (English → {preferredLang}).
You receive numbered images containing embedded subtitles along with their exact timestamps.

{timestamps}

For each image:
1. Extract the text from the image
2. Translate it to {preferredLang}
3. Use EXACTLY the provided timestamps

Rules:
- Output **MUST** always be in valid **SRT** format
- **Remove** any formatting tags
- Number subtitles sequentially starting from 1
- Preserve line breaks within subtitles
- Return ONLY the SRT subtitles, nothing else
```

Placeholders:
- `{timestamps}` - dynamically replaced per batch with numbered timestamp list
- `{preferredLang}` - from `PreferredSubsLang` setting

`LlmSettingsDto` extended with `OcrBatchSize` (int) and `OcrSystemPrompt` (string).

## Infrastructure - New Components

### PgsParser

`Core/Infrastructure/Services/PgsParser.cs`

Parses .sup file → `List<PgsSubtitle>`. Each subtitle has: `StartTime`, `EndTime`, `Width`, `Height`, `RleData` (byte[]), `Palette` (color array).

Port from POC (`sub-poc/PgsExtract.cs`). Handles PGS segment types: PCS (composition), PDS (palette), ODS (object data), END. Pairs show/clear events for start/end timestamps. Filters out empty (clear) events.

### PgsRenderer

`Core/Infrastructure/Services/PgsRenderer.cs`

Takes `List<PgsSubtitle>` and output directory → renders each subtitle to PNG on disk. Filenames encode timing: `{index}_{startHHMMSSMMM}_{endHHMMSSMMM}.png`.

RLE decoder handles: single pixel, short/long transparent runs, short/long colored runs, end-of-line. YCbCr → RGB color conversion from palette. Black background, subtitle text rendered on top. Uses ImageSharp (`SixLabors.ImageSharp`) for PNG encoding.

Returns `List<RenderedFrame>` with `FilePath`, `StartTime`, `EndTime`, `Index`.

### ISubtitleTranslator Extension

Add new method to existing interface:
```csharp
Task<string> TranslateWithImagesAsync(string prompt, List<byte[]> images, LlmSettingsDto settings);
```

`OpenAiSubtitleTranslator` implementation: builds `UserChatMessage` with text part (prompt) + image parts (base64 PNG inline data). Uses same `OpenAIClient` with configured base URL + API key.

### BitmapSubtitleTranslator

`Core/Infrastructure/Services/BitmapSubtitleTranslator.cs`

Implements `IBitmapSubtitleTranslator`. Orchestrates per-file OCR:

1. Extract .sup via `IFfmpegService`
2. Parse via `PgsParser`
3. Render to PNG via `PgsRenderer`
4. Batch frames by `OcrBatchSize`
5. Per batch: build prompt (template + timestamps), load PNG bytes, call `ISubtitleTranslator.TranslateWithImagesAsync`
6. Merge SRT fragments, fix numbering
7. Cleanup temp files
8. Return final SRT string

## Application Layer

### IBitmapSubtitleTranslator

`Core/Application/Abstractions/Services/IBitmapSubtitleTranslator.cs`

```csharp
Task<string> TranslateBitmapSubtitlesAsync(
    string videoPath, int streamIndex, LlmSettingsDto settings);
```

### BitmapTranslationService

`Core/Application/Services/BitmapTranslationService.cs`

Orchestrates batch processing. Similar to `SubtitleTranslationService` but for bitmap pipeline:

1. Query entries: `IsWanted && HasBitmapSubtitlesOnly && !IsProcessed`
2. For each entry: find bitmap subtitle stream, call `IBitmapSubtitleTranslator`, save SRT, mark processed
3. Progress reporting via callback (same pattern as existing)
4. Rate limiting, error handling, cancellation token support

### FfmpegService Changes

- New method: `ExtractSupAsync(string videoPath, int streamIndex, string outputPath)` - extracts PGS stream as .sup file
- `FindBestSubtitleStreamAsync`: when bitmap-only, still returns stream info (not null) but with a flag. OR: return null as before, and `BitmapTranslationService` calls a separate method to find bitmap streams.

Better approach: new method `FindBestBitmapSubtitleStreamAsync(string videoPath)` that returns the best bitmap stream (preferring English, non-SDH, same logic but for bitmap codecs only).

### MediaScannerService Changes

When scan detects bitmap-only subtitles: set `HasBitmapSubtitlesOnly = true`, `IsProcessed = false` (not true), don't set error message.

## API Layer

### New Endpoint

`POST /api/translation/translate-bitmap?batchSize=100` - starts bitmap OCR translation pipeline. Same pattern as existing `/translate`: fire-and-forget Task.Run, progress reporting, conflict detection if already running.

`GET /api/translation/bitmap-status` - status polling for bitmap pipeline.

`POST /api/translation/cancel-bitmap` - cancel bitmap pipeline.

### Stats Extension

`GET /api/stats/library-stats` - add `BitmapFiles` count to `LibraryStats` response.

## Frontend

### Dashboard (Home.razor)

- New stat card: "Bitmap Subtitles" showing count of `HasBitmapSubtitlesOnly && IsWanted && !IsProcessed`
- New button: "Start Bitmap OCR Translation" (same pattern as existing translation button, with stop capability)
- Progress card for bitmap translation (separate from text translation)

### Settings (Settings.razor)

New card "Bitmap OCR Settings" in right column:
- **OCR Batch Size** - number input (min 1, max 50, default 15)
- **OCR System Prompt** - textarea with help text about `{timestamps}` and `{preferredLang}` placeholders

## Data Flow Per File

```
1. BitmapTranslationService selects file (IsWanted && HasBitmapSubtitlesOnly && !IsProcessed)
2. FfmpegService.ExtractSupAsync(videoPath, streamIndex) → /tmp/translarr/{hash}.sup
3. PgsParser.Parse(supPath) → List<PgsSubtitle>
4. PgsRenderer.RenderAll(subtitles, outputDir) → /tmp/translarr/{hash}_frames/*.png
5. Batch loop (OcrBatchSize from settings, default 15):
   a. Take next N frames
   b. Build prompt: OCR template with {timestamps} filled + {preferredLang}
   c. Load PNG bytes from disk
   d. Call ISubtitleTranslator.TranslateWithImagesAsync(prompt, images, settings)
   e. Append response SRT fragment
6. Merge all SRT fragments → fix sequential numbering
7. Save as {baseFileName}.{preferredLang}.srt next to video
8. Mark entry IsProcessed = true, clear HasBitmapSubtitlesOnly? No - keep flag for info
9. Cleanup: delete .sup and PNG frames from temp dir
```

## Packages

### Add to Directory.Packages.props

- `SixLabors.ImageSharp` (latest stable)

### Add to Infrastructure.csproj

- `<PackageReference Include="SixLabors.ImageSharp" />`

## Files Created

- `Core/Application/Abstractions/Services/IBitmapSubtitleTranslator.cs`
- `Core/Application/Services/BitmapTranslationService.cs`
- `Core/Infrastructure/Services/PgsParser.cs`
- `Core/Infrastructure/Services/PgsRenderer.cs`
- `Core/Infrastructure/Services/BitmapSubtitleTranslator.cs`
- `Core/Infrastructure/Migrations/XXXXXXXX_AddHasBitmapSubtitlesOnly.cs`

## Files Modified

- `Core/Application/Abstractions/Services/ISubtitleTranslator.cs` - add `TranslateWithImagesAsync`
- `Core/Application/Abstractions/Services/IFfmpegService.cs` - add `ExtractSupAsync`, `FindBestBitmapSubtitleStreamAsync`
- `Core/Application/Models/LlmSettingsDto.cs` - add `OcrBatchSize`, `OcrSystemPrompt`
- `Core/Application/Models/SubtitleEntryDto.cs` - add `HasBitmapSubtitlesOnly`
- `Core/Infrastructure/Services/FfmpegService.cs` - implement new methods, change bitmap-only scan logic
- `Core/Infrastructure/Services/OpenAiSubtitleTranslator.cs` - implement `TranslateWithImagesAsync`
- `Core/Infrastructure/Services/TranslarrDatabaseInitializer.cs` - add new settings defaults
- `Core/Infrastructure/DependencyInjection.cs` - register new services
- `Core/Infrastructure/Persistence/Daos/SubtitleEntryDao.cs` - add column
- `Core/Infrastructure/Persistence/Configurations/SubtitleEntryConfiguration.cs` - add column config
- `Core/Api/Endpoints/TranslationEndpoints.cs` - add bitmap endpoints
- `Core/Api/Endpoints/StatsEndpoints.cs` - add bitmap count
- `Core/Api/Models/LibraryStats.cs` - add BitmapFiles
- `Frontend/HavitWebApp/Components/Pages/Home.razor` - bitmap stat card, button, progress
- `Frontend/HavitWebApp/Components/Pages/Settings.razor` - OCR settings card
- `Frontend/HavitWebApp/Services/TranslationApiService.cs` - bitmap API methods
- `Directory.Packages.props` - add ImageSharp
- `Core/Infrastructure/Infrastructure.csproj` - add ImageSharp reference
- `Core/Application/Services/SettingsService.cs` - read new settings
