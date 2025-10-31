namespace Translarr.Core.Application.Models;

public class ScanResultDto
{
    public int NewFiles { get; set; }
    public int UpdatedFiles { get; set; }
    public int RemovedFiles { get; set; }
    public int ErrorFiles { get; set; }
    public TimeSpan Duration { get; set; }
    public List<string> Errors { get; set; } = new();
}

