using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.History;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Selection;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for lifting the selected pixels off a layer and putting them down somewhere else,
/// which is what Ctrl+T does.
/// </summary>
public sealed class SelectionTransformTests
{
    private static readonly PixelColor Red = new(255, 0, 0);

    [Fact]
    public void MovingASelectionTakesThePixelsWithItAndLeavesAHole()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(1, 1, 2, 2), Red);
        fixture.Select(new ImageRect(1, 1, 2, 2));

        Assert.True(fixture.Workspace.BeginTransform());
        fixture.Workspace.ActiveTransform!.MoveBy(4, 0);
        Assert.True(fixture.Workspace.CommitTransform());

        Assert.Equal(PixelColor.Transparent, fixture.Pixels.GetPixel(1, 1));
        Assert.Equal(PixelColor.Transparent, fixture.Pixels.GetPixel(2, 2));
        Assert.Equal(Red, fixture.Pixels.GetPixel(5, 1));
        Assert.Equal(Red, fixture.Pixels.GetPixel(6, 2));
    }

    [Fact]
    public void DraggingAroundAndBackAgainLeavesTheDocumentUntouched()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(1, 1, 2, 2), Red);
        fixture.Select(new ImageRect(1, 1, 2, 2));

        fixture.Workspace.BeginTransform();

        // Every position is drawn from the artwork as it was lifted. Were it read back out of
        // the layer instead, the second move would pick up what the first one left behind.
        SelectionTransform transform = fixture.Workspace.ActiveTransform!;
        transform.MoveTo(new ImagePoint(3, 3));
        transform.MoveTo(new ImagePoint(5, 0));
        transform.MoveTo(ImagePoint.Origin);

        Assert.False(fixture.Workspace.CommitTransform());
        Assert.Equal(Red, fixture.Pixels.GetPixel(1, 1));
        Assert.Equal(Red, fixture.Pixels.GetPixel(2, 2));
        Assert.Equal(0, fixture.Workspace.History.Count);
    }

    [Fact]
    public void AMoveThatOverlapsWhereItCameFromKeepsTheArtwork()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(1, 1, 3, 3), Red);
        fixture.Select(new ImageRect(1, 1, 3, 3));

        fixture.Workspace.BeginTransform();
        fixture.Workspace.ActiveTransform!.MoveBy(1, 0);
        fixture.Workspace.CommitTransform();

        // The hole is punched before the artwork is stamped, so the column they share belongs
        // to the artwork. Clearing afterwards would erase a stripe out of the middle.
        Assert.Equal(PixelColor.Transparent, fixture.Pixels.GetPixel(1, 1));

        for (int x = 2; x <= 4; x++)
        {
            Assert.Equal(Red, fixture.Pixels.GetPixel(x, 2));
        }
    }

    [Fact]
    public void TheWholeDragIsASingleUndoStep()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(1, 1, 2, 2), Red);
        fixture.Select(new ImageRect(1, 1, 2, 2));

        fixture.Workspace.BeginTransform();

        SelectionTransform transform = fixture.Workspace.ActiveTransform!;
        transform.MoveBy(1, 0);
        transform.MoveBy(1, 0);
        transform.MoveBy(1, 1);

        fixture.Workspace.CommitTransform();

        Assert.Equal(1, fixture.Workspace.History.Count);
        Assert.True(fixture.Workspace.History.Undo());
        Assert.Equal(Red, fixture.Pixels.GetPixel(1, 1));
        Assert.Equal(PixelColor.Transparent, fixture.Pixels.GetPixel(4, 2));
    }

    [Fact]
    public void PlacingThePixelsClearsTheMarksAndUndoBringsThemBack()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(1, 1, 2, 2), Red);
        fixture.Select(new ImageRect(1, 1, 2, 2));

        fixture.Workspace.BeginTransform();
        fixture.Workspace.ActiveTransform!.MoveBy(3, 2);
        fixture.Workspace.CommitTransform();

        // Enter means done, here as everywhere else on the canvas: the pixels are placed and
        // the outline that was asking about them goes with them.
        Assert.Null(fixture.Workspace.Selection);

        fixture.Workspace.History.Undo();
        Assert.Equal(new ImageRect(1, 1, 2, 2), fixture.Workspace.Selection!.Bounds);
    }

    [Fact]
    public void AMoveThatWentNowhereLeavesTheMarksAlone()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(1, 1, 2, 2), Red);
        fixture.Select(new ImageRect(1, 1, 2, 2));

        fixture.Workspace.BeginTransform();
        fixture.Workspace.CommitTransform();

        // Pressing on the marching ants and letting go without dragging has answered nothing,
        // and this path is also reached defensively when the tool or the tab changes - so
        // throwing the marks away here would lose them to a gesture nobody made.
        Assert.NotNull(fixture.Workspace.Selection);
        Assert.Equal(0, fixture.Workspace.History.Count);
    }

    [Fact]
    public void CancellingAMovePutsEveryPixelBack()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(1, 1, 2, 2), Red);
        fixture.Select(new ImageRect(1, 1, 2, 2));

        fixture.Workspace.BeginTransform();
        fixture.Workspace.ActiveTransform!.MoveBy(4, 4);
        fixture.Workspace.CancelTransform();

        Assert.Null(fixture.Workspace.ActiveTransform);
        Assert.Equal(Red, fixture.Pixels.GetPixel(1, 1));
        Assert.Equal(PixelColor.Transparent, fixture.Pixels.GetPixel(5, 5));
        Assert.Equal(0, fixture.Workspace.History.Count);
    }

    [Fact]
    public void PixelsPushedOffTheCanvasAreKept()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 2, 2), Red);
        fixture.Select(new ImageRect(0, 0, 2, 2));

        fixture.Workspace.BeginTransform();
        fixture.Workspace.ActiveTransform!.MoveBy(7, 0);
        fixture.Workspace.CommitTransform();

        // The right-hand column lands outside an eight-wide document. It is kept in the layer,
        // hidden rather than cut, exactly as it would be if the whole layer had been dragged
        // off the side: only the eraser and the delete commands destroy pixels.
        Assert.Equal(Red, fixture.CelPixel(7, 0));
        Assert.Equal(Red, fixture.CelPixel(8, 0));
        Assert.Equal(Red, fixture.CelPixel(8, 1));

        // And undoing puts the layer back exactly as it was, grown buffer and all.
        fixture.Workspace.History.Undo();
        Assert.Equal(Red, fixture.CelPixel(0, 0));
        Assert.True(fixture.CelPixel(7, 0).IsTransparent);
    }

    [Fact]
    public void ThereIsNothingToMoveWithoutASelection()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(1, 1, 2, 2), Red);

        Assert.False(fixture.Workspace.BeginTransform());
        Assert.Null(fixture.Workspace.ActiveTransform);
    }

    [Fact]
    public void AMoveIgnoresTheSelectionItIsCarrying()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(1, 1, 2, 2), Red);
        fixture.Select(new ImageRect(1, 1, 2, 2));

        fixture.Workspace.BeginTransform();
        fixture.Workspace.ActiveTransform!.MoveBy(4, 4);
        fixture.Workspace.CommitTransform();

        // Every other edit is clipped to the selection so that no tool can escape it. This one
        // has to write where the artist is putting the pixels, which is by definition outside.
        Assert.Equal(Red, fixture.Pixels.GetPixel(5, 5));
    }

    /// <summary>An 8x8 document with one layer, ready to select on.</summary>
    private sealed class Fixture
    {
        public Fixture()
        {
            PixelDocument document = PixelDocument.CreateNew(new ImageSize(8, 8));
            Workspace = new DocumentWorkspace(document);

            Layer = (RasterLayer)document.Root.Children[0];
            Cel cel = document.GetOrCreateCel(Layer, document.Frames[0]);
            Pixels = cel.Pixels;
        }

        public DocumentWorkspace Workspace { get; }

        public RasterLayer Layer { get; }

        public PixelBuffer Pixels { get; }

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

        /// <summary>
        /// Reads a pixel of the layer's own content, in document coordinates, including the
        /// part of it that has been pushed off the canvas.
        /// </summary>
        /// <remarks>
        /// Looked up rather than cached, because a cel is replaced rather than resized when it
        /// has to grow to hold artwork that went over the edge.
        /// </remarks>
        public PixelColor CelPixel(int x, int y)
        {
            Cel cel = Workspace.Document.Cels.Find(Layer.Id, Workspace.Document.Frames[0].Id)!;
            return cel.Pixels.GetPixelOrTransparent(x - cel.Offset.X, y - cel.Offset.Y);
        }

        public void Select(ImageRect region) =>
            Workspace.Selection = SelectionMask.FromRect(new ImageSize(8, 8), region);
    }
}
