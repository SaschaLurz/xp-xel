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
    public void TheMarqueeFollowsThePixelsAndComesBackWithThem()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(1, 1, 2, 2), Red);
        fixture.Select(new ImageRect(1, 1, 2, 2));

        fixture.Workspace.BeginTransform();
        fixture.Workspace.ActiveTransform!.MoveBy(3, 2);
        fixture.Workspace.CommitTransform();

        Assert.Equal(new ImageRect(4, 3, 2, 2), fixture.Workspace.Selection!.Bounds);

        fixture.Workspace.History.Undo();
        Assert.Equal(new ImageRect(1, 1, 2, 2), fixture.Workspace.Selection!.Bounds);
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
    public void PixelsPushedOffTheCanvasAreDropped()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 2, 2), Red);
        fixture.Select(new ImageRect(0, 0, 2, 2));

        fixture.Workspace.BeginTransform();
        fixture.Workspace.ActiveTransform!.MoveBy(7, 0);
        fixture.Workspace.CommitTransform();

        // The right-hand column of the selection lands outside an eight-wide document, so it
        // goes; the marquee has to agree with that rather than claim pixels that do not exist.
        Assert.Equal(Red, fixture.Pixels.GetPixel(7, 0));
        Assert.Equal(new ImageRect(7, 0, 1, 2), fixture.Workspace.Selection!.Bounds);
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

            RasterLayer layer = (RasterLayer)document.Root.Children[0];
            Cel cel = document.GetOrCreateCel(layer, document.Frames[0]);
            Pixels = cel.Pixels;
        }

        public DocumentWorkspace Workspace { get; }

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

        public void Select(ImageRect region) =>
            Workspace.Selection = SelectionMask.FromRect(new ImageSize(8, 8), region);
    }
}
