namespace Translarr.Core.Application.Models;

public class SeriesWatchConfigDto
{
    public int Id { get; set; }
    public required string SeriesName { get; set; }
    public string? SeasonName { get; set; }
    public bool AutoWatch { get; set; }
    public DateTime CreatedAt { get; set; }
}
