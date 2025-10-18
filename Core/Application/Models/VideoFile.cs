namespace Translarr.Core.Application.Models;

public class VideoFile
{
    public required string FilePath { get; set; }
    public required string FileName { get; set; }
    public required string SeriesNumber { get; set; }
    public required string SeasonNumber { get; set; }
}

