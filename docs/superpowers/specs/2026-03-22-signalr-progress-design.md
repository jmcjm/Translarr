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

**Note:** `ScanStatus` currently lacks a `CurrentStep` field. Add `ScanStep CurrentStep` to `ScanStatus` so the scan phase information can be pushed to and displayed by the frontend (matching `TranslationStatus.CurrentStep` pattern).

### API Changes

#### TranslationEndpoints

Callbacks in `StartTranslation()` and `StartBitmapTranslation()` gain an additional step: after updating the static field under lock, push a **snapshot** via `IHubContext<ProgressHub>`.

**Critical: snapshot-then-send pattern.** `TranslationStatus` is a mutable class. Capturing a reference and sending it outside the lock risks serializing mid-mutation. The callback must:
1. Update the static field inside the lock
2. Create a snapshot (shallow copy) of the status inside the lock
3. Fire-and-forget `SendAsync` **outside** the lock with the snapshot

```csharp
onProgressUpdate: update => {
    TranslationStatus snapshot;
    lock (StatusLock) {
        _currentStatus = MapToStatus(update);
        snapshot = _currentStatus.Snapshot(); // shallow copy
    }
    _ = hubContext.Clients.All.SendAsync("TranslationProgress", snapshot);
}
```

Add a `Snapshot()` method to `TranslationStatus` (and `ScanStatus`) that returns a shallow copy. These are flat DTOs with value-type fields + strings + a nullable `TranslationResultDto`, so shallow copy is sufficient.

Completion, error, and cancellation states also push final status using the same snapshot pattern.

`IHubContext<ProgressHub>` injected as parameter in endpoint lambdas (DI resolution).

Static fields + Lock remain unchanged — source of truth for GET endpoints.

**Note on broadcasting:** `Clients.All.SendAsync` broadcasts to every connected client. This is accepted behavior — Translarr is a single-user self-hosted application. If multi-user support is added later, switch to group-based or user-specific broadcasting.

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
- `CancellationTokenSource _hubCts` for cancelling connection on disposal
- Event handlers for `TranslationProgress`, `BitmapProgress`, `ScanProgress`
- Reconnect logic: `WithAutomaticReconnect()` (default: 0s, 2s, 10s, 30s retry)
- `hubConnection.Reconnected` handler: re-fetch current state via GET endpoints
- `@implements IAsyncDisposable`: convert existing `Dispose()` to `DisposeAsync()`, dispose both hub connection and any remaining timers

```csharp
_hubCts = new CancellationTokenSource();

hubConnection = new HubConnectionBuilder()
    .WithUrl($"{apiBaseUrl}/hubs/progress", options => {
        options.AccessTokenProvider = () => Task.FromResult(authCookieHolder.CookieValue);
    })
    .WithAutomaticReconnect()
    .Build();

hubConnection.On<TranslationStatus>("TranslationProgress", status => {
    var wasRunning = _translationStatus?.IsRunning ?? false;
    _translationStatus = status;
    if (wasRunning && !status.IsRunning && status.Result is not null)
        InvokeAsync(LoadStats);
    InvokeAsync(StateHasChanged);
});
// analogous for BitmapProgress, ScanProgress

hubConnection.Reconnected += async _ => await LoadCurrentStatus();

await hubConnection.StartAsync(_hubCts.Token);
await LoadCurrentStatus(); // GET endpoints for initial state
```

**State transition detection:** The current polling code checks `IsRunning: false, Result: not null` to trigger `LoadStats()`. With push updates, this check would re-trigger on every push after completion. Instead, track the **transition**: compare `wasRunning` (previous state) to new state. Only call `LoadStats()` when transitioning from running to completed.

**CancellationToken on StartAsync:** If the component is disposed while `StartAsync()` is connecting (user navigates away), it throws `ObjectDisposedException`. Pass `_hubCts.Token` and cancel it in `DisposeAsync()`.

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

Hub mapped with `.RequireAuthorization()`.

**WebSocket JWT query string handling:** SignalR client cannot send custom HTTP headers during WebSocket upgrade. Instead, the token is sent as `?access_token=...` query parameter. The API's JWT auth middleware must be configured to read tokens from the query string for hub endpoints:

```csharp
// In JWT Bearer configuration (Program.cs or auth setup)
options.Events = new JwtBearerEvents {
    OnMessageReceived = context => {
        var token = context.Request.Query["access_token"];
        var path = context.HttpContext.Request.Path;
        if (!string.IsNullOrEmpty(token) && path.StartsWithSegments("/hubs"))
            context.Token = token;
        return Task.CompletedTask;
    }
};
```

Without this, every WebSocket connection will 401.

**Token provider in WebApp:** `HubConnection` uses `AccessTokenProvider` delegate. Inject `AuthCookieHolder` (scoped, circuit-lifetime) into `Home.razor` and read `CookieValue` for the JWT:

```csharp
options.AccessTokenProvider = () => Task.FromResult(authCookieHolder.CookieValue);
```

## Files Changed

| File | Change Type | Description |
|------|------------|-------------|
| `Core/Api/Hubs/ProgressHub.cs` | **New** | Empty SignalR hub |
| `Core/Api/Program.cs` | Modified | Add `AddSignalR()` + `MapHub<ProgressHub>()` |
| `Core/Api/Endpoints/TranslationEndpoints.cs` | Modified | Inject `IHubContext`, push in callbacks |
| `Core/Api/Endpoints/LibraryEndpoints.cs` | Modified | Add scan progress callback + hub push |
| `Core/Api/Models/ScanStatus.cs` | Modified | Add `ScanStep CurrentStep` field + `Snapshot()` method |
| `Core/Api/Models/TranslationStatus.cs` | Modified | Add `Snapshot()` method |
| `Core/Application/Services/MediaScannerService.cs` | Modified | Add `Action<ScanProgressUpdate>?` parameter, report progress |
| `Core/Application/Services/IMediaScannerService.cs` | Modified | Updated interface signature |
| `Core/Application/Models/ScanProgressUpdate.cs` | **New** | Scan progress record |
| `Core/Application/Models/ScanStep.cs` | **New** | Scan step enum |
| `Frontend/HavitWebApp/HavitWebApp.csproj` | Modified | Add SignalR.Client package reference |
| `Frontend/HavitWebApp/Components/Pages/Home.razor` | Modified | Replace Timer polling with HubConnection |
| `Directory.Packages.props` | Modified | Add SignalR.Client version |

## What Does NOT Change

- `TranslationStatus` model — same payload (gains `Snapshot()` method only)
- `TranslationProgressUpdate`, `TranslationStep` — unchanged
- `SubtitleTranslationService`, `BitmapTranslationService` — callback signatures unchanged
- GET status endpoints — kept for reconnect
- Start/cancel endpoints — request-response, no SignalR needed
- Static fields + Lock in endpoints — remain as source of truth
