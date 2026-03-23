namespace Translarr.Core.Application.Models;

public class SeasonDetailDto
{
    public string SeasonName { get; set; } = "";
    public bool IsWatched { get; set; }
    public int TotalFiles { get; set; }
    public int WantedFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public List<SubtitleEntryDto> Entries { get; set; } = [];
}
