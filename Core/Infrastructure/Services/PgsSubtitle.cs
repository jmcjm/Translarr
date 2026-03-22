using SixLabors.ImageSharp.PixelFormats;

namespace Translarr.Core.Infrastructure.Services;

public class PgsSubtitle
{
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public byte[]? Bitmap { get; set; }
    public Rgba32[]? Palette { get; set; }
}
