using Microsoft.AspNetCore.Mvc;
using Translarr.Core.Api.Models;
using Translarr.Core.Application.Abstractions.Repositories;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Api.Endpoints;

public static class LibraryEndpoints
{
    public static RouteGroupBuilder MapLibraryEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/scan", ScanLibrary)
            .WithName("ScanLibrary")
            .WithOpenApi()
            .Produces<ScanResultDto>()
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        group.MapGet("/entries", GetEntries)
            .WithName("GetEntries")
            .WithOpenApi()
            .Produces<PagedResult<SubtitleEntryDto>>();

        group.MapGet("/entries/{id:int}", GetEntryById)
            .WithName("GetEntryById")
            .WithOpenApi()
            .Produces<SubtitleEntryDto>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPatch("/entries/{id:int}/wanted", UpdateWantedStatus)
            .WithName("UpdateWantedStatus")
            .WithOpenApi()
            .Produces<SubtitleEntryDto>()
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> ScanLibrary(IMediaScannerService scannerService)
    {
        var result = await scannerService.ScanLibraryAsync();
        return Results.Ok(result);
    }

    private static async Task<IResult> GetEntries(
        ISubtitleEntryRepository repository,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] bool? isProcessed = null,
        [FromQuery] bool? isWanted = null,
        [FromQuery] bool? alreadyHas = null,
        [FromQuery] string? search = null)
    {
        var allEntries = await repository.GetAllAsync();

        // Apply filters
        var filtered = allEntries.AsEnumerable();

        if (isProcessed.HasValue)
            filtered = filtered.Where(e => e.IsProcessed == isProcessed.Value);

        if (isWanted.HasValue)
            filtered = filtered.Where(e => e.IsWanted == isWanted.Value);

        if (alreadyHas.HasValue)
            filtered = filtered.Where(e => e.AlreadyHas == alreadyHas.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            filtered = filtered.Where(e =>
                e.FileName.Contains(searchLower, StringComparison.CurrentCultureIgnoreCase) ||
                e.Series.Contains(searchLower, StringComparison.CurrentCultureIgnoreCase) ||
                e.Season.Contains(searchLower, StringComparison.CurrentCultureIgnoreCase));
        }

        // Apply pagination
        var subtitleEntryDtos = filtered as SubtitleEntryDto[] ?? filtered.ToArray();
        var total = subtitleEntryDtos.Length;
        var items = subtitleEntryDtos
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var result = new PagedResult<SubtitleEntryDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };

        return Results.Ok(result);
    }

    private static async Task<IResult> GetEntryById(int id, ISubtitleEntryRepository repository)
    {
        var entry = await repository.GetByIdAsync(id);
        return entry is not null ? Results.Ok(entry) : Results.NotFound();
    }

    private static async Task<IResult> UpdateWantedStatus(
        int id,
        [FromBody] UpdateWantedRequest request,
        ISubtitleEntryRepository repository)
    {
        var entry = await repository.GetByIdAsync(id);
        if (entry is null)
            return Results.NotFound();

        entry.IsWanted = request.IsWanted;
        var updated = await repository.UpdateAsync(entry);

        return Results.Ok(updated);
    }
}
