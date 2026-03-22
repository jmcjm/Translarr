# SignalR Progress Push Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace HTTP polling with SignalR push for all progress reporting (text translation, bitmap translation, library scan).

**Architecture:** API hosts a `ProgressHub` that pushes status updates to connected Blazor Server clients. Existing callback pattern in services stays unchanged — endpoint callbacks gain an additional `IHubContext` push step alongside the existing static field update. GET status endpoints remain for reconnect state sync.

**Tech Stack:** ASP.NET Core SignalR (server built-in), Microsoft.AspNetCore.SignalR.Client (WebApp), .NET 10

**Spec:** `docs/superpowers/specs/2026-03-22-signalr-progress-design.md`

**Worktree:** `/var/home/jmc/RiderProjects/Translarr/.worktrees/feature-signalr` (branch: `feature/signalr-progress`)

**Note on tests:** This project has no test infrastructure. Steps are structured as build-verify cycles instead of TDD.

---

### Task 1: NuGet packages and new Application models

**Files:**
- Modify: `Directory.Packages.props:58` (after AI group)
- Modify: `Frontend/HavitWebApp/HavitWebApp.csproj:18-21` (package references)
- Create: `Core/Application/Models/ScanProgressUpdate.cs`
- Create: `Core/Application/Models/ScanStep.cs`

- [ ] **Step 1: Add SignalR.Client package version to central package management**

In `Directory.Packages.props`, add after the `<ItemGroup Label="AI">` block (after line 60):

```xml
<ItemGroup Label="SignalR">
  <PackageVersion Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.0" />
</ItemGroup>
```

- [ ] **Step 2: Add SignalR.Client package reference to WebApp**

In `Frontend/HavitWebApp/HavitWebApp.csproj`, add to the `<ItemGroup>` at line 18-21:

```xml
<PackageReference Include="Microsoft.AspNetCore.SignalR.Client" />
```

- [ ] **Step 3: Create ScanStep enum**

Create `Core/Application/Models/ScanStep.cs`:

```csharp
namespace Translarr.Core.Application.Models;

public enum ScanStep
{
    Starting,
    DiscoveringFiles,
    AnalyzingStreams,
    UpdatingDatabase,
    Completed
}
```

- [ ] **Step 4: Create ScanProgressUpdate record**

Create `Core/Application/Models/ScanProgressUpdate.cs`:

```csharp
namespace Translarr.Core.Application.Models;

public record ScanProgressUpdate(
    int TotalFiles,
    int ProcessedFiles,
    string CurrentFileName,
    ScanStep CurrentStep
);
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build --configuration Release`
Expected: Build succeeded, 0 errors

- [ ] **Step 6: Commit**

```bash
git add Directory.Packages.props Frontend/HavitWebApp/HavitWebApp.csproj Core/Application/Models/ScanStep.cs Core/Application/Models/ScanProgressUpdate.cs
git commit -m "Add SignalR.Client package and scan progress models"
```

---

### Task 2: Status model updates — Snapshot() methods and ScanStep field

**Files:**
- Modify: `Core/Api/Models/TranslationStatus.cs`
- Modify: `Core/Api/Models/ScanStatus.cs`

- [ ] **Step 1: Add Snapshot() to TranslationStatus**

In `Core/Api/Models/TranslationStatus.cs`, add method after line 20 (before closing brace):

```csharp
public TranslationStatus Snapshot() => new()
{
    IsRunning = IsRunning,
    StartedAt = StartedAt,
    CompletedAt = CompletedAt,
    Progress = Progress,
    Result = Result,
    Error = Error,
    TotalFiles = TotalFiles,
    ProcessedFiles = ProcessedFiles,
    CurrentFileName = CurrentFileName,
    CurrentStep = CurrentStep,
    CurrentBatch = CurrentBatch,
    TotalBatches = TotalBatches
};
```

- [ ] **Step 2: Add ScanStep field and Snapshot() to ScanStatus**

In `Core/Api/Models/ScanStatus.cs`, add `ScanStep` field and `Snapshot()`. The file needs a using for `ScanStep` (it's in Application.Models). Add after line 17 (before closing brace):

```csharp
public ScanStep CurrentStep { get; set; }

public ScanStatus Snapshot() => new()
{
    IsRunning = IsRunning,
    StartedAt = StartedAt,
    CompletedAt = CompletedAt,
    Progress = Progress,
    Result = Result,
    Error = Error,
    TotalFiles = TotalFiles,
    ProcessedFiles = ProcessedFiles,
    CurrentFileName = CurrentFileName,
    CurrentStep = CurrentStep
};
```

Note: `ScanStatus.cs` already has `using Translarr.Core.Application.Models;` at line 1, so `ScanStep` resolves.

- [ ] **Step 3: Build to verify**

Run: `dotnet build --configuration Release`
Expected: Build succeeded, 0 errors

- [ ] **Step 4: Commit**

```bash
git add Core/Api/Models/TranslationStatus.cs Core/Api/Models/ScanStatus.cs
git commit -m "Add Snapshot() to status models and ScanStep field to ScanStatus"
```

---

### Task 3: ProgressHub, API registration, and JWT query string auth

**Files:**
- Create: `Core/Api/Hubs/ProgressHub.cs`
- Modify: `Core/Api/Program.cs:69,97-131`
- Modify: `Core/Infrastructure/AuthDependencyInjection.cs:63-74`

- [ ] **Step 1: Create ProgressHub**

Create `Core/Api/Hubs/ProgressHub.cs`:

```csharp
using Microsoft.AspNetCore.SignalR;

namespace Translarr.Core.Api.Hubs;

public class ProgressHub : Hub;
```

- [ ] **Step 2: Register SignalR in API Program.cs**

In `Core/Api/Program.cs`, add `builder.Services.AddSignalR();` after line 27 (`builder.Services.AddAuthorization();`):

```csharp
builder.Services.AddSignalR();
```

Then add hub mapping after line 97 (`app.UseRateLimiter();`), before the API endpoint group:

```csharp
app.MapHub<ProgressHub>("/hubs/progress").RequireAuthorization();
```

Add using at top: `using Translarr.Core.Api.Hubs;`

- [ ] **Step 3: Add JWT query string handler for WebSocket auth**

In `Core/Infrastructure/AuthDependencyInjection.cs`, modify the `.AddJwtBearer()` call at lines 63-74 to include `OnMessageReceived` event. Replace:

```csharp
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = key,
        ClockSkew = TimeSpan.FromMinutes(1)
    };
});
```

With:

```csharp
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = key,
        ClockSkew = TimeSpan.FromMinutes(1)
    };

    // SignalR sends JWT as query string during WebSocket upgrade
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var token = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(token) && path.StartsWithSegments("/hubs"))
                context.Token = token;
            return Task.CompletedTask;
        }
    };
});
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build --configuration Release`
Expected: Build succeeded, 0 errors

- [ ] **Step 5: Commit**

```bash
git add Core/Api/Hubs/ProgressHub.cs Core/Api/Program.cs Core/Infrastructure/AuthDependencyInjection.cs
git commit -m "Add ProgressHub, SignalR registration, and JWT WebSocket auth"
```

---

### Task 4: MediaScannerService progress callback

**Files:**
- Modify: `Core/Application/Abstractions/Services/IMediaScannerService.cs`
- Modify: `Core/Application/Services/MediaScannerService.cs`

- [ ] **Step 1: Update IMediaScannerService interface**

Replace the interface in `Core/Application/Abstractions/Services/IMediaScannerService.cs`:

```csharp
using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Abstractions.Services;

public interface IMediaScannerService
{
    Task<ScanResultDto> ScanLibraryAsync(
        Action<ScanProgressUpdate>? onProgressUpdate = null,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Update MediaScannerService.ScanLibraryAsync signature and add progress reporting**

In `Core/Application/Services/MediaScannerService.cs`, change the `ScanLibraryAsync` method signature at line 21 from:

```csharp
public async Task<ScanResultDto> ScanLibraryAsync()
```

To:

```csharp
public async Task<ScanResultDto> ScanLibraryAsync(
    Action<ScanProgressUpdate>? onProgressUpdate = null,
    CancellationToken cancellationToken = default)
```

- [ ] **Step 3: Add progress reporting calls to ScanLibraryAsync body**

In `ScanLibraryAsync()` (lines 21-50), add progress reports. After `var videoFiles = ScanFilesystemAsync(_mediaRootPath);` (line 33), add:

```csharp
onProgressUpdate?.Invoke(new ScanProgressUpdate(
    TotalFiles: videoFiles.Count,
    ProcessedFiles: 0,
    CurrentFileName: string.Empty,
    CurrentStep: ScanStep.DiscoveringFiles
));
```

- [ ] **Step 4: Add progress callback parameter to AnalyzeVideoFilesAsync**

Change `AnalyzeVideoFilesAsync` signature at line 115 from:

```csharp
private async Task AnalyzeVideoFilesAsync(List<VideoFile> videoFiles, string preferredLang, ScanResultDto result, List<string> errors)
```

To:

```csharp
private async Task AnalyzeVideoFilesAsync(List<VideoFile> videoFiles, string preferredLang, ScanResultDto result, List<string> errors, Action<ScanProgressUpdate>? onProgressUpdate = null)
```

Update the call site in `ScanLibraryAsync` (line 35) from:

```csharp
await AnalyzeVideoFilesAsync(videoFiles, preferredLang, result, errors);
```

To:

```csharp
await AnalyzeVideoFilesAsync(videoFiles, preferredLang, result, errors, onProgressUpdate);
```

- [ ] **Step 5: Add per-file progress reporting inside AnalyzeVideoFilesAsync**

Inside the `foreach` loop in `AnalyzeVideoFilesAsync`, after `processedCount++;` (line 171), add:

```csharp
onProgressUpdate?.Invoke(new ScanProgressUpdate(
    TotalFiles: videoFiles.Count,
    ProcessedFiles: processedCount,
    CurrentFileName: videoFile.FileName,
    CurrentStep: ScanStep.AnalyzingStreams
));
```

After the batch save at line 177 (`logger.LogInformation("Saved batch...")`), add:

```csharp
onProgressUpdate?.Invoke(new ScanProgressUpdate(
    TotalFiles: videoFiles.Count,
    ProcessedFiles: processedCount,
    CurrentFileName: videoFile.FileName,
    CurrentStep: ScanStep.UpdatingDatabase
));
```

At end of `ScanLibraryAsync`, before `return result;` (line 49), add:

```csharp
onProgressUpdate?.Invoke(new ScanProgressUpdate(
    TotalFiles: videoFiles.Count,
    ProcessedFiles: videoFiles.Count,
    CurrentFileName: string.Empty,
    CurrentStep: ScanStep.Completed
));
```

- [ ] **Step 6: Build to verify**

Run: `dotnet build --configuration Release`
Expected: Build succeeded, 0 errors

- [ ] **Step 7: Commit**

```bash
git add Core/Application/Abstractions/Services/IMediaScannerService.cs Core/Application/Services/MediaScannerService.cs
git commit -m "Add progress callback to MediaScannerService"
```

---

### Task 5: TranslationEndpoints — inject IHubContext and push in callbacks

**Files:**
- Modify: `Core/Api/Endpoints/TranslationEndpoints.cs`

- [ ] **Step 1: Add using and inject IHubContext into StartTranslation**

Add usings at top of `TranslationEndpoints.cs`:

```csharp
using Microsoft.AspNetCore.SignalR;
using Translarr.Core.Api.Hubs;
```

Change `StartTranslation` method signature at line 51 from:

```csharp
private static IResult StartTranslation(
    [FromQuery] int batchSize,
    IServiceScopeFactory serviceScopeFactory)
```

To:

```csharp
private static IResult StartTranslation(
    [FromQuery] int batchSize,
    IServiceScopeFactory serviceScopeFactory,
    IHubContext<ProgressHub> hubContext)
```

- [ ] **Step 2: Modify text translation OnProgressUpdate callback for snapshot-then-send**

Replace the `OnProgressUpdate` local function (lines 91-104) with:

```csharp
void OnProgressUpdate(TranslationProgressUpdate update)
{
    TranslationStatus snapshot;
    lock (StatusLock)
    {
        if (_currentStatus != null)
        {
            _currentStatus.TotalFiles = update.TotalFiles;
            _currentStatus.ProcessedFiles = update.ProcessedFiles;
            _currentStatus.CurrentFileName = update.CurrentFileName;
            _currentStatus.CurrentStep = update.CurrentStep;
            _currentStatus.Progress = FormatProgress(update);
            snapshot = _currentStatus.Snapshot();
        }
        else return;
    }
    _ = hubContext.Clients.All.SendAsync("TranslationProgress", snapshot);
}
```

- [ ] **Step 3: Push final status on text translation completion**

After the completion lock block (lines 110-123), add push outside the lock. Replace lines 108-123:

```csharp
var wasCancelled = cts.IsCancellationRequested;

TranslationStatus completionSnapshot;
lock (StatusLock)
{
    _currentStatus = new TranslationStatus
    {
        IsRunning = false,
        StartedAt = _currentStatus.StartedAt,
        CompletedAt = DateTime.UtcNow,
        Progress = wasCancelled ? "Cancelled" : "Completed",
        CurrentStep = TranslationStep.Completed,
        TotalFiles = _currentStatus.TotalFiles,
        ProcessedFiles = _currentStatus.ProcessedFiles,
        Result = result
    };
    completionSnapshot = _currentStatus.Snapshot();
}
_ = hubContext.Clients.All.SendAsync("TranslationProgress", completionSnapshot);
```

- [ ] **Step 4: Push final status on text translation error**

Replace the error catch block (lines 125-146) with snapshot push. After the lock block, add the push:

```csharp
catch (Exception ex)
{
    TranslationStatus errorSnapshot;
    lock (StatusLock)
    {
        _currentStatus = new TranslationStatus
        {
            IsRunning = false,
            StartedAt = _currentStatus?.StartedAt ?? DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            Progress = $"Failed: {ex.Message}",
            Error = ex.Message,
            Result = new TranslationResultDto
            {
                SuccessCount = 0,
                SkippedNoSubtitles = 0,
                ErrorCount = 0,
                Duration = TimeSpan.Zero,
                Errors = [$"Critical error during translation: {ex.Message}"]
            }
        };
        errorSnapshot = _currentStatus.Snapshot();
    }
    _ = hubContext.Clients.All.SendAsync("TranslationProgress", errorSnapshot);
}
```

- [ ] **Step 5: Inject IHubContext into StartBitmapTranslation**

Change `StartBitmapTranslation` method signature at line 194 from:

```csharp
private static IResult StartBitmapTranslation(
    [FromQuery] int batchSize,
    IServiceScopeFactory serviceScopeFactory)
```

To:

```csharp
private static IResult StartBitmapTranslation(
    [FromQuery] int batchSize,
    IServiceScopeFactory serviceScopeFactory,
    IHubContext<ProgressHub> hubContext)
```

- [ ] **Step 6: Modify bitmap OnProgressUpdate callback for snapshot-then-send**

Replace the bitmap `OnProgressUpdate` (lines 230-245) with:

```csharp
void OnProgressUpdate(TranslationProgressUpdate update)
{
    TranslationStatus snapshot;
    lock (BitmapStatusLock)
    {
        if (_currentBitmapStatus != null)
        {
            _currentBitmapStatus.TotalFiles = update.TotalFiles;
            _currentBitmapStatus.ProcessedFiles = update.ProcessedFiles;
            _currentBitmapStatus.CurrentFileName = update.CurrentFileName;
            _currentBitmapStatus.CurrentStep = update.CurrentStep;
            _currentBitmapStatus.CurrentBatch = update.CurrentBatch;
            _currentBitmapStatus.TotalBatches = update.TotalBatches;
            _currentBitmapStatus.Progress = FormatProgress(update);
            snapshot = _currentBitmapStatus.Snapshot();
        }
        else return;
    }
    _ = hubContext.Clients.All.SendAsync("BitmapProgress", snapshot);
}
```

- [ ] **Step 7: Push final status on bitmap completion**

Replace the bitmap success lock block (find `lock (BitmapStatusLock)` after `var wasCancelled = cts.IsCancellationRequested;`) with:

```csharp
var wasCancelled = cts.IsCancellationRequested;

TranslationStatus completionSnapshot;
lock (BitmapStatusLock)
{
    _currentBitmapStatus = new TranslationStatus
    {
        IsRunning = false,
        StartedAt = _currentBitmapStatus.StartedAt,
        CompletedAt = DateTime.UtcNow,
        Progress = wasCancelled ? "Cancelled" : "Completed",
        CurrentStep = TranslationStep.Completed,
        TotalFiles = _currentBitmapStatus.TotalFiles,
        ProcessedFiles = _currentBitmapStatus.ProcessedFiles,
        Result = result
    };
    completionSnapshot = _currentBitmapStatus.Snapshot();
}
_ = hubContext.Clients.All.SendAsync("BitmapProgress", completionSnapshot);
```

- [ ] **Step 8: Push final status on bitmap error**

Replace the bitmap `catch (Exception ex)` block with:

```csharp
catch (Exception ex)
{
    TranslationStatus errorSnapshot;
    lock (BitmapStatusLock)
    {
        _currentBitmapStatus = new TranslationStatus
        {
            IsRunning = false,
            StartedAt = _currentBitmapStatus?.StartedAt ?? DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            Progress = $"Failed: {ex.Message}",
            Error = ex.Message,
            Result = new TranslationResultDto
            {
                SuccessCount = 0,
                SkippedNoSubtitles = 0,
                ErrorCount = 0,
                Duration = TimeSpan.Zero,
                Errors = [$"Critical error during bitmap translation: {ex.Message}"]
            }
        };
        errorSnapshot = _currentBitmapStatus.Snapshot();
    }
    _ = hubContext.Clients.All.SendAsync("BitmapProgress", errorSnapshot);
}
```

- [ ] **Step 8: Build to verify**

Run: `dotnet build --configuration Release`
Expected: Build succeeded, 0 errors

- [ ] **Step 9: Commit**

```bash
git add Core/Api/Endpoints/TranslationEndpoints.cs
git commit -m "Add SignalR push to translation progress callbacks"
```

---

### Task 6: LibraryEndpoints — inject IHubContext and add scan progress push

**Files:**
- Modify: `Core/Api/Endpoints/LibraryEndpoints.cs`

- [ ] **Step 1: Add usings and inject IHubContext into ScanLibrary**

Add usings at top of `LibraryEndpoints.cs`:

```csharp
using Microsoft.AspNetCore.SignalR;
using Translarr.Core.Api.Hubs;
```

Change `ScanLibrary` method signature at line 53 from:

```csharp
private static IResult ScanLibrary(IServiceScopeFactory serviceScopeFactory)
```

To:

```csharp
private static IResult ScanLibrary(
    IServiceScopeFactory serviceScopeFactory,
    IHubContext<ProgressHub> hubContext)
```

- [ ] **Step 2: Add progress callback and hub push to scan Task.Run**

In the `Task.Run` block (lines 77-122), after getting the scanner service (line 83), replace `var result = await scannerService.ScanLibraryAsync();` (line 85) with:

```csharp
void OnScanProgress(ScanProgressUpdate update)
{
    ScanStatus snapshot;
    lock (ScanLock)
    {
        if (_currentScanStatus != null)
        {
            _currentScanStatus.TotalFiles = update.TotalFiles;
            _currentScanStatus.ProcessedFiles = update.ProcessedFiles;
            _currentScanStatus.CurrentFileName = update.CurrentFileName;
            _currentScanStatus.CurrentStep = update.CurrentStep;
            _currentScanStatus.Progress = FormatScanProgress(update);
            snapshot = _currentScanStatus.Snapshot();
        }
        else return;
    }
    _ = hubContext.Clients.All.SendAsync("ScanProgress", snapshot);
}

var result = await scannerService.ScanLibraryAsync(OnScanProgress);
```

- [ ] **Step 3: Push final scan status on completion**

Replace the success lock block (lines 87-97) with:

```csharp
ScanStatus completionSnapshot;
lock (ScanLock)
{
    _currentScanStatus = new ScanStatus
    {
        IsRunning = false,
        StartedAt = _currentScanStatus.StartedAt,
        CompletedAt = DateTime.UtcNow,
        Progress = "Completed",
        CurrentStep = ScanStep.Completed,
        Result = result
    };
    completionSnapshot = _currentScanStatus.Snapshot();
}
_ = hubContext.Clients.All.SendAsync("ScanProgress", completionSnapshot);
```

- [ ] **Step 4: Push final scan status on error**

Replace the error catch block (lines 99-121) with:

```csharp
catch (Exception ex)
{
    ScanStatus errorSnapshot;
    lock (ScanLock)
    {
        _currentScanStatus = new ScanStatus
        {
            IsRunning = false,
            StartedAt = _currentScanStatus?.StartedAt ?? DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            Progress = $"Failed: {ex.Message}",
            Error = ex.Message,
            CurrentStep = ScanStep.Completed,
            Result = new ScanResultDto
            {
                NewFiles = 0,
                UpdatedFiles = 0,
                RemovedFiles = 0,
                ErrorFiles = 0,
                Duration = TimeSpan.Zero,
                Errors = [$"Critical error during scan: {ex.Message}"]
            }
        };
        errorSnapshot = _currentScanStatus.Snapshot();
    }
    _ = hubContext.Clients.All.SendAsync("ScanProgress", errorSnapshot);
}
```

- [ ] **Step 5: Add FormatScanProgress helper method**

Add at the bottom of `LibraryEndpoints` class:

```csharp
private static string FormatScanProgress(ScanProgressUpdate update)
{
    var stepText = update.CurrentStep switch
    {
        ScanStep.Starting => "Starting",
        ScanStep.DiscoveringFiles => "Discovering files",
        ScanStep.AnalyzingStreams => "Analyzing streams",
        ScanStep.UpdatingDatabase => "Updating database",
        ScanStep.Completed => "Completed",
        _ => "Processing"
    };

    return update.TotalFiles > 0
        ? $"[{update.ProcessedFiles}/{update.TotalFiles}] {stepText}: {update.CurrentFileName}"
        : stepText;
}
```

- [ ] **Step 6: Build to verify**

Run: `dotnet build --configuration Release`
Expected: Build succeeded, 0 errors

- [ ] **Step 7: Commit**

```bash
git add Core/Api/Endpoints/LibraryEndpoints.cs
git commit -m "Add SignalR push to library scan progress"
```

---

### Task 7: Home.razor — replace polling with HubConnection

**Files:**
- Modify: `Frontend/HavitWebApp/Components/Pages/Home.razor`

This is the largest change. Replace the Timer-based polling with a SignalR `HubConnection`.

- [ ] **Step 1: Add inject directives and IAsyncDisposable**

**Note:** The current code has `public void Dispose()` without `@implements IDisposable` — this was a pre-existing bug where the timer was never actually disposed. Adding `@implements IAsyncDisposable` fixes the disposal lifecycle properly.

At the top of `Home.razor`, after existing `@inject` lines (line 5), add:

```razor
@using Microsoft.AspNetCore.SignalR.Client
@using Translarr.Frontend.HavitWebApp.Auth
@inject AuthCookieHolder AuthCookieHolder
@inject IConfiguration Configuration
@implements IAsyncDisposable
```

- [ ] **Step 2: Replace fields in @code block**

Replace `Timer? _statusTimer;` (line 228) with:

```csharp
private HubConnection? _hubConnection;
private CancellationTokenSource? _hubCts;
```

- [ ] **Step 3: Replace OnInitializedAsync**

Replace the entire `OnInitializedAsync` method (lines 230-247) with:

```csharp
protected override async Task OnInitializedAsync()
{
    await LoadStats();

    var apiBaseUrl = Configuration["ApiBaseUrl"] ?? "https+http://Translarr-Api";
    _hubCts = new CancellationTokenSource();

    _hubConnection = new HubConnectionBuilder()
        .WithUrl($"{apiBaseUrl}/hubs/progress", options =>
        {
            options.AccessTokenProvider = () => Task.FromResult(AuthCookieHolder.CookieValue);
        })
        .WithAutomaticReconnect()
        .Build();

    _hubConnection.On<TranslationStatus>("TranslationProgress", status =>
    {
        var wasRunning = _translationStatus?.IsRunning ?? false;
        _translationStatus = status;
        if (wasRunning && !status.IsRunning && status.Result is not null)
            _ = InvokeAsync(LoadStats);
        _ = InvokeAsync(StateHasChanged);
    });

    _hubConnection.On<TranslationStatus>("BitmapProgress", status =>
    {
        var wasRunning = _bitmapTranslationStatus?.IsRunning ?? false;
        _bitmapTranslationStatus = status;
        if (wasRunning && !status.IsRunning && status.Result is not null)
            _ = InvokeAsync(LoadStats);
        _ = InvokeAsync(StateHasChanged);
    });

    _hubConnection.On<ScanStatus>("ScanProgress", status =>
    {
        var wasRunning = _scanStatus?.IsRunning ?? false;
        _scanStatus = status;
        if (wasRunning && !status.IsRunning && status.Result is not null)
            _ = InvokeAsync(LoadStats);
        _ = InvokeAsync(StateHasChanged);
    });

    _hubConnection.Reconnected += async _ => await LoadCurrentStatus();

    try
    {
        await _hubConnection.StartAsync(_hubCts.Token);
        await LoadCurrentStatus();
    }
    catch (OperationCanceledException)
    {
        // Component disposed during connection — ignore
    }
}
```

- [ ] **Step 4: Add LoadCurrentStatus method**

Add after `LoadStats()` method:

```csharp
private async Task LoadCurrentStatus()
{
    try
    {
        _translationStatus = await TranslationService.GetTranslationStatusAsync();
        _bitmapTranslationStatus = await TranslationService.GetBitmapTranslationStatusAsync();
        _scanStatus = await LibraryService.GetScanStatusAsync();
        await InvokeAsync(StateHasChanged);
    }
    catch
    {
        // Ignore errors during status fetch
    }
}
```

- [ ] **Step 5: Remove polling methods**

Delete these three methods entirely (find them by name — line numbers will have shifted from earlier edits):
- `CheckTranslationStatus()` — the method containing `await TranslationService.GetTranslationStatusAsync()` in a try/catch
- `CheckScanStatus()` — the method containing `await LibraryService.GetScanStatusAsync()` in a try/catch
- `CheckBitmapTranslationStatus()` — the method containing `await TranslationService.GetBitmapTranslationStatusAsync()` in a try/catch

- [ ] **Step 6: Replace Dispose with DisposeAsync**

Find `public void Dispose()` method (contains `_statusTimer?.Dispose()`) and replace it with:

```csharp
public async ValueTask DisposeAsync()
{
    if (_hubCts is not null)
    {
        await _hubCts.CancelAsync();
        _hubCts.Dispose();
    }

    if (_hubConnection is not null)
        await _hubConnection.DisposeAsync();
}
```

- [ ] **Step 7: Build to verify**

Run: `dotnet build --configuration Release`
Expected: Build succeeded, 0 errors

- [ ] **Step 8: Commit**

```bash
git add Frontend/HavitWebApp/Components/Pages/Home.razor
git commit -m "Replace HTTP polling with SignalR HubConnection in Home.razor"
```

---

### Task 8: Final build verification and cleanup

- [ ] **Step 1: Clean build of entire solution**

Run: `dotnet build --configuration Release`
Expected: Build succeeded, 0 errors, 0 warnings (or pre-existing warnings only)

- [ ] **Step 2: Verify no remaining references to removed polling pattern**

Search for `_statusTimer` across the codebase — should find zero results.
Search for `CheckTranslationStatus` — should find zero results.
Search for `TimeSpan.FromSeconds(3)` in Home.razor — should find zero results.

- [ ] **Step 3: Verify SignalR hub is registered**

Search for `MapHub<ProgressHub>` in Program.cs — should find exactly one result.
Search for `AddSignalR` in Program.cs — should find exactly one result.

- [ ] **Step 4: Final commit if any cleanup was needed**

Only if changes were made during verification.
