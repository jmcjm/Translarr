using SixLabors.ImageSharp.PixelFormats;

namespace Translarr.Core.Infrastructure.Services;

public static class PgsParser
{
    const byte PDS = 0x14; // Palette Definition Segment
    const byte ODS = 0x15; // Object Definition Segment
    const byte PCS = 0x16; // Presentation Composition Segment
    const byte END = 0x80; // End of Display Set Segment

    public static List<PgsSubtitle> Parse(string path)
    {
        var result = new List<PgsSubtitle>();
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        PgsSubtitle? current = null;
        Rgba32[]? currentPalette = null;
        using var objectData = new MemoryStream();
        int objWidth = 0, objHeight = 0;

        while (stream.Position < stream.Length)
        {
            if (stream.Length - stream.Position < 13) break;

            var magic1 = reader.ReadByte();
            var magic2 = reader.ReadByte();
            if (magic1 != 0x50 || magic2 != 0x47)
                break;

            var pts = ReadUInt32BE(reader);
            ReadUInt32BE(reader); // DTS - not used but must be read to advance stream
            var segType = reader.ReadByte();
            var segSize = ReadUInt16BE(reader);

            if (stream.Length - stream.Position < segSize) break;
            var segData = reader.ReadBytes(segSize);

            switch (segType)
            {
                case PCS:
                    ParsePcs(segData, pts, ref current, result);
                    break;
                case PDS:
                    currentPalette = ParsePds(segData);
                    break;
                case ODS:
                    ParseOds(segData, objectData, ref objWidth, ref objHeight);
                    break;
                case END:
                    if (current != null)
                    {
                        current.Palette = currentPalette;
                        if (objectData.Length > 0)
                        {
                            current.Bitmap = objectData.ToArray();
                            current.Width = objWidth;
                            current.Height = objHeight;
                        }
                    }
                    break;
            }
        }

        for (var i = 0; i < result.Count - 1; i++)
        {
            if (result[i].EndTime == TimeSpan.Zero)
                result[i].EndTime = result[i + 1].StartTime;
        }
        if (result.Count > 0 && result[^1].EndTime == TimeSpan.Zero)
            result[^1].EndTime = result[^1].StartTime + TimeSpan.FromSeconds(3);

        return result.Where(s => s.Bitmap is { Length: > 0 }).ToList();
    }

    static void ParsePcs(byte[] data, uint pts, ref PgsSubtitle? current, List<PgsSubtitle> result)
    {
        if (data.Length < 11) return;

        var compState = data[7];
        var numObjects = data[10];

        if (compState == 0x00 && numObjects == 0)
        {
            if (current != null)
                current.EndTime = TimeSpan.FromMilliseconds(pts / 90.0);
            current = null;
            return;
        }

        current = new PgsSubtitle
        {
            StartTime = TimeSpan.FromMilliseconds(pts / 90.0)
        };
        result.Add(current);
    }

    static Rgba32[] ParsePds(byte[] data)
    {
        // All entries start transparent. Index 0xFF is always transparent by spec.
        var palette = new Rgba32[256];

        var i = 2;
        while (i + 4 < data.Length)
        {
            var idx = data[i];
            var y = data[i + 1];
            var cb = data[i + 2];
            var cr = data[i + 3];
            var alpha = data[i + 4];

            var r = (byte)Math.Clamp((int)(y + 1.402 * (cr - 128)), 0, 255);
            var g = (byte)Math.Clamp((int)(y - 0.34414 * (cb - 128) - 0.71414 * (cr - 128)), 0, 255);
            var b = (byte)Math.Clamp((int)(y + 1.772 * (cb - 128)), 0, 255);

            palette[idx] = new Rgba32(r, g, b, alpha);
            i += 5;
        }

        return palette;
    }

    static void ParseOds(byte[] data, MemoryStream objectData, ref int width, ref int height)
    {
        if (data.Length < 4) return;

        var seqFlag = data[3];

        if ((seqFlag & 0x80) != 0)
        {
            if (data.Length < 11) return;
            width = (data[7] << 8) | data[8];
            height = (data[9] << 8) | data[10];
            objectData.SetLength(0);
            objectData.Write(data, 11, data.Length - 11);
        }
        else
        {
            objectData.Write(data, 4, data.Length - 4);
        }
    }

    static uint ReadUInt32BE(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        return (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
    }

    static ushort ReadUInt16BE(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(2);
        return (ushort)((bytes[0] << 8) | bytes[1]);
    }
}
