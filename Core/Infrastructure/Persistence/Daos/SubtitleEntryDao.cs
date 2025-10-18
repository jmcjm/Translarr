namespace Translarr.Core.Infrastructure.Persistence.Daos;

public class SubtitleEntryDao
{
    public int Id { get; set; }
    public required string Series { get; set; }
    public required string Season { get; set; }
    public required string FileName { get; set; }
    public required string FilePath { get; set; }
    public bool IsProcessed { get; set; }
    public bool IsWanted { get; set; }
    // The name is misleading; it should be "AlreadyHad" because it refers to the state at the time of the library scan, indicating that it doesn't need to be processed.
    public bool AlreadyHas { get; set; }
    public DateTime LastScanned { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

