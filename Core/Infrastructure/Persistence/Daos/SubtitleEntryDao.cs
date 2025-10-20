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
    public bool AlreadyHad { get; set; }
    public bool ForceProcess { get; set; }
    public DateTime LastScanned { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

