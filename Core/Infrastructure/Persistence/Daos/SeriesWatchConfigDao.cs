namespace Translarr.Core.Infrastructure.Persistence.Daos;

public class SeriesWatchConfigDao
{
    public int Id { get; set; }
    public required string SeriesName { get; set; }
    public string? SeasonName { get; set; }
    public bool AutoWatch { get; set; }
    public DateTime CreatedAt { get; set; }
}
