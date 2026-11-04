using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using MuslimMatrimony.Services;

namespace MuslimMatrimony.Tests;

public static class TestImages
{
    /// <summary>A tiny JPEG skeleton with an EXIF (APP1) segment containing a fake GPS marker and a comment.</summary>
    public static byte[] JpegWithExif()
    {
        var ms = new MemoryStream();
        ms.Write([0xFF, 0xD8]);
        Segment(ms, 0xE0, "JFIF\0\u0001\u0001\0\0\u0001\0\u0001\0\0"u8.ToArray());
        Segment(ms, 0xE1, "Exif\0\0GPSLatitude=59.3293"u8.ToArray());
        Segment(ms, 0xFE, "secret comment"u8.ToArray());
        Segment(ms, 0xDB, new byte[65]);
        ms.Write([0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00, 0x12, 0x34, 0x56, 0xFF, 0xD9]);
        return ms.ToArray();
    }

    private static void Segment(Stream s, byte marker, byte[] payload)
    {
        var len = payload.Length + 2;
        s.Write([0xFF, marker, (byte)(len >> 8), (byte)len]);
        s.Write(payload);
    }

    public static byte[] PngWithText()
    {
        var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(ms, "IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0]);
        Chunk(ms, "tEXt", "Comment\0GPS 59.3293"u8.ToArray());
        Chunk(ms, "eXIf", "MM\0*"u8.ToArray());
        Chunk(ms, "IDAT", ZLibEmpty());
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static byte[] ZLibEmpty()
    {
        var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            z.Write([0, 0, 0, 0]);
        }

        return ms.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
        s.Write(len);
        s.Write(Encoding.ASCII.GetBytes(type));
        s.Write(data);
        s.Write([0, 0, 0, 0]); // CRC is not checked by the sanitizer
    }

    public static byte[] WebpWithExif()
    {
        var body = new MemoryStream();
        RiffChunk(body, "VP8X", [0x08 | 0x04, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        RiffChunk(body, "VP8 ", new byte[11]);
        RiffChunk(body, "EXIF", "GPSLatitude"u8.ToArray());
        RiffChunk(body, "XMP ", "<x:xmpmeta/>"u8.ToArray());
        var ms = new MemoryStream();
        ms.Write("RIFF"u8);
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)(4 + body.Length));
        ms.Write(size);
        ms.Write("WEBP"u8);
        ms.Write(body.ToArray());
        return ms.ToArray();
    }

    private static void RiffChunk(Stream s, string type, byte[] data)
    {
        s.Write(Encoding.ASCII.GetBytes(type));
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)data.Length);
        s.Write(size);
        s.Write(data);
        if (data.Length % 2 == 1)
        {
            s.WriteByte(0);
        }
    }

    public static bool Contains(byte[] haystack, string needle) => haystack.AsSpan().IndexOf(Encoding.ASCII.GetBytes(needle)) >= 0;
}

public class ImageSanitizerTests
{
    [Fact]
    public void Jpeg_metadata_is_removed_and_image_data_kept()
    {
        var input = TestImages.JpegWithExif();
        var result = ImageSanitizer.Sanitize(input);

        Assert.NotNull(result);
        Assert.Equal("image/jpeg", result.ContentType);
        Assert.False(TestImages.Contains(result.Data, "GPSLatitude"));
        Assert.False(TestImages.Contains(result.Data, "secret comment"));
        Assert.True(TestImages.Contains(result.Data, "JFIF"));
        Assert.Equal(input[^15..], result.Data[^15..]); // scan data untouched
    }

    [Fact]
    public void Png_text_and_exif_chunks_are_removed()
    {
        var result = ImageSanitizer.Sanitize(TestImages.PngWithText());

        Assert.NotNull(result);
        Assert.Equal("image/png", result.ContentType);
        Assert.False(TestImages.Contains(result.Data, "tEXt"));
        Assert.False(TestImages.Contains(result.Data, "eXIf"));
        Assert.True(TestImages.Contains(result.Data, "IDAT"));
        Assert.True(TestImages.Contains(result.Data, "IEND"));
    }

    [Fact]
    public void Webp_exif_and_xmp_are_removed_and_header_fixed()
    {
        var result = ImageSanitizer.Sanitize(TestImages.WebpWithExif());

        Assert.NotNull(result);
        Assert.Equal("image/webp", result.ContentType);
        Assert.False(TestImages.Contains(result.Data, "GPSLatitude"));
        Assert.False(TestImages.Contains(result.Data, "xmpmeta"));
        Assert.Equal((uint)(result.Data.Length - 8), BinaryPrimitives.ReadUInt32LittleEndian(result.Data.AsSpan(4)));
        Assert.Equal(0, result.Data[20] & 0x0C); // VP8X flags cleared
    }

    [Theory]
    [InlineData("GIF89a............")]
    [InlineData("<html><script>alert(1)</script></html>")]
    [InlineData("")]
    public void Non_images_are_rejected(string content) => Assert.Null(ImageSanitizer.Sanitize(Encoding.ASCII.GetBytes(content)));

    [Fact]
    public void Truncated_files_are_rejected()
    {
        var jpeg = TestImages.JpegWithExif();
        Assert.Null(ImageSanitizer.Sanitize(jpeg[..30]));
        var png = TestImages.PngWithText();
        Assert.Null(ImageSanitizer.Sanitize(png[..40]));
    }

    [Theory]
    [InlineData("2000-06-15", "2018-06-14", 17)]
    [InlineData("2000-06-15", "2018-06-15", 18)]
    [InlineData("2000-02-29", "2019-02-28", 18)]
    public void Age_is_computed_on_birthdays(string dob, string today, int expected) =>
        Assert.Equal(expected, Age.On(DateOnly.Parse(dob, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture)));
}
