using XPXel.Core.Documents;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Formats.Png;

namespace XPXel.Formats.Tests;

/// <summary>
/// Tests for the PNG codec: round-tripping XP-xel's own output, and decoding the colour
/// types and bit depths that other editors produce.
/// </summary>
public sealed class PngCodecTests
{
    [Fact]
    public void RoundTripsEveryPixelExactly()
    {
        PixelBuffer original = CreateTestImage(23, 17);

        using MemoryStream stream = new();
        PngCodec.EncodeImage(stream, original);
        stream.Position = 0;
        PixelBuffer decoded = PngCodec.DecodeImage(stream);

        Assert.Equal(original.Size, decoded.Size);
        Assert.True(original.ContentEquals(decoded));
    }

    [Fact]
    public void RoundTripsSemiTransparentPixelsWithoutPrecisionLoss()
    {
        // This is the test that would fail if the editor stored premultiplied alpha.
        PixelBuffer original = new(1, 1);
        PixelColor faint = new(200, 100, 50, 3);
        original.SetPixel(0, 0, faint);

        using MemoryStream stream = new();
        PngCodec.EncodeImage(stream, original);
        stream.Position = 0;

        Assert.Equal(faint, PngCodec.DecodeImage(stream).GetPixel(0, 0));
    }

    [Fact]
    public void WritesAValidSignature()
    {
        using MemoryStream stream = new();
        PngCodec.EncodeImage(stream, new PixelBuffer(2, 2));

        Assert.True(new PngCodec().CanRead(stream.ToArray()));
    }

    [Fact]
    public void DecodesEightBitGreyscale()
    {
        byte[] scanlines = [0, 128, 255, 64];
        byte[] png = TestPngBuilder.Build(2, 2, bitDepth: 8, colorType: 0, scanlines);

        PixelBuffer image = Decode(png);

        Assert.Equal(new PixelColor(0, 0, 0), image.GetPixel(0, 0));
        Assert.Equal(new PixelColor(128, 128, 128), image.GetPixel(1, 0));
        Assert.Equal(new PixelColor(255, 255, 255), image.GetPixel(0, 1));
    }

    [Fact]
    public void DecodesOneBitGreyscale()
    {
        // Eight pixels packed into a single byte, most significant bit first.
        byte[] png = TestPngBuilder.Build(8, 1, bitDepth: 1, colorType: 0, [0b1010_0000]);

        PixelBuffer image = Decode(png);

        Assert.Equal(PixelColor.White, image.GetPixel(0, 0));
        Assert.Equal(PixelColor.Black, image.GetPixel(1, 0));
        Assert.Equal(PixelColor.White, image.GetPixel(2, 0));
        Assert.Equal(PixelColor.Black, image.GetPixel(7, 0));
    }

    [Fact]
    public void DecodesFourBitIndexedColourWithPaletteTransparency()
    {
        byte[] palette =
        [
            255, 0, 0,      // 0: red
            0, 255, 0,      // 1: green
            0, 0, 255,      // 2: blue
        ];

        // tRNS gives entry 0 an alpha of 0 and leaves the rest opaque by omission.
        byte[] transparency = [0];

        // Two 4-bit samples per byte: indices 0, 1 then 2, 0.
        byte[] png = TestPngBuilder.Build(2, 2, bitDepth: 4, colorType: 3, [0x01, 0x20], palette, transparency);

        PixelBuffer image = Decode(png);

        Assert.Equal(new PixelColor(255, 0, 0, 0), image.GetPixel(0, 0));
        Assert.Equal(new PixelColor(0, 255, 0), image.GetPixel(1, 0));
        Assert.Equal(new PixelColor(0, 0, 255), image.GetPixel(0, 1));
    }

    [Fact]
    public void DecodesTruecolourWithAColourKey()
    {
        byte[] scanlines = [255, 0, 255, 10, 20, 30];

        // tRNS for truecolour is three 16-bit samples naming the transparent colour.
        byte[] transparency = [0, 255, 0, 0, 0, 255];

        byte[] png = TestPngBuilder.Build(2, 1, bitDepth: 8, colorType: 2, scanlines, transparency: transparency);

        PixelBuffer image = Decode(png);

        Assert.Equal(0, image.GetPixel(0, 0).A);
        Assert.Equal(new PixelColor(10, 20, 30), image.GetPixel(1, 0));
    }

    [Fact]
    public void DecodesSixteenBitChannelsByTakingTheHighByte()
    {
        // One RGBA pixel at 16 bits per channel.
        byte[] scanlines = [0xAB, 0xCD, 0x12, 0x34, 0x56, 0x78, 0xFF, 0xFF];
        byte[] png = TestPngBuilder.Build(1, 1, bitDepth: 16, colorType: 6, scanlines);

        Assert.Equal(new PixelColor(0xAB, 0x12, 0x56, 0xFF), Decode(png).GetPixel(0, 0));
    }

    [Fact]
    public void RejectsInterlacedFiles()
    {
        byte[] png = TestPngBuilder.Build(1, 1, bitDepth: 8, colorType: 6, [1, 2, 3, 4], interlace: 1);

        ImageFormatException error = Assert.Throws<ImageFormatException>(() => Decode(png));
        Assert.Contains("Interlaced", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsACorruptChunk()
    {
        byte[] png = TestPngBuilder.Build(2, 2, bitDepth: 8, colorType: 0, [0, 1, 2, 3]);

        // Corrupt a byte inside the IHDR payload; its CRC no longer matches.
        png[20] ^= 0xFF;

        Assert.Throws<ImageFormatException>(() => Decode(png));
    }

    [Fact]
    public void RejectsAFileThatIsNotAPng()
    {
        Assert.Throws<ImageFormatException>(() => Decode("this is not a png"u8.ToArray()));
    }

    [Fact]
    public void ExportScalesWithNearestNeighbour()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(2, 2));
        Cel cel = document.GetOrCreateCel(document.Root.Children[0], document.Frames[0]);
        cel.Pixels.SetPixel(0, 0, new PixelColor(255, 0, 0));

        using MemoryStream stream = new();
        new PngCodec().Write(stream, document, new ExportOptions { Scale = 3 });
        stream.Position = 0;

        PixelBuffer exported = PngCodec.DecodeImage(stream);

        Assert.Equal(new ImageSize(6, 6), exported.Size);

        // The single red source pixel must become an exact 3x3 block with hard edges.
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                Assert.Equal(new PixelColor(255, 0, 0), exported.GetPixel(x, y));
            }
        }

        Assert.Equal(PixelColor.Transparent, exported.GetPixel(3, 0));
    }

    [Fact]
    public void ExportFlattensTheLayerStack()
    {
        PixelDocument document = new(new ImageSize(1, 1));
        RasterLayer bottom = document.CreateRasterLayer("Bottom");
        RasterLayer top = document.CreateRasterLayer("Top");
        document.GetOrCreateCel(bottom, document.Frames[0]).Pixels.Fill(new PixelColor(255, 0, 0));
        document.GetOrCreateCel(top, document.Frames[0]).Pixels.Fill(new PixelColor(0, 0, 255));

        using MemoryStream stream = new();
        new PngCodec().Write(stream, document, ExportOptions.Default);
        stream.Position = 0;

        Assert.Equal(new PixelColor(0, 0, 255), PngCodec.DecodeImage(stream).GetPixel(0, 0));
    }

    [Fact]
    public void ImportProducesASingleLayerDocument()
    {
        PixelBuffer original = CreateTestImage(5, 4);
        using MemoryStream stream = new();
        PngCodec.EncodeImage(stream, original);
        stream.Position = 0;

        PixelDocument document = new PngCodec().Read(stream, "sprite");

        Assert.Equal(original.Size, document.Size);
        Assert.Single(document.Root.Children);
        Assert.Equal("sprite", document.Root.Children[0].Name);

        Cel cel = document.Cels.Find(document.Root.Children[0].Id, document.Frames[0].Id)!;
        Assert.True(original.ContentEquals(cel.Pixels));
    }

    private static PixelBuffer Decode(byte[] png)
    {
        using MemoryStream stream = new(png);
        return PngCodec.DecodeImage(stream);
    }

    /// <summary>An image with varied colours and alphas, to exercise the scanline filters.</summary>
    private static PixelBuffer CreateTestImage(int width, int height)
    {
        PixelBuffer buffer = new(width, height);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                buffer.SetPixel(x, y, new PixelColor(
                    (byte)((x * 11) % 256),
                    (byte)((y * 23) % 256),
                    (byte)((x + y) * 7 % 256),
                    (byte)(((x * y) % 3) switch { 0 => 255, 1 => 128, _ => 0 })));
            }
        }

        return buffer;
    }
}
