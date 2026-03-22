using Microsoft.AspNetCore.Mvc;
using Translarr.Core.Api.Models;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Api.Endpoints;

public static class TranslationEndpoints
{
    private static TranslationStatus? _currentStatus;
    private static readonly Lock StatusLock = new();
    private static CancellationTokenSource? _cancellationTokenSource;

    private static TranslationStatus? _currentBitmapStatus;
    private static readonly Lock BitmapStatusLock = new();
    private static CancellationTokenSource? _bitmapCancellationTokenSource;

    public static RouteGroupBuilder MapTranslationEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/translate", StartTranslation)
            .WithName("StartTranslation")
            .Produces<TranslationResultDto>()
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        group.MapPost("/cancel", CancelTranslation)
            .WithName("CancelTranslation")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/status", GetTranslationStatus)
            .WithName("GetTranslationStatus")
            .Produces<TranslationStatus>();

        group.MapPost("/translate-bitmap", StartBitmapTranslation)
            .WithName("StartBitmapTranslation")
            .Produces<TranslationResultDto>()
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        group.MapGet("/bitmap-status", GetBitmapTranslationStatus)
            .WithName("GetBitmapTranslationStatus")
            .Produces<TranslationStatus>();

        group.MapPost("/cancel-bitmap", CancelBitmapTranslation)
            .WithName("CancelBitmapTranslation")
            .Produces(StatusCodes.Status200OK);

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

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();

            _currentStatus = new TranslationStatus
            {
                IsRunning = true,
                StartedAt = DateTime.UtcNow,
                Progress = "Starting translation..."
            };
        }

        var cts = _cancellationTokenSource;

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

                var result = await translationService.TranslateNextBatchAsync(batchSize, OnProgressUpdate, cts.Token);

                var wasCancelled = cts.IsCancellationRequested;

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

    private static IResult CancelTranslation()
    {
        lock (StatusLock)
        {
            if (_currentStatus?.IsRunning != true)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "No translation in progress",
                    Detail = "There is no running translation to cancel."
                });
            }

            _cancellationTokenSource?.Cancel();
            _currentStatus.Progress = "Cancelling...";

            return Results.Ok(new { Message = "Cancellation requested" });
        }
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

    private static IResult StartBitmapTranslation(
        [FromQuery] int batchSize,
        IServiceScopeFactory serviceScopeFactory)
    {
        lock (BitmapStatusLock)
        {
            if (_currentBitmapStatus?.IsRunning == true)
            {
                return Results.Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Bitmap translation already in progress",
                    Detail = "A bitmap translation job is already running. Please wait for it to complete."
                });
            }

            _bitmapCancellationTokenSource?.Dispose();
            _bitmapCancellationTokenSource = new CancellationTokenSource();

            _currentBitmapStatus = new TranslationStatus
            {
                IsRunning = true,
                StartedAt = DateTime.UtcNow,
                Progress = "Starting bitmap translation..."
            };
        }

        var cts = _bitmapCancellationTokenSource;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = serviceScopeFactory.CreateScope();
                var translationService = scope.ServiceProvider.GetRequiredService<IBitmapTranslationService>();

                void OnProgressUpdate(TranslationProgressUpdate update)
                {
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
                        }
                    }
                }

                var result = await translationService.TranslateBitmapBatchAsync(batchSize, OnProgressUpdate, cts.Token);

                var wasCancelled = cts.IsCancellationRequested;

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
                }
            }
            catch (Exception ex)
            {
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
                }
            }
        });

        return Results.Accepted("/api/translation/bitmap-status", new
        {
            Message = "Bitmap translation started",
            StatusUrl = "/api/translation/bitmap-status"
        });
    }

    private static IResult GetBitmapTranslationStatus()
    {
        lock (BitmapStatusLock)
        {
            if (_currentBitmapStatus is null)
            {
                return Results.Ok(new TranslationStatus
                {
                    IsRunning = false,
                    Progress = "No bitmap translation running"
                });
            }

            return Results.Ok(_currentBitmapStatus);
        }
    }

    private static IResult CancelBitmapTranslation()
    {
        lock (BitmapStatusLock)
        {
            if (_currentBitmapStatus?.IsRunning != true)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "No bitmap translation in progress",
                    Detail = "There is no running bitmap translation to cancel."
                });
            }

            _bitmapCancellationTokenSource?.Cancel();
            _currentBitmapStatus.Progress = "Cancelling...";

            return Results.Ok(new { Message = "Cancellation requested" });
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
            TranslationStep.TranslatingWithLlm => "Translating with LLM",
            TranslationStep.SavingSubtitles => "Saving subtitles",
            TranslationStep.Completed => "Completed",
            _ => "Processing"
        };

        var batchInfo = update.TotalBatches > 0 ? $" (batch {update.CurrentBatch}/{update.TotalBatches})" : "";
        return $"[{update.ProcessedFiles + 1}/{update.TotalFiles}] {stepText}{batchInfo}: {update.CurrentFileName}";
    }
}
