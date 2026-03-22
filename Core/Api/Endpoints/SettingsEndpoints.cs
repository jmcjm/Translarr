using Microsoft.AspNetCore.Mvc;
using Translarr.Core.Api.Models;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Api.Endpoints;

public static class SettingsEndpoints
{
    public static RouteGroupBuilder MapSettingsEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", GetAllSettings)
            .WithName("GetAllSettings")
            .Produces<List<AppSettingDto>>();

        group.MapGet("/{key}", GetSetting)
            .WithName("GetSetting")
            .Produces<AppSettingDto>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/{key}", UpdateSetting)
            .WithName("UpdateSetting")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/test-api", TestApiConnection)
            .WithName("TestApiConnection")
            .Produces<ApiTestResult>()
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        return group;
    }

    private static async Task<IResult> GetAllSettings(ISettingsService settingsService)
    {
        var settings = await settingsService.GetAllSettingsAsync();
        return Results.Ok(settings);
    }

    private static async Task<IResult> GetSetting(string key, ISettingsService settingsService)
    {
        var settings = await settingsService.GetAllSettingsAsync();
        var setting = settings.FirstOrDefault(s => s.Key == key);

        return setting is not null ? Results.Ok(setting) : Results.NotFound();
    }

    private static async Task<IResult> UpdateSetting(
        string key,
        [FromBody] UpdateSettingRequest request,
        ISettingsService settingsService)
    {
        try
        {
            await settingsService.UpdateSettingAsync(key, request.Value);
            return Results.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> TestApiConnection(ISettingsService settingsService, ISubtitleTranslator translator)
    {
        try
        {
            var settings = await settingsService.GetLlmSettingsAsync();
            // Send a minimal test prompt
            await translator.TranslateSubtitlesAsync("1\n00:00:01,000 --> 00:00:02,000\nTest", settings);
            return Results.Ok(new { Success = true, Message = "Connection successful" });
        }
        catch (Exception ex)
        {
            return Results.Ok(new { Success = false, Message = ex.Message });
        }
    }
}

