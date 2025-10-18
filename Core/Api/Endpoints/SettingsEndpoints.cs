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
            .WithOpenApi()
            .Produces<List<AppSettingDto>>();

        group.MapGet("/{key}", GetSetting)
            .WithName("GetSetting")
            .WithOpenApi()
            .Produces<AppSettingDto>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/{key}", UpdateSetting)
            .WithName("UpdateSetting")
            .WithOpenApi()
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/test-api", TestApiConnection)
            .WithName("TestApiConnection")
            .WithOpenApi()
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
            // Get settings for the test
            var systemPrompt = await settingsService.GetSettingAsync("SystemPrompt") 
                ?? "You are a helpful assistant.";
            var temperatureStr = await settingsService.GetSettingAsync("Temperature") ?? "0.55";
            var model = await settingsService.GetSettingAsync("GeminiModel") ?? "gemini-2.5-pro";
            
            TryParse(temperatureStr, out var temperature);
            
            // Try to make a simple test request to Gemini API
            const string testPrompt = "Hello, this is a test. Please respond with 'Your connection is working!'.";
            
            var response = await geminiClient.TranslateSubtitlesAsync(
                testPrompt, 
                systemPrompt, 
                temperature, 
                model);

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

