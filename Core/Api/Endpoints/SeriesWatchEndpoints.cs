using Microsoft.AspNetCore.Mvc;
using Translarr.Core.Api.Helpers;
using Translarr.Core.Api.Models;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Api.Endpoints;

public static class SeriesWatchEndpoints
{
    public static RouteGroupBuilder MapSeriesWatchEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/watch-configs", GetAllWatchConfigs)
            .WithName("GetAllWatchConfigs")
            .Produces<List<SeriesWatchConfigDto>>();

        group.MapPut("/watch-configs", SetAutoWatch)
            .WithName("SetAutoWatch")
            .Produces<SetAutoWatchResult>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        group.MapDelete("/watch-configs", RemoveAutoWatch)
            .WithName("RemoveAutoWatch")
            .Produces<bool>()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapGet("/series", GetSeriesGroups)
            .WithName("GetSeriesGroups")
            .Produces<List<SeriesGroupDto>>();

        return group;
    }

    private static async Task<IResult> GetAllWatchConfigs([FromServices] ISeriesWatchService service)
    {
        var configs = await service.GetAllWatchConfigsAsync();
        return Results.Ok(configs);
    }

    private static async Task<IResult> SetAutoWatch(
        [FromQuery] string series,
        [FromQuery] string? season,
        [FromQuery] bool autoWatch,
        [FromServices] ISeriesWatchService service)
    {
        var result = await service.SetAutoWatchAsync(series, season, autoWatch);

        if (result.IsError)
            return ErrorTypeMapper.MapErrorsToProblemResponse(result);

        return Results.Ok(new SetAutoWatchResult(result.Value));
    }

    private static async Task<IResult> RemoveAutoWatch(
        [FromQuery] string series,
        [FromQuery] string? season,
        [FromServices] ISeriesWatchService service)
    {
        var result = await service.RemoveAutoWatchAsync(series, season);

        if (result.IsError)
            return ErrorTypeMapper.MapErrorsToProblemResponse(result);

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> GetSeriesGroups([FromServices] ISeriesWatchService service)
    {
        var groups = await service.GetSeriesGroupsWithWatchStatusAsync();
        return Results.Ok(groups);
    }
}
