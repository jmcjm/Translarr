using Microsoft.AspNetCore.Mvc;
using Translarr.Core.Api.Helpers;
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
        
        group.MapPatch("/entries/{id:int}/force", UpdateForceProcessStatus)
            .WithName("UpdateForceProcessStatus")
            .WithOpenApi()
            .Produces<SubtitleEntryDto>()
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> ScanLibrary([FromServices] IMediaScannerService scannerService)
    {
        var result = await scannerService.ScanLibraryAsync();
        return Results.Ok(result);
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
}
