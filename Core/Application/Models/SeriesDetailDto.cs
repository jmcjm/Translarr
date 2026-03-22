namespace Translarr.Core.Application.Models;

public class SeriesDetailDto
{
    public string SeriesName { get; set; } = "";
    public bool IsWatched { get; set; }
    public List<SeasonDetailDto> Seasons { get; set; } = [];
}
