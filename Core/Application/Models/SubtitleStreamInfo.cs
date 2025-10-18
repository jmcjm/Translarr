namespace Translarr.Core.Application.Models;

public class SubtitleStreamInfo
{
    public int StreamIndex { get; set; }
    public required string Language { get; set; }
    public required string CodecName { get; set; }
    public bool IsSdh { get; set; }
}

