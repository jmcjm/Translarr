using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Translarr.Core.Api.Helpers;
using Translarr.Core.Api.Hubs;
using Translarr.Core.Api.Models;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Api.Endpoints;

public static class LibraryEndpoints
{
    private static ScanStatus? _currentScanStatus;
    private static readonly Lock ScanLock = new();

    public static RouteGroupBuilder MapLibraryEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/scan", ScanLibrary)
            .WithName("ScanLibrary")
            .Produces<ScanResultDto>()
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        group.MapGet("/scan/status", GetScanStatus)
            .WithName("GetScanStatus")
            .Produces<ScanStatus>();

        group.MapGet("/entries", GetEntries)
            .WithName("GetEntries")
            .Produces<PagedResult<SubtitleEntryDto>>();

        group.MapGet("/entries/{id:int}", GetEntryById)
            .WithName("GetEntryById")
            .Produces<SubtitleEntryDto>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPatch("/entries/{id:int}/wanted", UpdateWantedStatus)
            .WithName("UpdateWantedStatus")
            .Produces<SubtitleEntryDto>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPatch("/entries/{id:int}/force", UpdateForceProcessStatus)
            .WithName("UpdateForceProcessStatus")
            .Produces<SubtitleEntryDto>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/bulk/wanted", BulkUpdateWantedStatus)
            .WithName("BulkUpdateWantedStatus")
            .Produces<BulkUpdateResult>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest);

        group.MapGet("/browse", GetLibraries)
            .WithName("GetLibraries")
            .Produces<List<string>>();

        group.MapGet("/browse/series", GetSeriesByLibrary)
            .WithName("GetSeriesByLibrary")
            .Produces<List<SeriesGroupDto>>();

        group.MapGet("/browse/series/detail", GetSeriesDetail)
            .WithName("GetSeriesDetail")
            .Produces<SeriesDetailDto>()
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    private static IResult ScanLibrary(
        IServiceScopeFactory serviceScopeFactory,
        IHubContext<ProgressHub> hubContext)
    {
        // Check if scan is already running
        lock (ScanLock)
        {
            if (_currentScanStatus?.IsRunning == true)
            {
                return Results.Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Library scan already in progress",
                    Detail = "A library scan is already running. Please wait for it to complete."
                });
            }

            _currentScanStatus = new ScanStatus
            {
                IsRunning = true,
                StartedAt = DateTime.UtcNow,
                Progress = "Starting library scan..."
            };
        }

        // Start scan asynchronously with its own scope
        _ = Task.Run(async () =>
        {
            try
            {
                // Create new scope for background task
                using var scope = serviceScopeFactory.CreateScope();
                var scannerService = scope.ServiceProvider.GetRequiredService<IMediaScannerService>();

                void OnScanProgress(ScanProgressUpdate update)
                {
                    HubBroadcastHelper.LockSnapshotBroadcast(ScanLock, () =>
                    {
                        if (_currentScanStatus is null) return null;
                        _currentScanStatus.TotalFiles = update.TotalFiles;
                        _currentScanStatus.ProcessedFiles = update.ProcessedFiles;
                        _currentScanStatus.CurrentFileName = update.CurrentFileName;
                        _currentScanStatus.CurrentStep = update.CurrentStep;
                        _currentScanStatus.Progress = FormatScanProgress(update);
                        return _currentScanStatus.Snapshot();
                    }, "ScanProgress", hubContext);
                }

                var result = await scannerService.ScanLibraryAsync(OnScanProgress);

                HubBroadcastHelper.LockSnapshotBroadcast(ScanLock, () =>
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
                    return _currentScanStatus.Snapshot();
                }, "ScanProgress", hubContext);
            }
            catch (Exception ex)
            {
                HubBroadcastHelper.LockSnapshotBroadcast(ScanLock, () =>
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
                    return _currentScanStatus.Snapshot();
                }, "ScanProgress", hubContext);
            }
        });

        return Results.Accepted("/api/library/scan/status", new
        {
            Message = "Library scan started",
            StatusUrl = "/api/library/scan/status"
        });
    }

    private static async Task<IResult> GetEntries(
        [FromServices] ILibraryService libraryService,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] bool? isProcessed = null,
        [FromQuery] bool? isWanted = null,
        [FromQuery] bool? alreadyHas = null,
        [FromQuery] string? search = null)
    {
        var result = await libraryService.GetEntriesAsync(page, pageSize, isProcessed, isWanted, alreadyHas, search);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetEntryById(int id, [FromServices] ILibraryService service)
    {
        var entryResult = await service.GetEntryById(id);
        
        if (entryResult.IsError)
            return ErrorTypeMapper.MapErrorsToProblemResponse(entryResult);
        
        return Results.Ok(entryResult.Value);
    }

    private static async Task<IResult> UpdateWantedStatus(
        int id,
        [FromBody] UpdateWantedRequest request,
        [FromServices] ILibraryService service)
    {
        var result = await service.SetWantedStatusAsync(id, request.IsWanted);

        if (result.IsError)
            return ErrorTypeMapper.MapErrorsToProblemResponse(result);
        
        return Results.Ok(result.Value);
    }
    
    private static async Task<IResult> UpdateForceProcessStatus(
        int id,
        [FromBody] UpdateForceProcessRequest request,
        [FromServices] ILibraryService service)
    {
        var result = await service.SetForceProcessStatusAsync(id, request.IsWanted);

        if (result.IsError)
            return ErrorTypeMapper.MapErrorsToProblemResponse(result);
        
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> BulkUpdateWantedStatus(
        [FromQuery] string series,
        [FromQuery] string? season,
        [FromQuery] bool wanted,
        [FromQuery] string? library,
        [FromServices] ILibraryService service)
    {
        var result = await service.BulkSetWantedAsync(series, season, wanted, library);

        if (result.IsError)
            return ErrorTypeMapper.MapErrorsToProblemResponse(result);

        return Results.Ok(new BulkUpdateResult(result.Value));
    }

    private static async Task<IResult> GetLibraries([FromServices] ILibraryService libraryService)
    {
        var result = await libraryService.GetLibrariesAsync();

        if (result.IsError)
            return ErrorTypeMapper.MapErrorsToProblemResponse(result);

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> GetSeriesByLibrary(
        [FromQuery] string library,
        [FromServices] ISeriesWatchService seriesWatchService)
    {
        var result = await seriesWatchService.GetSeriesGroupsWithWatchStatusAsync(library);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetSeriesDetail(
        [FromQuery] string library,
        [FromQuery] string series,
        [FromServices] ILibraryService libraryService)
    {
        var result = await libraryService.GetSeriesDetailAsync(library, series);

        if (result.IsError)
            return ErrorTypeMapper.MapErrorsToProblemResponse(result);

        return Results.Ok(result.Value);
    }

    private static IResult GetScanStatus()
    {
        lock (ScanLock)
        {
            if (_currentScanStatus is null)
            {
                return Results.Ok(new ScanStatus
                {
                    IsRunning = false,
                    Progress = "No scan running"
                });
            }

            return Results.Ok(_currentScanStatus);
        }
    }

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
}
