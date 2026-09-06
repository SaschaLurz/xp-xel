using System.Text;
using System.Text.Json;
using XPXel.Core.Documents;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Formats.Animation;
using XPXel.Formats.Png;

namespace XPXel.Formats.Tests;

/// <summary>
/// Tests for the animation exporters.
/// </summary>
/// <remarks>
/// The sheet tests check geometry and metadata rather than pixels, because an incorrectly
/// placed cell is the failure that matters: a sheet whose cells are one pixel out still looks
/// plausible on screen and imports into an engine as a sprite with a stripe of its neighbour
/// down the edge.
/// </remarks>
public sealed class AnimationExportTests
{
    private static readonly PixelColor Red = new(255, 0, 0);
    private static readonly PixelColor Blue = new(0, 0, 255);
    private static readonly PixelColor Green = new(0, 255, 0);

    /// <summary>Builds a document whose frames are each filled with a distinct colour.</summary>
    private static PixelDocument CreateDocument(int frames, int size = 4)
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(size, size));
        LayerNode layer = document.Root.Children[0];

        PixelColor[] palette = [Red, Blue, Green];

        for (int i = 0; i < frames; i++)
        {
            Frame frame = i == 0 ? document.Frames[0] : document.CreateFrame();
            document.GetOrCreateCel(layer, frame).Pixels.Fill(palette[i % palette.Length]);
        }

        return document;
    }

    private static ExportArtifact Single(IReadOnlyList<ExportArtifact> artifacts, string extension) =>
        artifacts.Single(a => a.FileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    // ---------------------------------------------------------------------------------
    // Sprite sheet
    // ---------------------------------------------------------------------------------

    [Fact]
    public void ASheetWritesAnImageAndItsMetadata()
    {
        PixelDocument document = CreateDocument(4);

        IReadOnlyList<ExportArtifact> artifacts = new SpriteSheetExporter()
            .Export(document, new AnimationExportOptions { BaseName = "hero" });

        Assert.Equal(2, artifacts.Count);
        Assert.Equal("hero.png", artifacts[0].FileName);
        Assert.Equal("hero.json", artifacts[1].FileName);
        Assert.True(PngChunkIOProbe.LooksLikePng(artifacts[0].Content));
    }

    [Fact]
    public void AStripPlacesEveryFrameInOneRow()
    {
        PixelDocument document = CreateDocument(4);

        ExportArtifact image = Single(
            new SpriteSheetExporter().Export(
                document,
                new AnimationExportOptions { Layout = SpriteSheetLayout.HorizontalStrip }),
            ".png");

        using MemoryStream stream = new(image.Content);
        PixelBuffer sheet = PngCodec.DecodeImage(stream);

        Assert.Equal(new ImageSize(16, 4), sheet.Size);

        // Each cell holds the frame it should: the exporter's placement is the thing that
        // silently goes wrong, and it goes wrong by one cell.
        Assert.Equal(Red, sheet.GetPixel(0, 0));
        Assert.Equal(Blue, sheet.GetPixel(4, 0));
        Assert.Equal(Green, sheet.GetPixel(8, 0));
        Assert.Equal(Red, sheet.GetPixel(12, 0));
    }

    [Fact]
    public void AGridIsAsSquareAsTheFrameCountAllows()
    {
        PixelDocument document = CreateDocument(9);

        ExportArtifact image = Single(
            new SpriteSheetExporter().Export(document, new AnimationExportOptions { Layout = SpriteSheetLayout.Grid }),
            ".png");

        using MemoryStream stream = new(image.Content);
        Assert.Equal(new ImageSize(12, 12), PngCodec.DecodeImage(stream).Size);
    }

    [Fact]
    public void OneRowPerAnimationPadsShortRowsOutToTheLongest()
    {
        PixelDocument document = CreateDocument(5);
        document.CreateTag("idle", 0, 1);
        document.CreateTag("walk", 2, 4);

        ExportArtifact image = Single(
            new SpriteSheetExporter().Export(
                document,
                new AnimationExportOptions { Layout = SpriteSheetLayout.RowPerAnimation }),
            ".png");

        using MemoryStream stream = new(image.Content);
        PixelBuffer sheet = PngCodec.DecodeImage(stream);

        // Three columns for the longest animation, two rows for the two animations.
        Assert.Equal(new ImageSize(12, 8), sheet.Size);

        // The idle row's unused third cell stays empty rather than borrowing a frame.
        Assert.True(sheet.GetPixel(8, 0).IsTransparent);
    }

    [Fact]
    public void PaddingSurroundsEveryCellIncludingTheOutermost()
    {
        PixelDocument document = CreateDocument(2);

        ExportArtifact image = Single(
            new SpriteSheetExporter().Export(
                document,
                new AnimationExportOptions { Layout = SpriteSheetLayout.HorizontalStrip, Padding = 1 }),
            ".png");

        using MemoryStream stream = new(image.Content);
        PixelBuffer sheet = PngCodec.DecodeImage(stream);

        Assert.Equal(new ImageSize(12, 6), sheet.Size);

        // A sheet padded only between its cells still bleeds at the edges.
        Assert.True(sheet.GetPixel(0, 0).IsTransparent);
        Assert.Equal(Red, sheet.GetPixel(1, 1));
    }

    [Fact]
    public void SheetMetadataDescribesTheCellsTheTimingsAndTheAnimations()
    {
        PixelDocument document = CreateDocument(4);
        document.Frames[1].DurationMilliseconds = 250;

        AnimationTag walk = document.CreateTag("walk", 0, 3);
        walk.Direction = LoopDirection.PingPong;

        ExportArtifact metadata = Single(
            new SpriteSheetExporter().Export(
                document,
                new AnimationExportOptions { BaseName = "hero", Layout = SpriteSheetLayout.HorizontalStrip }),
            ".json");

        using JsonDocument json = JsonDocument.Parse(metadata.Content);
        JsonElement root = json.RootElement;

        JsonElement frames = root.GetProperty("frames");
        Assert.Equal(4, frames.GetArrayLength());
        Assert.Equal(250, frames[1].GetProperty("duration").GetInt32());
        Assert.Equal(4, frames[1].GetProperty("frame").GetProperty("x").GetInt32());

        JsonElement meta = root.GetProperty("meta");
        Assert.Equal("hero.png", meta.GetProperty("image").GetString());

        // The cell size is the number an artist types into Unity's Grid By Cell Size, and is
        // the single most useful thing in the file.
        Assert.Equal(4, meta.GetProperty("cellSize").GetProperty("w").GetInt32());

        JsonElement tag = meta.GetProperty("frameTags")[0];
        Assert.Equal("walk", tag.GetProperty("name").GetString());
        Assert.Equal(0, tag.GetProperty("from").GetInt32());
        Assert.Equal(3, tag.GetProperty("to").GetInt32());

        // The direction is recorded rather than baked into the frames, so the sheet stores
        // each drawing once.
        Assert.Equal("pingpong", tag.GetProperty("direction").GetString());
    }

    [Fact]
    public void OneSheetPerAnimationIsWrittenWhenAsked()
    {
        PixelDocument document = CreateDocument(4);
        document.CreateTag("idle", 0, 1);
        document.CreateTag("walk", 2, 3);

        IReadOnlyList<ExportArtifact> artifacts = new SpriteSheetExporter().Export(
            document,
            new AnimationExportOptions { BaseName = "hero", SeparateFilePerTag = true });

        Assert.Equal(4, artifacts.Count);
        Assert.Contains(artifacts, a => a.FileName == "hero_idle.png");
        Assert.Contains(artifacts, a => a.FileName == "hero_walk.json");
    }

    // ---------------------------------------------------------------------------------
    // Frame sequence
    // ---------------------------------------------------------------------------------

    [Fact]
    public void AFrameSequenceIsNumberedSoItSortsCorrectly()
    {
        PixelDocument document = CreateDocument(12);

        IReadOnlyList<ExportArtifact> artifacts = new PngSequenceExporter()
            .Export(document, new AnimationExportOptions { BaseName = "run" });

        Assert.Equal(12, artifacts.Count);

        // Unpadded numbers sort as 1, 10, 11, 2 and scramble the animation in any tool that
        // ingests a folder in name order.
        Assert.Equal("run_000.png", artifacts[0].FileName);
        Assert.Equal("run_011.png", artifacts[11].FileName);
    }

    [Fact]
    public void AFrameSequenceNamesItsFilesAfterTheAnimation()
    {
        PixelDocument document = CreateDocument(3);
        document.CreateTag("jump", 0, 2);

        IReadOnlyList<ExportArtifact> artifacts = new PngSequenceExporter()
            .Export(document, new AnimationExportOptions { BaseName = "hero" });

        Assert.Equal("hero_jump_000.png", artifacts[0].FileName);
    }

    // ---------------------------------------------------------------------------------
    // Animated images
    // ---------------------------------------------------------------------------------

    [Fact]
    public void AGifIsWrittenWithAHeaderALoopExtensionAndATrailer()
    {
        PixelDocument document = CreateDocument(3);

        ExportArtifact gif = Assert.Single(
            new GifAnimationExporter().Export(document, new AnimationExportOptions { BaseName = "hero" }));

        Assert.Equal("hero.gif", gif.FileName);
        Assert.Equal("GIF89a", Encoding.ASCII.GetString(gif.Content, 0, 6));
        Assert.Equal(0x3B, gif.Content[^1]);

        // Looping is a vendor extension that became universal; without it a GIF plays once.
        Assert.Contains("NETSCAPE2.0", Encoding.ASCII.GetString(gif.Content), StringComparison.Ordinal);
    }

    [Fact]
    public void AGifRoundTripsItsFramesThroughTheEncoder()
    {
        PixelDocument document = CreateDocument(3, size: 8);

        ExportArtifact gif = Assert.Single(
            new GifAnimationExporter().Export(document, AnimationExportOptions.Default));

        // Decoding is what proves the LZW stream, the code widths and the sub-block framing
        // are all right; a broken one produces a file that opens as a garbled mess rather
        // than an error.
        IReadOnlyList<PixelBuffer> decoded = TestGifDecoder.Decode(gif.Content);

        Assert.Equal(3, decoded.Count);
        Assert.Equal(new ImageSize(8, 8), decoded[0].Size);
        Assert.Equal(Red, decoded[0].GetPixel(0, 0));
        Assert.Equal(Blue, decoded[1].GetPixel(4, 4));
        Assert.Equal(Green, decoded[2].GetPixel(7, 7));
    }

    [Fact]
    public void AGifExpandsAPingPongLoopIntoItsPlayedOrder()
    {
        PixelDocument document = CreateDocument(3);
        AnimationTag tag = document.CreateTag("swing", 0, 2);
        tag.Direction = LoopDirection.PingPong;

        ExportArtifact gif = Assert.Single(
            new GifAnimationExporter().Export(document, AnimationExportOptions.Default));

        // A GIF has no way to express a direction, so the frames must arrive in the order
        // they are meant to be seen: 0 1 2 1.
        Assert.Equal(4, TestGifDecoder.Decode(gif.Content).Count);
    }

    [Fact]
    public void AnAnimatedPngCarriesTheAnimationChunks()
    {
        PixelDocument document = CreateDocument(3);

        ExportArtifact apng = Assert.Single(
            new ApngAnimationExporter().Export(document, new AnimationExportOptions { BaseName = "hero" }));

        Assert.Equal("hero.apng", apng.FileName);
        Assert.True(PngChunkIOProbe.LooksLikePng(apng.Content));

        string content = Encoding.Latin1.GetString(apng.Content);
        Assert.Contains("acTL", content, StringComparison.Ordinal);
        Assert.Contains("fcTL", content, StringComparison.Ordinal);
        Assert.Contains("fdAT", content, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAnimatedPngStillOpensAsAStillImage()
    {
        PixelDocument document = CreateDocument(3, size: 8);

        ExportArtifact apng = Assert.Single(
            new ApngAnimationExporter().Export(document, AnimationExportOptions.Default));

        // The first frame lives in a plain IDAT, which is what lets a decoder that has never
        // heard of APNG show it rather than fail.
        using MemoryStream stream = new(apng.Content);
        PixelBuffer first = PngCodec.DecodeImage(stream);

        Assert.Equal(new ImageSize(8, 8), first.Size);
        Assert.Equal(Red, first.GetPixel(0, 0));
    }

    [Fact]
    public void ExportingScalesWithNearestNeighbourSampling()
    {
        PixelDocument document = CreateDocument(1);

        ExportArtifact image = Single(
            new SpriteSheetExporter().Export(document, new AnimationExportOptions { Scale = 4 }),
            ".png");

        using MemoryStream stream = new(image.Content);
        PixelBuffer sheet = PngCodec.DecodeImage(stream);

        Assert.Equal(new ImageSize(16, 16), sheet.Size);
        Assert.Equal(Red, sheet.GetPixel(15, 15));
    }

    [Fact]
    public void AnimationNamesAreMadeSafeForAFileSystem()
    {
        Assert.Equal("run_jump", AnimationExportOptions.SanitiseFileName("run/jump"));
        Assert.Equal("animation", AnimationExportOptions.SanitiseFileName("   "));
        Assert.Equal("attack 2", AnimationExportOptions.SanitiseFileName("attack 2"));
    }
}

/// <summary>Checks a byte array's PNG signature without exposing the codec's internals.</summary>
internal static class PngChunkIOProbe
{
    public static bool LooksLikePng(byte[] content) =>
        content.Length > 8
        && content[0] == 0x89
        && content[1] == 0x50
        && content[2] == 0x4E
        && content[3] == 0x47;
}
