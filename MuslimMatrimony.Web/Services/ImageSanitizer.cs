using System.Buffers.Binary;
using System.Text;

namespace MuslimMatrimony.Services;

/// <summary>
/// Identifies JPEG/PNG/WebP by content and removes embedded metadata (EXIF incl. GPS location,
/// XMP, comments, text chunks) without re-encoding. Anything malformed is rejected.
/// </summary>
public static class ImageSanitizer
{
    public sealed record Result(byte[] Data, string ContentType, string Extension);

    public static Result? Sanitize(byte[] data)
    {
        try
        {
            if (data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            {
                return Wrap(StripJpeg(data), "image/jpeg", ".jpg");
            }

            if (data.Length > 8 && data.AsSpan(0, 8).SequenceEqual(PngSignature))
            {
                return Wrap(StripPng(data), "image/png", ".png");
            }

            if (data.Length > 12 && Ascii(data, 0, 4) == "RIFF" && Ascii(data, 8, 4) == "WEBP")
            {
                return Wrap(StripWebp(data), "image/webp", ".webp");
            }
        }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or InvalidDataException)
        {
            return null;
        }

        return null;
    }

    private static Result? Wrap(byte[]? data, string type, string ext) => data is null ? null : new Result(data, type, ext);

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static string Ascii(byte[] d, int offset, int count) => Encoding.ASCII.GetString(d, offset, count);

    /// <summary>Drops APP1–APP13, APP15 (EXIF, XMP, IPTC…) and COM. Keeps APP0 (JFIF) and APP14 (Adobe colour info).</summary>
    private static byte[]? StripJpeg(byte[] d)
    {
        using var output = new MemoryStream(d.Length);
        output.Write(d, 0, 2);
        var i = 2;
        while (i + 4 <= d.Length)
        {
            if (d[i] != 0xFF)
            {
                return null;
            }

            var marker = d[i + 1];
            if (marker == 0xFF)
            {
                i++; // fill byte
                continue;
            }

            if (marker == 0xDA)
            {
                // Start of scan: the rest is entropy-coded image data.
                output.Write(d, i, d.Length - i);
                return output.ToArray();
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                output.Write(d, i, 2);
                i += 2;
                continue;
            }

            var length = (d[i + 2] << 8) | d[i + 3];
            if (length < 2 || i + 2 + length > d.Length)
            {
                return null;
            }

            var isMetadata = marker == 0xFE || (marker >= 0xE1 && marker <= 0xEF && marker != 0xEE);
            if (!isMetadata)
            {
                output.Write(d, i, 2 + length);
            }

            i += 2 + length;
        }

        return null; // no image data
    }

    private static readonly HashSet<string> PngMetadataChunks = ["tEXt", "zTXt", "iTXt", "eXIf", "tIME"];

    private static byte[]? StripPng(byte[] d)
    {
        using var output = new MemoryStream(d.Length);
        output.Write(PngSignature);
        var i = 8;
        var sawEnd = false;
        while (i + 12 <= d.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(i));
            if (length > int.MaxValue || i + 12 + (long)length > d.Length)
            {
                return null;
            }

            var type = Ascii(d, i + 4, 4);
            var total = 12 + (int)length;
            if (!PngMetadataChunks.Contains(type))
            {
                output.Write(d, i, total);
            }

            i += total;
            if (type == "IEND")
            {
                sawEnd = true;
                break;
            }
        }

        return sawEnd ? output.ToArray() : null;
    }

    private static byte[]? StripWebp(byte[] d)
    {
        var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(4));
        var end = (int)Math.Min(d.Length, 8L + riffSize);
        var chunks = new List<(string Type, int Offset, int Total)>();
        var i = 12;
        while (i + 8 <= end)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(i + 4));
            var padded = 8L + size + (size & 1);
            if (i + padded > end)
            {
                return null;
            }

            chunks.Add((Ascii(d, i, 4), i, (int)padded));
            i += (int)padded;
        }

        if (!chunks.Any(c => c.Type is "VP8 " or "VP8L" or "VP8X"))
        {
            return null;
        }

        using var body = new MemoryStream(d.Length);
        foreach (var (type, offset, total) in chunks)
        {
            if (type is "EXIF" or "XMP ")
            {
                continue;
            }

            var chunk = d.AsSpan(offset, total).ToArray();
            if (type == "VP8X" && chunk.Length > 8)
            {
                chunk[8] &= unchecked((byte)~0x0C); // clear EXIF (0x08) and XMP (0x04) flags
            }

            body.Write(chunk);
        }

        using var output = new MemoryStream();
        output.Write("RIFF"u8);
        Span<byte> size4 = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size4, (uint)(4 + body.Length));
        output.Write(size4);
        output.Write("WEBP"u8);
        body.Position = 0;
        body.CopyTo(output);
        return output.ToArray();
    }
}
