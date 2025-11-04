using Translarr.Core.Application.Models;

namespace Translarr.Core.Api.Models;

public class TranslationStatus
{
    public bool IsRunning { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Progress { get; set; } = string.Empty;
    public TranslationResultDto? Result { get; set; }
    public string? Error { get; set; }

    // Detailed progress info
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public string? CurrentFileName { get; set; }
    public TranslationStep CurrentStep { get; set; }
}

