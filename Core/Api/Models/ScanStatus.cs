using Translarr.Core.Application.Models;

namespace Translarr.Core.Api.Models;

public class ScanStatus
{
    public bool IsRunning { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Progress { get; set; } = string.Empty;
    public ScanResultDto? Result { get; set; }
    public string? Error { get; set; }

    // Detailed progress info
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public string? CurrentFileName { get; set; }
    public ScanStep CurrentStep { get; set; }

    public ScanStatus Snapshot() => new()
    {
        IsRunning = IsRunning,
        StartedAt = StartedAt,
        CompletedAt = CompletedAt,
        Progress = Progress,
        Result = Result,
        Error = Error,
        TotalFiles = TotalFiles,
        ProcessedFiles = ProcessedFiles,
        CurrentFileName = CurrentFileName,
        CurrentStep = CurrentStep
    };
}
