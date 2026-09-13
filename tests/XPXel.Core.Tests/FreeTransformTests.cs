using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Rendering;
using XPXel.Core.Selection;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for the free transform: what Ctrl+T does to the artwork when it is scaled, flipped and
/// turned, and what it leaves in the history afterwards.
/// </summary>
public sealed class FreeTransformTests
{
    private static readonly PixelColor Red = new(255, 0, 0);
    private static readonly PixelColor Blue = new(0, 0, 255);
    private static readonly PixelColor Green = new(0, 255, 0);

    [Fact]
    public void ScalingUpRepeatsEachPixelIntoABlock()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 2, 2), Red);
        fixture.Pixels.SetPixel(1, 0, Blue);
        fixture.Select(new ImageRect(0, 0, 2, 2));

        FreeTransform transform = FreeTransform.Begin(fixture.Workspace, FreeTransformScope.Pixels)!;

        transform.BeginDrag();
        transform.DragEdges(0, 0, 4, 4);

        // Each source pixel becomes a two-by-two block, in the colour it started with: nearest
        // neighbour, no invented colours along the edges.
        Assert.Equal(Red, fixture.Sample(0, 0));
        Assert.Equal(Red, fixture.Sample(1, 1));
        Assert.Equal(Blue, fixture.Sample(2, 0));
        Assert.Equal(Blue, fixture.Sample(3, 1));
    }

    [Fact]
    public void ScalingDownDropsPixelsRatherThanBlendingThem()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 4, 4), Red);
        fixture.Select(new ImageRect(0, 0, 4, 4));

        FreeTransform transform = FreeTransform.Begin(fixture.Workspace, FreeTransformScope.Pixels)!;

        transform.BeginDrag();
        transform.DragEdges(0, 0, 2, 2);

        Assert.Equal(Red, fixture.Sample(0, 0));
        Assert.Equal(Red, fixture.Sample(1, 1));

        // The rest of the original square is gone, not faded: a half-covered pixel would mean
        // the resampling had averaged, which is exactly what pixel art must not do.
        Assert.True(fixture.Sample(3, 3).IsTransparent);
    }

    [Fact]
    public void DraggingAHandlePastTheOppositeEdgeMirrorsTheArtwork()
    {
        Fixture fixture = new();
        fixture.Pixels.SetPixel(0, 0, Red);
        fixture.Pixels.SetPixel(1, 0, Blue);
        fixture.Select(new ImageRect(0, 0, 2, 1));

        FreeTransform transform = FreeTransform.Begin(fixture.Workspace, FreeTransformScope.Pixels)!;

        transform.BeginDrag();

        // The left edge dragged two pixels past the right one: the box is the same size and
        // sits alongside, and the artwork inside it has been flipped.
        transform.DragEdges(4, 0, 2, 1);

        Assert.True(transform.IsFlippedHorizontally);
        Assert.Equal(Blue, fixture.Sample(2, 0));
        Assert.Equal(Red, fixture.Sample(3, 0));
    }

    [Fact]
    public void AQuarterTurnSwapsTheBoxAndTurnsThePixelsWithIt()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(2, 2, 2, 1), Red);
        fixture.Pixels.SetPixel(2, 2, Blue);
        fixture.Select(new ImageRect(2, 2, 2, 1));

        FreeTransform transform = FreeTransform.Begin(fixture.Workspace, FreeTransformScope.Pixels)!;
        transform.Rotate(clockwise: true);

        Assert.Equal(1, transform.QuarterTurns);
        Assert.Equal(1, transform.Bounds.Width);
        Assert.Equal(2, transform.Bounds.Height);

        // The left-hand pixel of a horizontal pair becomes the top one of a vertical pair.
        Assert.Equal(Blue, fixture.Sample(transform.Bounds.Left, transform.Bounds.Top));
        Assert.Equal(Red, fixture.Sample(transform.Bounds.Left, transform.Bounds.Top + 1));
    }

    [Fact]
    public void FourQuarterTurnsPutEverythingBackAsItWas()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(1, 1, 3, 2), Red);
        fixture.Pixels.SetPixel(1, 1, Blue);
        fixture.Select(new ImageRect(1, 1, 3, 2));

        FreeTransform transform = FreeTransform.Begin(fixture.Workspace, FreeTransformScope.Pixels)!;

        for (int i = 0; i < 4; i++)
        {
            transform.Rotate(clockwise: true);
        }

        Assert.Equal(0, transform.QuarterTurns);
        Assert.Equal(Blue, fixture.Sample(1, 1));
        Assert.Equal(Red, fixture.Sample(3, 2));
    }

    [Fact]
    public void ApplyingATransformIsOneUndoableStep()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 2, 2), Red);
        fixture.Select(new ImageRect(0, 0, 2, 2));

        FreeTransform transform = FreeTransform.Begin(fixture.Workspace, FreeTransformScope.Pixels)!;
        transform.BeginDrag();
        transform.DragEdges(0, 0, 4, 4);

        fixture.Workspace.History.Push(transform.Commit()!);

        Assert.Equal(1, fixture.Workspace.History.Count);
        Assert.Equal(Red, fixture.Sample(3, 3));

        fixture.Workspace.History.Undo();

        Assert.Equal(Red, fixture.Sample(0, 0));
        Assert.True(fixture.Sample(3, 3).IsTransparent);
    }

    [Fact]
    public void AbandoningATransformLeavesTheArtworkExactlyAsItWas()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 2, 2), Red);
        fixture.Select(new ImageRect(0, 0, 2, 2));

        FreeTransform transform = FreeTransform.Begin(fixture.Workspace, FreeTransformScope.Pixels)!;
        transform.BeginDrag();
        transform.DragEdges(0, 0, 5, 5);
        transform.Rotate(clockwise: true);
        transform.Cancel();

        Assert.Equal(Red, fixture.Sample(1, 1));
        Assert.True(fixture.Sample(4, 4).IsTransparent);
        Assert.Equal(0, fixture.Workspace.History.Count);
    }

    [Fact]
    public void ApplyingTheBoxClearsTheMarks()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 2, 2), Red);
        fixture.Select(new ImageRect(0, 0, 2, 2));

        FreeTransform transform = FreeTransform.Begin(fixture.Workspace, FreeTransformScope.Pixels)!;
        transform.BeginDrag();
        transform.DragEdges(0, 0, 4, 4);

        fixture.Workspace.History.Push(transform.Commit()!);

        // Placing the artwork answers whatever the marks were asking, so Enter finishes the
        // job rather than leaving an outline the artist has to dismiss separately.
        Assert.Null(fixture.Workspace.Selection);

        // And the marks come back with an undo, so nothing about them is lost either way.
        fixture.Workspace.History.Undo();
        Assert.Equal(new ImageRect(0, 0, 2, 2), fixture.Workspace.Selection!.Bounds);
    }

    [Fact]
    public void WithNothingMarkedTheBoxStartsAroundThePaintedPixelsOnly()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(3, 4, 2, 2), Red);

        FreeTransform transform = FreeTransform.Begin(fixture.Workspace, FreeTransformScope.Pixels)!;

        // The painted rectangle, not the eight-by-eight canvas the cel actually occupies.
        Assert.Equal(new ImageRect(3, 4, 2, 2), transform.SourceBounds);
    }

    [Fact]
    public void ALayerWithNothingOnItHasNothingToTransform()
    {
        Fixture fixture = new();

        Assert.Null(FreeTransform.Begin(fixture.Workspace, FreeTransformScope.Pixels));
    }

    [Fact]
    public void ANonRectangularMarkKeepsItsShapeThroughTheTransform()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 8, 8), Red);

        // Two blue pixels on a diagonal, so the box is two by two with two corners uncovered.
        fixture.Pixels.SetPixel(0, 0, Blue);
        fixture.Pixels.SetPixel(1, 1, Blue);

        SelectionMask mask = new(new ImageSize(8, 8));
        mask.SetCoverage(0, 0, byte.MaxValue);
        mask.SetCoverage(1, 1, byte.MaxValue);
        mask.RecalculateBounds();
        fixture.Workspace.Selection = mask;

        FreeTransform transform = FreeTransform.Begin(fixture.Workspace, FreeTransformScope.Pixels)!;
        transform.BeginDrag();
        transform.DragEdges(4, 4, 6, 6);

        fixture.Workspace.History.Push(transform.Commit()!);

        // The two marked pixels landed on the new diagonal, and the two unmarked corners of the
        // box were left alone rather than being carried along as a rectangle.
        Assert.Equal(Blue, fixture.Sample(4, 4));
        Assert.Equal(Blue, fixture.Sample(5, 5));
        Assert.Equal(Red, fixture.Sample(5, 4));
        Assert.Equal(Red, fixture.Sample(4, 5));
    }

    [Fact]
    public void ArtworkPushedOverTheCanvasEdgeIsKeptRatherThanCut()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 2, 2), Red);
        fixture.Select(new ImageRect(0, 0, 2, 2));

        FreeTransform transform = FreeTransform.Begin(fixture.Workspace, FreeTransformScope.Pixels)!;
        transform.BeginDrag();

        // Half the box now hangs over the right-hand edge of an eight-wide canvas.
        transform.DragEdges(7, 0, 9, 2);

        fixture.Workspace.History.Push(transform.Commit()!);

        // The column that is still on the canvas is visible...
        Assert.Equal(Red, fixture.Sample(7, 0));

        // ...and the one past the edge is kept in the layer rather than thrown away, exactly
        // as it would be if the layer had been dragged off the side.
        Assert.Equal(Red, fixture.CelPixel(fixture.Layer, 8, 0));
        Assert.Equal(Red, fixture.CelPixel(fixture.Layer, 8, 1));
    }

    [Fact]
    public void ArtworkPushedOverTheEdgeComesBackWithAnUndo()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 2, 2), Red);
        fixture.Select(new ImageRect(0, 0, 2, 2));

        FreeTransform transform = FreeTransform.Begin(fixture.Workspace, FreeTransformScope.Pixels)!;
        transform.BeginDrag();
        transform.DragEdges(7, 0, 9, 2);

        fixture.Workspace.History.Push(transform.Commit()!);
        fixture.Workspace.History.Undo();

        // Growing the layer's buffer to hold what went over the side must not survive the
        // undo of the transform that needed it.
        Assert.Equal(Red, fixture.Sample(0, 0));
        Assert.True(fixture.Sample(7, 0).IsTransparent);
    }

    [Fact]
    public void ATransformLandingOnArtworkIsGivenALayerOfItsOwn()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 2, 2), Blue);
        fixture.Fill(new ImageRect(4, 4, 2, 2), Red);
        fixture.Select(new ImageRect(0, 0, 2, 2));

        int layersBefore = fixture.Document.Root.Children.Count;

        FreeTransform transform = FreeTransform.Begin(fixture.Workspace, FreeTransformScope.Pixels)!;
        transform.BeginDrag();
        transform.DragEdges(4, 4, 6, 6);

        fixture.Workspace.History.Push(transform.Commit()!);

        // The blue square is on a new layer above, so the red one it was dropped onto is
        // still there underneath rather than having been written over.
        Assert.Equal(layersBefore + 1, fixture.Document.Root.Children.Count);
        Assert.Equal(Blue, fixture.Sample(4, 4));
        Assert.Equal(Red, fixture.CelPixel(fixture.Layer, 4, 4));

        // And the whole episode is still one step back.
        fixture.Workspace.History.Undo();
        Assert.Equal(layersBefore, fixture.Document.Root.Children.Count);
        Assert.Equal(Blue, fixture.Sample(0, 0));
        Assert.Equal(Red, fixture.Sample(4, 4));
    }

    [Fact]
    public void ATransformLandingOnNothingStaysWhereItIs()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 2, 2), Blue);
        fixture.Select(new ImageRect(0, 0, 2, 2));

        int layersBefore = fixture.Document.Root.Children.Count;

        FreeTransform transform = FreeTransform.Begin(fixture.Workspace, FreeTransformScope.Pixels)!;
        transform.BeginDrag();
        transform.DragEdges(4, 4, 6, 6);

        fixture.Workspace.History.Push(transform.Commit()!);

        // Nothing was going to be destroyed, so there is no new layer to account for.
        Assert.Equal(layersBefore, fixture.Document.Root.Children.Count);
        Assert.Equal(Blue, fixture.CelPixel(fixture.Layer, 4, 4));
    }

    [Fact]
    public void TheNewLayerRuleCanBeTurnedOff()
    {
        Fixture fixture = new();
        fixture.Workspace.LayerSettings.TransformToNewLayer = false;

        fixture.Fill(new ImageRect(0, 0, 2, 2), Blue);
        fixture.Fill(new ImageRect(4, 4, 2, 2), Red);
        fixture.Select(new ImageRect(0, 0, 2, 2));

        int layersBefore = fixture.Document.Root.Children.Count;

        FreeTransform transform = FreeTransform.Begin(fixture.Workspace, FreeTransformScope.Pixels)!;
        transform.BeginDrag();
        transform.DragEdges(4, 4, 6, 6);

        fixture.Workspace.History.Push(transform.Commit()!);

        // Someone who works on one layer on purpose gets exactly that, and the red square is
        // written over as it was before the option existed.
        Assert.Equal(layersBefore, fixture.Document.Root.Children.Count);
        Assert.Equal(Blue, fixture.CelPixel(fixture.Layer, 4, 4));
    }

    [Fact]
    public void TheMiddleOfTheBoxOutranksItsEdges()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 8, 8), Red);
        fixture.Select(new ImageRect(0, 0, 8, 8));

        FreeTransform transform = FreeTransform.Begin(fixture.Workspace, FreeTransformScope.Pixels)!;

        // Dead centre, with enough slack that both edges would otherwise be in reach: the
        // move handle wins, which is what stops a small sprite from being all edge.
        Assert.Equal(TransformGrip.Center, transform.HitTest(new ImagePoint(4, 4), tolerance: 4));

        // A corner is still a corner, and the body between the two is still the body.
        Assert.Equal(
            TransformGrip.Left | TransformGrip.Top,
            transform.HitTest(new ImagePoint(0, 0), tolerance: 1));
        Assert.Equal(TransformGrip.Inside, transform.HitTest(new ImagePoint(2, 6), tolerance: 1));

        // And nothing at all outside it.
        Assert.Equal(TransformGrip.None, transform.HitTest(new ImagePoint(20, 20), tolerance: 1));
    }

    [Fact]
    public void TransformingTheMarksReshapesThemAndTouchesNoPixels()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 8, 8), Red);
        fixture.Select(new ImageRect(0, 0, 2, 2));

        FreeTransform transform = FreeTransform.Begin(fixture.Workspace, FreeTransformScope.Marks)!;

        transform.BeginDrag();
        transform.DragEdges(0, 0, 4, 4);

        fixture.Workspace.History.Push(transform.Commit()!);

        Assert.True(fixture.Workspace.Selection!.IsSelected(3, 3));

        // Not one pixel moved: the whole square is still the flat red it started as.
        Assert.Equal(Red, fixture.Sample(0, 0));
        Assert.Equal(Red, fixture.Sample(7, 7));
    }

    [Fact]
    public void ReshapingTheMarksNeedsSomethingMarked()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 8, 8), Red);

        Assert.Null(FreeTransform.Begin(fixture.Workspace, FreeTransformScope.Marks));
    }

    [Fact]
    public void EveryMarkedLayerIsTransformedTogether()
    {
        Fixture fixture = new();
        RasterLayer second = fixture.AddLayer("Second");

        fixture.Pixels.SetPixel(0, 0, Red);
        fixture.PixelsOf(second).SetPixel(0, 0, Green);

        fixture.Workspace.SetSelectedLayers([fixture.Layer, second]);
        fixture.Select(new ImageRect(0, 0, 1, 1));

        FreeTransform transform = FreeTransform.Begin(fixture.Workspace, FreeTransformScope.Pixels)!;
        transform.BeginDrag();
        transform.DragEdges(4, 4, 5, 5);

        fixture.Workspace.History.Push(transform.Commit()!);

        // Both layers moved, each keeping its own colour: the composite shows the upper one.
        Assert.Equal(Green, fixture.Sample(4, 4));
        Assert.True(fixture.Pixels.GetPixel(0, 0).IsTransparent);
        Assert.Equal(Red, fixture.Pixels.GetPixel(4, 4));
        Assert.Equal(Green, fixture.PixelsOf(second).GetPixel(4, 4));
    }

    [Fact]
    public void ADescriptionSaysTheSizeAndWhatWasDoneToIt()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 2, 2), Red);
        fixture.Select(new ImageRect(0, 0, 2, 2));

        FreeTransform transform = FreeTransform.Begin(fixture.Workspace, FreeTransformScope.Pixels)!;
        transform.BeginDrag();
        transform.DragEdges(0, 0, 4, 4);
        transform.Flip(horizontally: true);

        Assert.Contains("4", transform.Describe(), StringComparison.Ordinal);
        Assert.Contains("flipped", transform.Describe(), StringComparison.Ordinal);
    }

    /// <summary>An 8×8 document with one raster layer, and the means to add more.</summary>
    private sealed class Fixture
    {
        public Fixture()
        {
            Document = PixelDocument.CreateNew(new ImageSize(8, 8));
            Workspace = new DocumentWorkspace(Document);

            Layer = (RasterLayer)Document.Root.Children[0];
            Pixels = Document.GetOrCreateCel(Layer, Document.Frames[0]).Pixels;
        }

        public PixelDocument Document { get; }

        public DocumentWorkspace Workspace { get; }

        public RasterLayer Layer { get; }

        public PixelBuffer Pixels { get; }

        public RasterLayer AddLayer(string name)
        {
            RasterLayer layer = Document.CreateRasterLayer(name);
            Document.GetOrCreateCel(layer, Document.Frames[0]);
            return layer;
        }

        public PixelBuffer PixelsOf(RasterLayer layer) =>
            Document.GetOrCreateCel(layer, Document.Frames[0]).Pixels;

        public void Fill(ImageRect region, PixelColor color)
        {
            for (int y = region.Top; y < region.Bottom; y++)
            {
                for (int x = region.Left; x < region.Right; x++)
                {
                    Pixels.SetPixel(x, y, color);
                }
            }
        }

        public void Select(ImageRect region) =>
            Workspace.Selection = SelectionMask.FromRect(new ImageSize(8, 8), region);

        /// <summary>
        /// Reads a pixel of one layer's own content, in document coordinates, including the
        /// part of it that has been pushed off the canvas.
        /// </summary>
        public PixelColor CelPixel(RasterLayer layer, int x, int y)
        {
            // Looked up rather than cached: a cel is replaced, not resized, when it has to
            // grow, so a reference taken earlier would be to a buffer no longer in use.
            Cel cel = Document.Cels.Find(layer.Id, Document.Frames[0].Id)!;
            return cel.Pixels.GetPixelOrTransparent(x - cel.Offset.X, y - cel.Offset.Y);
        }

        /// <summary>Reads a pixel of the flattened image.</summary>
        public PixelColor Sample(int x, int y)
        {
            PixelBuffer flattened = new(Document.Size);
            new LayerCompositor().Compose(Document, Document.Frames[0], flattened);
            return flattened.GetPixelOrTransparent(x, y);
        }
    }
}
