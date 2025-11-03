namespace Translarr.Core.Application.Models;

public class TranslationResultDto
{
    public int SuccessCount { get; set; }
    public int SkippedNoSubtitles { get; set; }
    public int ErrorCount { get; set; }
    public TimeSpan Duration { get; set; }
    public List<string> Errors { get; set; } = [];
}

