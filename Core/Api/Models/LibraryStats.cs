namespace Translarr.Core.Api.Models;

public class LibraryStats
{
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public int UnprocessedFiles { get; set; }
    public int WantedFiles { get; set; }
    public int AlreadyHasFiles { get; set; }
    public int ErrorFiles { get; set; }
    public DateTime? LastScanned { get; set; }
}

