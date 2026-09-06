using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.History;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Rendering;
using XPXel.Core.Text;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for changing the canvas size, which covers both cropping and expanding.
/// </summary>
public sealed class CanvasResizeTests
{
    private static readonly PixelColor Red = new(255, 0, 0);
    private static readonly PixelColor Blue = new(0, 0, 255);

    private static (PixelDocument Document, LayerNode Layer) CreateDocument(int size = 8)
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(size, size));
        return (document, document.Root.Children[0]);
    }

    [Fact]
    public void GrowingTheCanvasKeepsTheArtworkAndPadsAroundIt()
    {
        (PixelDocument document, LayerNode layer) = CreateDocument(4);
        document.GetOrCreateCel(layer, document.Frames[0]).Pixels.SetPixel(0, 0, Red);

        document.Resize(new ImageSize(8, 8), new ImagePoint(2, 2));

        Assert.Equal(new ImageSize(8, 8), document.Size);

        Cel cel = document.Cels.Find(layer.Id, document.Frames[0].Id)!;
        Assert.Equal(new ImageSize(8, 8), cel.Pixels.Size);
        Assert.Equal(Red, cel.Pixels.GetPixel(2, 2));
        Assert.True(cel.Pixels.GetPixel(0, 0).IsTransparent);
    }

    [Fact]
    public void CroppingTheCanvasHidesThePixelsThatFallOutsideRatherThanDestroyingThem()
    {
        (PixelDocument document, LayerNode layer) = CreateDocument(8);
        PixelBufferSetter(document, layer, 1, 1, Red);
        PixelBufferSetter(document, layer, 6, 6, Blue);

        document.Resize(new ImageSize(4, 4), ImagePoint.Origin);

        Assert.Equal(new ImageSize(4, 4), document.Size);

        // The canvas stops at 3,3 but the blue pixel at 6,6 is still on the layer: the cel
        // keeps a buffer wide enough to hold it. Cropping hides artwork, the way a slide
        // editor hides a shape dragged off the slide, so trimming too far is recoverable.
        Cel cel = document.Cels.Find(layer.Id, document.Frames[0].Id)!;
        Assert.Equal(Red, cel.Pixels.GetPixel(1, 1));
        Assert.Equal(Blue, cel.Pixels.GetPixel(6, 6));
    }

    [Fact]
    public void GrowingTheCanvasBackShowsWhatTheCropHid()
    {
        (PixelDocument document, LayerNode layer) = CreateDocument(8);
        PixelBufferSetter(document, layer, 6, 6, Blue);

        document.Resize(new ImageSize(4, 4), ImagePoint.Origin);
        document.Resize(new ImageSize(8, 8), ImagePoint.Origin);

        // Not an undo: two separate crops, the second of them outwards. This is what dragging
        // the crop frame back out has to do.
        Cel cel = document.Cels.Find(layer.Id, document.Frames[0].Id)!;
        Assert.Equal(Blue, cel.Pixels.GetPixel(6 - cel.Offset.X, 6 - cel.Offset.Y));
        Assert.True(cel.Bounds.Contains(document.Bounds));
    }

    [Fact]
    public void CroppingAwayEmptySpaceStillShrinksTheBuffer()
    {
        (PixelDocument document, LayerNode layer) = CreateDocument(8);
        PixelBufferSetter(document, layer, 1, 1, Red);

        document.Resize(new ImageSize(4, 4), ImagePoint.Origin);

        // Only painted pixels are worth remembering. The right-hand half of this layer held
        // nothing, so cropping it away costs nothing — which is the common case, and the
        // reason keeping artwork can afford to be expensive.
        Cel cel = document.Cels.Find(layer.Id, document.Frames[0].Id)!;
        Assert.Equal(new ImageSize(4, 4), cel.Pixels.Size);
        Assert.Equal(ImagePoint.Origin, cel.Offset);
    }

    [Fact]
    public void ResizingIsUndoneWithEveryCroppedPixelIntact()
    {
        (PixelDocument document, LayerNode layer) = CreateDocument(8);
        DocumentWorkspace workspace = new(document);
        PixelBufferSetter(document, layer, 6, 6, Blue);

        ResizeCanvasCommand command = new("Canvas Size", document, new ImageSize(4, 4), ImagePoint.Origin);
        workspace.History.Execute(command);

        Assert.Equal(new ImageSize(4, 4), document.Size);

        workspace.History.Undo();

        Assert.Equal(new ImageSize(8, 8), document.Size);

        Cel cel = document.Cels.Find(layer.Id, document.Frames[0].Id)!;
        Assert.Equal(new ImageSize(8, 8), cel.Pixels.Size);
        Assert.Equal(Blue, cel.Pixels.GetPixel(6, 6));
    }

    [Fact]
    public void ResizingCanBeRedoneAfterBeingUndone()
    {
        (PixelDocument document, LayerNode layer) = CreateDocument(8);
        DocumentWorkspace workspace = new(document);
        PixelBufferSetter(document, layer, 1, 1, Red);

        workspace.History.Execute(new ResizeCanvasCommand("Canvas Size", document, new ImageSize(4, 4), ImagePoint.Origin));
        workspace.History.Undo();
        workspace.History.Redo();

        Assert.Equal(new ImageSize(4, 4), document.Size);
        Assert.Equal(Red, document.Cels.Find(layer.Id, document.Frames[0].Id)!.Pixels.GetPixel(1, 1));
    }

    [Fact]
    public void ResizingMovesEveryFramesArtwork()
    {
        (PixelDocument document, LayerNode layer) = CreateDocument(4);
        Frame second = document.CreateFrame();

        document.GetOrCreateCel(layer, document.Frames[0]).Pixels.SetPixel(0, 0, Red);
        document.GetOrCreateCel(layer, second).Pixels.SetPixel(0, 0, Blue);

        document.Resize(new ImageSize(8, 8), new ImagePoint(2, 2));

        Assert.Equal(Red, document.Cels.Find(layer.Id, document.Frames[0].Id)!.Pixels.GetPixel(2, 2));
        Assert.Equal(Blue, document.Cels.Find(layer.Id, second.Id)!.Pixels.GetPixel(2, 2));
    }

    [Fact]
    public void ATextLayersOriginMovesWithItsGlyphs()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(8, 8));
        TextLayer text = document.CreateTextLayer("Caption");
        text.Origin = new ImagePoint(1, 2);

        document.Resize(new ImageSize(16, 16), new ImagePoint(4, 4));

        // The glyphs live in a cel and shift with everything else. If the origin stayed put,
        // the text would jump back the next time anything re-rendered it — long after the
        // resize, and very hard to connect to its cause.
        Assert.Equal(new ImagePoint(5, 6), text.Origin);
    }

    [Fact]
    public void TheCompositorAgreesWithTheNewSizeImmediately()
    {
        (PixelDocument document, LayerNode layer) = CreateDocument(4);
        document.GetOrCreateCel(layer, document.Frames[0]).Pixels.Fill(Red);

        document.Resize(new ImageSize(6, 6), new ImagePoint(1, 1));

        PixelBuffer flattened = new LayerCompositor().ComposeToNewBuffer(document, document.Frames[0]);

        Assert.Equal(new ImageSize(6, 6), flattened.Size);
        Assert.Equal(Red, flattened.GetPixel(1, 1));
        Assert.True(flattened.GetPixel(0, 0).IsTransparent);
    }

    [Fact]
    public void ResizingReportsASizeChange()
    {
        (PixelDocument document, _) = CreateDocument(4);

        DocumentChangeKind? reported = null;
        document.Changed += (_, e) => reported = e.Kind;

        document.Resize(new ImageSize(8, 8), ImagePoint.Origin);

        // Everything cached at the old size — the render target's bitmap above all — has to
        // be told to rebuild rather than merely repaint.
        Assert.Equal(DocumentChangeKind.Size, reported);
    }

    [Theory]
    [InlineData(CanvasAnchor.TopLeft, 0, 0)]
    [InlineData(CanvasAnchor.Center, 4, 4)]
    [InlineData(CanvasAnchor.BottomRight, 8, 8)]
    [InlineData(CanvasAnchor.Top, 4, 0)]
    [InlineData(CanvasAnchor.Left, 0, 4)]
    [InlineData(CanvasAnchor.BottomLeft, 0, 8)]
    [InlineData(CanvasAnchor.TopRight, 8, 0)]
    public void TheAnchorDecidesWhereTheOldCanvasLands(CanvasAnchor anchor, int expectedX, int expectedY)
    {
        ImagePoint offset = PixelDocument.GetAnchorOffset(new ImageSize(8, 8), new ImageSize(16, 16), anchor);

        Assert.Equal(new ImagePoint(expectedX, expectedY), offset);
    }

    [Fact]
    public void AnchoringWorksTheSameWayWhenCropping()
    {
        // Shrinking is the same arithmetic with a negative delta, which is what lets one
        // operation cover both directions.
        ImagePoint offset = PixelDocument.GetAnchorOffset(new ImageSize(16, 16), new ImageSize(8, 8), CanvasAnchor.Center);

        Assert.Equal(new ImagePoint(-4, -4), offset);
    }

    [Fact]
    public void AZeroSizedCanvasIsRefused()
    {
        (PixelDocument document, _) = CreateDocument(4);

        Assert.Throws<ArgumentException>(() => document.Resize(new ImageSize(0, 8), ImagePoint.Origin));
    }

    private static void PixelBufferSetter(PixelDocument document, LayerNode layer, int x, int y, PixelColor color) =>
        document.GetOrCreateCel(layer, document.Frames[0]).Pixels.SetPixel(x, y, color);
}
