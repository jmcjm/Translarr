# SignalR Progress Push — Design Spec

## Summary

Replace HTTP polling (3-second timer in `Home.razor`) with SignalR push for all progress reporting: text translation, bitmap/OCR translation, and library scan. The API hosts a single `ProgressHub`; the Blazor Server WebApp connects via `HubConnection` and receives real-time updates.

## Context

### Current Architecture

- **Static state in endpoints:** `TranslationEndpoints` holds `_currentStatus`, `_currentBitmapStatus` with `Lock`. `LibraryEndpoints` holds `_currentScanStatus`.
- **Callback pattern:** `SubtitleTranslationService` and `BitmapTranslationService` accept `Action<TranslationProgressUpdate>?` callbacks. Endpoints wire these callbacks to update static fields.
- **Polling:** `Home.razor` runs a `Timer` every 3 seconds calling GET `/api/translation/status`, `/api/translation/bitmap-status`, and `/api/library/scan/status`.
- **No progress for scan:** `MediaScannerService.ScanLibraryAsync()` has no progress callback — scan status shows only "Starting" or "Completed".

### Problem

Blazor Server already runs on SignalR. Doing HTTP polling from within a SignalR circuit is redundant — adds unnecessary latency and network churn for something that could be pushed instantly.

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Hub location | API project | Progress state lives in API; services push directly via `IHubContext` |
| Hub count | Single `ProgressHub` | Same type of data (progress), same consumers; one WebSocket connection |
| Integration approach | IHubContext injection in endpoints (Approach A) | Minimal refactor; Application layer has no SignalR dependency; callbacks unchanged |
| Existing GET endpoints | Keep as fallback | Needed for reconnect state sync; zero maintenance cost |
| Scan progress | Add callback + push | Per-file granularity with phase steps, matching translation pattern |

## Architecture

### ProgressHub

```
Core/Api/Hubs/ProgressHub.cs
```

Empty hub — no server-side methods. Clients are receive-only.

```csharp
public class ProgressHub : Hub { }
```

Mapped at `/hubs/progress` with `.RequireAuthorization()`.

### Client Events

| Method Name | Payload Type | Trigger |
|-------------|-------------|---------|
| `TranslationProgress` | `TranslationStatus` | Each step of text subtitle translation |
| `BitmapProgress` | `TranslationStatus` | Each step of bitmap/OCR translation |
| `ScanProgress` | `ScanStatus` | Each step of library scan |

Payload is the full status object (same models as GET endpoints return). No deltas.

### API Changes

#### TranslationEndpoints

Callbacks in `StartTranslation()` and `StartBitmapTranslation()` gain an additional step: after updating the static field under lock, push via `IHubContext<ProgressHub>`:

```
onProgressUpdate: update => {
    lock { _currentStatus = mapToStatus(update); }
    hubContext.Clients.All.SendAsync("TranslationProgress", _currentStatus);
}
```

Completion, error, and cancellation states also push final status.

`IHubContext<ProgressHub>` injected as parameter in endpoint lambdas (DI resolution).

Static fields + Lock remain unchanged — source of truth for GET endpoints.

#### LibraryEndpoints

Same pattern as translation. Requires adding `Action<ScanProgressUpdate>?` parameter to the scan endpoint's call to `MediaScannerService`, then pushing via hub.

#### Program.cs (Api)

```csharp
builder.Services.AddSignalR();
// ...
app.MapHub<ProgressHub>("/hubs/progress").RequireAuthorization();
```

### Application Layer Changes

#### MediaScannerService

New optional parameter on `ScanLibraryAsync()`:

```csharp
Task<ScanResultDto> ScanLibraryAsync(
    Action<ScanProgressUpdate>? onProgressUpdate = null,
    CancellationToken cancellationToken = default);
```

#### New Models

```csharp
// Core/Application/Models/ScanProgressUpdate.cs
public record ScanProgressUpdate(
    int TotalFiles,
    int ProcessedFiles,
    string CurrentFileName,
    ScanStep CurrentStep
);

// Core/Application/Models/ScanStep.cs
public enum ScanStep
{
    Starting,
    DiscoveringFiles,
    AnalyzingStreams,
    UpdatingDatabase,
    Completed
}
```

#### Progress Points in Scanner

- `DiscoveringFiles` — after collecting file list (TotalFiles known)
- `AnalyzingStreams` — per-file, during FFmpeg stream analysis
- `UpdatingDatabase` — per-file, when saving/updating SubtitleEntry
- `Completed` — after all files processed

### WebApp Changes

#### Home.razor

**Remove:**
- `_statusTimer` (Timer with 3s interval)
- `CheckTranslationStatus()`, `CheckBitmapTranslationStatus()`, `CheckScanStatus()` polling methods

**Add:**
- `HubConnection` field, built in `OnInitializedAsync()`
- Event handlers for `TranslationProgress`, `BitmapProgress`, `ScanProgress`
- Reconnect logic: `WithAutomaticReconnect()` (default: 0s, 2s, 10s, 30s retry)
- On connect/reconnect: one-time GET to status endpoints for current state
- `IAsyncDisposable`: `hubConnection.DisposeAsync()`

```csharp
hubConnection = new HubConnectionBuilder()
    .WithUrl($"{apiBaseUrl}/hubs/progress", options => {
        options.AccessTokenProvider = () => tokenProvider.GetTokenAsync();
    })
    .WithAutomaticReconnect()
    .Build();

hubConnection.On<TranslationStatus>("TranslationProgress", status => {
    _translationStatus = status;
    InvokeAsync(StateHasChanged);
});
// analogous for BitmapProgress, ScanProgress

await hubConnection.StartAsync();
await LoadCurrentStatus(); // GET endpoints for initial state
```

#### TranslationApiService

- `GetTranslationStatusAsync()`, `GetBitmapTranslationStatusAsync()` — **kept** (used on reconnect)
- `StartTranslationAsync()`, `CancelTranslationAsync()` etc. — **kept unchanged** (request-response)
- Add `GetScanStatusAsync()` if not already present

#### Program.cs (WebApp)

No SignalR server-side registration needed. `HubConnection` is client-side only.

### NuGet Packages

**Directory.Packages.props:**
```xml
<PackageVersion Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.0" />
```

Referenced in `Frontend/HavitWebApp/HavitWebApp.csproj`.

Server-side SignalR is built into ASP.NET Core — no additional package for Api.

### Authorization

Hub mapped with `.RequireAuthorization()`. `HubConnection` provides Bearer token via `AccessTokenProvider`, using the same auth mechanism as `AuthenticatedApiClientFactory`.

## Files Changed

| File | Change Type | Description |
|------|------------|-------------|
| `Core/Api/Hubs/ProgressHub.cs` | **New** | Empty SignalR hub |
| `Core/Api/Program.cs` | Modified | Add `AddSignalR()` + `MapHub<ProgressHub>()` |
| `Core/Api/Endpoints/TranslationEndpoints.cs` | Modified | Inject `IHubContext`, push in callbacks |
| `Core/Api/Endpoints/LibraryEndpoints.cs` | Modified | Add scan progress callback + hub push |
| `Core/Application/Services/MediaScannerService.cs` | Modified | Add `Action<ScanProgressUpdate>?` parameter, report progress |
| `Core/Application/Services/IMediaScannerService.cs` | Modified | Updated interface signature |
| `Core/Application/Models/ScanProgressUpdate.cs` | **New** | Scan progress record |
| `Core/Application/Models/ScanStep.cs` | **New** | Scan step enum |
| `Frontend/HavitWebApp/HavitWebApp.csproj` | Modified | Add SignalR.Client package reference |
| `Frontend/HavitWebApp/Components/Pages/Home.razor` | Modified | Replace Timer polling with HubConnection |
| `Directory.Packages.props` | Modified | Add SignalR.Client version |

## What Does NOT Change

- `TranslationStatus`, `ScanStatus` models — same payload
- `TranslationProgressUpdate`, `TranslationStep` — unchanged
- `SubtitleTranslationService`, `BitmapTranslationService` — callback signatures unchanged
- GET status endpoints — kept for reconnect
- Start/cancel endpoints — request-response, no SignalR needed
- Static fields + Lock in endpoints — remain as source of truth
