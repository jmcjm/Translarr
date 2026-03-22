using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Translarr.Core.Infrastructure.Services;

public record RenderedFrame(string FilePath, TimeSpan StartTime, TimeSpan EndTime, int Index);

public static class PgsRenderer
{
    public static List<RenderedFrame> RenderAll(List<PgsSubtitle> subtitles, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var frames = new List<RenderedFrame>();

        for (var i = 0; i < subtitles.Count; i++)
        {
            var sub = subtitles[i];
            if (sub.Bitmap == null || sub.Palette == null) continue;

            var startStr = FormatTimestamp(sub.StartTime);
            var endStr = FormatTimestamp(sub.EndTime);
            var filename = $"{i + 1:D4}_{startStr}_{endStr}.png";
            var outPath = Path.Combine(outputDir, filename);

            using var image = RleToBitmap(sub);
            image.SaveAsPng(outPath);

            frames.Add(new RenderedFrame(outPath, sub.StartTime, sub.EndTime, i + 1));
        }

        return frames;
    }

    static Image<Rgba32> RleToBitmap(PgsSubtitle sub)
    {
        var image = new Image<Rgba32>(sub.Width, sub.Height, new Rgba32(0, 0, 0, 255));

        if (sub.Bitmap == null || sub.Palette == null) return image;

        var pixels = DecodeRle(sub.Bitmap, sub.Width, sub.Height);

        for (var y = 0; y < sub.Height; y++)
        {
            for (var x = 0; x < sub.Width; x++)
            {
                var idx = pixels[y * sub.Width + x];
                if (idx < sub.Palette.Length)
                {
                    var color = sub.Palette[idx];
                    if (color.A > 0)
                        image[x, y] = color;
                }
            }
        }

        return image;
    }

    static byte[] DecodeRle(byte[] data, int width, int height)
    {
        var pixels = new byte[width * height];
        var ofs = 0;
        var xpos = 0;
        var i = 0;

        while (i < data.Length && ofs < pixels.Length)
        {
            var b = data[i++] & 0xFF;
            if (b != 0)
            {
                // Single pixel with color b
                pixels[ofs++] = (byte)b;
                xpos++;
            }
            else
            {
                if (i >= data.Length) break;
                b = data[i++] & 0xFF;

                if (b == 0)
                {
                    // 00 00 = end of line
                    ofs = (ofs / width) * width;
                    if (xpos < width)
                        ofs += width;
                    xpos = 0;
                }
                else if ((b & 0xC0) == 0x40)
                {
                    // 00 4x xx = xxx zeros (long transparent run)
                    if (i >= data.Length) break;
                    var size = ((b - 0x40) << 8) + (data[i++] & 0xFF);
                    for (var j = 0; j < size && ofs < pixels.Length; j++)
                        pixels[ofs++] = 0;
                    xpos += size;
                }
                else if ((b & 0xC0) == 0x80)
                {
                    // 00 8x yy = x pixels of color yy (short colored run)
                    if (i >= data.Length) break;
                    var size = b - 0x80;
                    var color = (byte)(data[i++] & 0xFF);
                    for (var j = 0; j < size && ofs < pixels.Length; j++)
                        pixels[ofs++] = color;
                    xpos += size;
                }
                else if ((b & 0xC0) != 0)
                {
                    // 00 cx yy zz = xyy pixels of color zz (long colored run)
                    if (i + 1 >= data.Length) break;
                    var size = ((b - 0xC0) << 8) + (data[i++] & 0xFF);
                    var color = (byte)(data[i++] & 0xFF);
                    for (var j = 0; j < size && ofs < pixels.Length; j++)
                        pixels[ofs++] = color;
                    xpos += size;
                }
                else
                {
                    // 00 xx = xx zeros (short transparent run)
                    for (var j = 0; j < b && ofs < pixels.Length; j++)
                        pixels[ofs++] = 0;
                    xpos += b;
                }
            }
        }

        return pixels;
    }

    static string FormatTimestamp(TimeSpan ts)
    {
        return $"{(int)ts.TotalHours:D2}{ts.Minutes:D2}{ts.Seconds:D2}{ts.Milliseconds:D3}";
    }
}
