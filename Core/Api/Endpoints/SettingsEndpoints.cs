using Microsoft.AspNetCore.Mvc;
using Translarr.Core.Api.Models;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Models;
using static System.Single;

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

    private static async Task<IResult> TestApiConnection(
        IGeminiClient geminiClient,
        ISettingsService settingsService)
    {
        try
        {
            // Get Gemini settings
            var settings = await settingsService.GetGeminiSettingsAsync();

            // Try to make a simple test request to Gemini API
            const string testPrompt = "Hello, this is a test. Please respond with 'Your connection is working!'.";

            var response = await geminiClient.TranslateSubtitlesAsync(testPrompt, settings);

            var result = new ApiTestResult
            {
                Success = true,
                Message = "API connection successful",
                Response = response
            };

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            var result = new ApiTestResult
            {
                Success = false,
                Message = $"API connection failed: {ex.Message}",
                Response = null
            };

            return Results.Ok(result);
        }
    }
}

