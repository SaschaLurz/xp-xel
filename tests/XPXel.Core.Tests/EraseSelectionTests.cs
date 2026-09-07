using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Rendering;
using XPXel.Core.Selection;
using XPXel.Core.Tools;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for clearing the marked pixels, which is what Delete and Backspace do, and for the
/// move tool preferring a selection over the layer it sits on.
/// </summary>
public sealed class EraseSelectionTests
{
    private static readonly PixelColor Red = new(255, 0, 0);
    private static readonly PixelColor Blue = new(0, 0, 255);

    [Fact]
    public void ErasingClearsOnlyTheMarkedPixels()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 8, 8), Red);
        fixture.Select(new ImageRect(2, 2, 3, 3));

        Assert.True(fixture.Workspace.EraseSelection());

        Assert.Equal(PixelColor.Transparent, fixture.Pixels.GetPixel(2, 2));
        Assert.Equal(PixelColor.Transparent, fixture.Pixels.GetPixel(4, 4));
        Assert.Equal(Red, fixture.Pixels.GetPixel(1, 2));
        Assert.Equal(Red, fixture.Pixels.GetPixel(5, 4));
    }

    [Fact]
    public void ErasingIsOneUndoableStep()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 8, 8), Red);
        fixture.Select(new ImageRect(2, 2, 3, 3));

        fixture.Workspace.EraseSelection();

        Assert.Equal(1, fixture.Workspace.History.Count);

        fixture.Workspace.History.Undo();

        Assert.Equal(Red, fixture.Pixels.GetPixel(3, 3));
    }

    [Fact]
    public void ErasingLeavesTheMarksBehind()
    {
        // The usual next act is to draw something else in the space just cleared, and clearing
        // the marks would throw away the shape the artist would have to make again.
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 8, 8), Red);
        fixture.Select(new ImageRect(2, 2, 3, 3));

        fixture.Workspace.EraseSelection();

        Assert.NotNull(fixture.Workspace.Selection);
        Assert.True(fixture.Workspace.Selection!.IsSelected(3, 3));
    }

    [Fact]
    public void ErasingNothingChangesNothing()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 8, 8), Red);

        Assert.False(fixture.Workspace.EraseSelection());
        Assert.Equal(0, fixture.Workspace.History.Count);
    }

    [Fact]
    public void ErasingAnAlreadyEmptyRegionRecordsNoHistory()
    {
        Fixture fixture = new();
        fixture.Select(new ImageRect(2, 2, 3, 3));

        Assert.False(fixture.Workspace.EraseSelection());
        Assert.Equal(0, fixture.Workspace.History.Count);
    }

    [Fact]
    public void TheMoveToolLiftsTheSelectionRatherThanTheLayer()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(1, 1, 2, 2), Red);
        fixture.Fill(new ImageRect(5, 5, 2, 2), Blue);
        fixture.Select(new ImageRect(1, 1, 2, 2));

        MoveTool tool = new();
        tool.OnPointerPressed(fixture.Context, Press(new ImagePoint(1, 1)));

        // The press is answered by lifting the marked pixels; the canvas turns that into a
        // drag of its own, so the tool itself must not have taken hold of the layer.
        Assert.NotNull(fixture.Workspace.ActiveTransform);

        fixture.Workspace.ActiveTransform!.MoveBy(0, 4);
        Assert.True(fixture.Workspace.CommitTransform());

        Assert.Equal(Red, fixture.Pixels.GetPixel(1, 5));
        Assert.Equal(PixelColor.Transparent, fixture.Pixels.GetPixel(1, 1));

        // The rest of the layer stayed exactly where it was.
        Assert.Equal(Blue, fixture.Pixels.GetPixel(5, 5));
    }

    [Fact]
    public void TheMoveToolStillMovesTheWholeLayerWhenNothingIsMarked()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(1, 1, 2, 2), Red);

        MoveTool tool = new();
        tool.OnPointerPressed(fixture.Context, Press(new ImagePoint(4, 4)));
        tool.OnPointerMoved(fixture.Context, Press(new ImagePoint(6, 4)));
        tool.OnPointerReleased(fixture.Context, Press(new ImagePoint(6, 4)));

        // Nothing was lifted, and the drag became the layer move it has always been. Where the
        // artwork lands afterwards is LayerMoveTests' subject; what matters here is which of
        // the two things the tool chose to do.
        Assert.Null(fixture.Workspace.ActiveTransform);
        Assert.Equal(1, fixture.Workspace.History.Count);
        Assert.Equal(Red, fixture.Sample(3, 1));
        Assert.True(fixture.Sample(1, 1).IsTransparent);
    }

    private static ToolPointerEventArgs Press(ImagePoint position) =>
        new(position, ToolPointerButton.Left);

    private sealed class Fixture
    {
        public Fixture()
        {
            PixelDocument document = PixelDocument.CreateNew(new ImageSize(8, 8));
            Workspace = new DocumentWorkspace(document);
            Context = new ToolContext(Workspace, new ToolSettings(), new ColorSlots());

            RasterLayer layer = (RasterLayer)document.Root.Children[0];
            Cel cel = document.GetOrCreateCel(layer, document.Frames[0]);
            Pixels = cel.Pixels;
        }

        public DocumentWorkspace Workspace { get; }

        public ToolContext Context { get; }

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

        /// <summary>Reads a pixel of the flattened image.</summary>
        /// <remarks>
        /// A whole-layer move reallocates the cel's buffer rather than shifting an offset, so a
        /// buffer captured before the move is not the one the artwork ended up in. Compositing
        /// asks the question the artist would: what is actually on the canvas there?
        /// </remarks>
        public PixelColor Sample(int x, int y)
        {
            PixelBuffer flattened = new(Workspace.Document.Size);
            new LayerCompositor().Compose(Workspace.Document, Workspace.Document.Frames[0], flattened);
            return flattened.GetPixelOrTransparent(x, y);
        }
    }
}
