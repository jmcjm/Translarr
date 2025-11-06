namespace Translarr.Core.Application.Models;

public class SeriesGroupDto
{
    public required string SeriesName { get; set; }
    public List<SeasonGroupDto> Seasons { get; set; } = [];
    public int TotalFiles { get; set; }
    public int WantedFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public bool IsWatched { get; set; }
}

public class SeasonGroupDto
{
    public required string SeasonName { get; set; }
    public int TotalFiles { get; set; }
    public int WantedFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public bool IsWatched { get; set; }
}
