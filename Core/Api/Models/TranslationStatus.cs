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
}

