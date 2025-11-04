using Microsoft.AspNetCore.Mvc;
using Translarr.Core.Api.Models;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Api.Endpoints;

public static class TranslationEndpoints
{
    private static TranslationStatus? _currentStatus;
    private static readonly Lock StatusLock = new();

    public static RouteGroupBuilder MapTranslationEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/translate", StartTranslation)
            .WithName("StartTranslation")
            .WithOpenApi()
            .Produces<TranslationResultDto>()
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        group.MapGet("/status", GetTranslationStatus)
            .WithName("GetTranslationStatus")
            .WithOpenApi()
            .Produces<TranslationStatus>();

        return group;
    }

    private static IResult StartTranslation(
        [FromQuery] int batchSize,
        IServiceScopeFactory serviceScopeFactory)
    {
        // Check if translation is already running
        lock (StatusLock)
        {
            if (_currentStatus?.IsRunning == true)
            {
                return Results.Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Translation already in progress",
                    Detail = "A translation job is already running. Please wait for it to complete."
                });
            }

            _currentStatus = new TranslationStatus
            {
                IsRunning = true,
                StartedAt = DateTime.UtcNow,
                Progress = "Starting translation..."
            };
        }

        // Start translation asynchronously with its own scope
        _ = Task.Run(async () =>
        {
            try
            {
                // Create new scope for background task
                using var scope = serviceScopeFactory.CreateScope();
                var translationService = scope.ServiceProvider.GetRequiredService<ISubtitleTranslationService>();

                // Progress callback that updates _currentStatus
                void OnProgressUpdate(TranslationProgressUpdate update)
                {
                    lock (StatusLock)
                    {
                        if (_currentStatus != null)
                        {
                            _currentStatus.TotalFiles = update.TotalFiles;
                            _currentStatus.ProcessedFiles = update.ProcessedFiles;
                            _currentStatus.CurrentFileName = update.CurrentFileName;
                            _currentStatus.CurrentStep = update.CurrentStep;
                            _currentStatus.Progress = FormatProgress(update);
                        }
                    }
                }

                var result = await translationService.TranslateNextBatchAsync(batchSize, OnProgressUpdate);

                lock (StatusLock)
                {
                    _currentStatus = new TranslationStatus
                    {
                        IsRunning = false,
                        StartedAt = _currentStatus.StartedAt,
                        CompletedAt = DateTime.UtcNow,
                        Progress = "Completed",
                        CurrentStep = TranslationStep.Completed,
                        TotalFiles = _currentStatus.TotalFiles,
                        ProcessedFiles = _currentStatus.TotalFiles,
                        Result = result
                    };
                }
            }
            catch (Exception ex)
            {
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
                }
            }
        });

        return Results.Accepted("/api/translation/status", new
        {
            Message = "Translation started",
            StatusUrl = "/api/translation/status"
        });
    }

    private static IResult GetTranslationStatus()
    {
        lock (StatusLock)
        {
            if (_currentStatus is null)
            {
                return Results.Ok(new TranslationStatus
                {
                    IsRunning = false,
                    Progress = "No translation running"
                });
            }

            return Results.Ok(_currentStatus);
        }
    }

    private static string FormatProgress(TranslationProgressUpdate update)
    {
        var stepText = update.CurrentStep switch
        {
            TranslationStep.Starting => "Starting",
            TranslationStep.CheckingRateLimit => "Checking rate limit",
            TranslationStep.FindingSubtitles => "Finding subtitles",
            TranslationStep.ExtractingSubtitles => "Extracting subtitles",
            TranslationStep.CleaningSubtitles => "Cleaning subtitles",
            TranslationStep.ValidatingSize => "Validating size",
            TranslationStep.TranslatingWithGemini => "Translating with Gemini",
            TranslationStep.SavingSubtitles => "Saving subtitles",
            TranslationStep.Completed => "Completed",
            _ => "Processing"
        };

        return $"[{update.ProcessedFiles + 1}/{update.TotalFiles}] {stepText}: {update.CurrentFileName}";
    }
}

