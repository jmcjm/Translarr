using Microsoft.AspNetCore.Mvc;
using Translarr.Core.Api.Models;
using Translarr.Core.Application.Abstractions.Repositories;
using Translarr.Core.Application.Models;
using Translarr.Core.Application.Services;

namespace Translarr.Core.Api.Endpoints;

public static class StatsEndpoints
{
    public static RouteGroupBuilder MapStatsEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/api-usage", GetApiUsageStats)
            .WithName("GetApiUsageStats")
            .WithOpenApi()
            .Produces<List<ApiUsageDto>>();

        group.MapGet("/library-stats", GetLibraryStats)
            .WithName("GetLibraryStats")
            .WithOpenApi()
            .Produces<LibraryStats>();

        return group;
    }

    private static async Task<IResult> GetApiUsageStats(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? model,
        IApiUsageService apiUsageService)
    {
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow;

        var stats = await apiUsageService.GetUsageStatsAsync(fromDate, toDate, model);
        return Results.Ok(stats);
    }

    private static async Task<IResult> GetLibraryStats(ISubtitleEntryRepository repository)
    {
        var allEntries = await repository.GetAllAsync();

        var stats = new LibraryStats
        {
            TotalFiles = allEntries.Count,
            ProcessedFiles = allEntries.Count(e => e.IsProcessed),
            UnprocessedFiles = allEntries.Count(e => !e.IsProcessed),
            WantedFiles = allEntries.Count(e => e is { IsWanted: true, IsProcessed: false, AlreadyHas: false }),
            AlreadyHasFiles = allEntries.Count(e => e.AlreadyHas),
            ErrorFiles = allEntries.Count(e => !string.IsNullOrEmpty(e.ErrorMessage)),
            LastScanned = allEntries.Count != 0
                ? allEntries.Max(e => e.LastScanned) 
                : null
        };

        return Results.Ok(stats);
    }
}

