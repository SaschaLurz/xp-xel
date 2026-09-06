using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Rendering;
using XPXel.Core.Selection;
using XPXel.Core.Tools;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for moving a whole layer, and for the promise that goes with it: what leaves the canvas
/// is kept rather than cut.
/// </summary>
public sealed class LayerMoveTests
{
    private static readonly PixelColor Red = new(255, 0, 0);

    [Fact]
    public void DraggingMovesTheLayerAndRecordsOneUndoStep()
    {
        Fixture fixture = new();
        MoveTool tool = new();

        fixture.Pixels.SetPixel(2, 2, Red);

        fixture.Press(tool, 8, 8);
        fixture.Move(tool, 11, 12);
        fixture.Release(tool, 11, 12);

        Assert.Equal(1, fixture.Workspace.History.Count);
        Assert.Equal(Red, fixture.Sample(5, 6));
        Assert.True(fixture.Sample(2, 2).IsTransparent);
    }

    [Fact]
    public void WhatIsPushedOffTheCanvasIsKeptAndComesBack()
    {
        Fixture fixture = new();
        MoveTool tool = new();

        fixture.Pixels.SetPixel(1, 1, Red);

        // Twelve to the right, on a sixteen-wide canvas: the pixel lands at 13, still on it.
        fixture.Drag(tool, 0, 0, 12, 0);

        // Twelve more, and it is at 25 — well past the right-hand edge, and invisible.
        fixture.Drag(tool, 0, 0, 12, 0);
        Assert.True(fixture.Sample(15, 1).IsTransparent);

        // Back where it started. Nothing was cut, so the pixel is exactly where it was.
        fixture.Drag(tool, 0, 0, -24, 0);
        Assert.Equal(Red, fixture.Sample(1, 1));
    }

    [Fact]
    public void UndoingAMovePutsTheLayerBackWhereItWas()
    {
        Fixture fixture = new();
        MoveTool tool = new();

        fixture.Pixels.SetPixel(4, 4, Red);
        fixture.Drag(tool, 0, 0, 5, 5);

        Assert.Equal(Red, fixture.Sample(9, 9));

        fixture.Workspace.History.Undo();
        Assert.Equal(Red, fixture.Sample(4, 4));

        fixture.Workspace.History.Redo();
        Assert.Equal(Red, fixture.Sample(9, 9));
    }

    [Fact]
    public void DrawingStillLandsWhereItIsAimedAfterALayerHasBeenMoved()
    {
        Fixture fixture = new();
        MoveTool tool = new();
        PencilTool pencil = new();

        fixture.Drag(tool, 0, 0, 6, 6);

        // The cel now sits at an offset. Every tool speaks document coordinates, so a pencil
        // dab at (3, 3) has to land at (3, 3) — this is the whole reason the edit session
        // converts, rather than each tool remembering to.
        fixture.Press(pencil, 3, 3);
        fixture.Release(pencil, 3, 3);

        Assert.Equal(Red, fixture.Sample(3, 3));
    }

    [Fact]
    public void AMoveIsUndoneBeforeTheStrokeThatCameBeforeIt()
    {
        Fixture fixture = new();
        MoveTool move = new();
        PencilTool pencil = new();

        fixture.Press(pencil, 5, 5);
        fixture.Release(pencil, 5, 5);

        fixture.Drag(move, 0, 0, 4, 0);
        Assert.Equal(Red, fixture.Sample(9, 5));

        // The move replaced the cel with a larger one. Undoing it has to put the original cel
        // object back, or the stroke's own undo — which holds a reference to that cel — would
        // write into a buffer no longer in the document.
        fixture.Workspace.History.Undo();
        fixture.Workspace.History.Undo();

        Assert.True(fixture.Sample(5, 5).IsTransparent);
        Assert.True(fixture.Sample(9, 5).IsTransparent);
    }

    [Fact]
    public void MovingALayerWithNothingOnTheFrameDoesNothing()
    {
        Fixture fixture = new();
        MoveTool tool = new();

        // No cel has been created, because nothing has been drawn. A stray drag must not
        // conjure one, or every click on an empty layer would add weight to the file.
        fixture.Drag(tool, 0, 0, 3, 3);

        Assert.Equal(0, fixture.Workspace.History.Count);
        Assert.Null(fixture.Document.Cels.Find(fixture.Layer.Id, fixture.Document.Frames[0].Id));
    }

    [Fact]
    public void TheWandMarksTheWholeAreaOfOneColour()
    {
        Fixture fixture = new();
        MagicWandTool tool = new();

        for (int y = 2; y < 6; y++)
        {
            for (int x = 2; x < 6; x++)
            {
                fixture.Pixels.SetPixel(x, y, Red);
            }
        }

        fixture.Press(tool, 3, 3);

        SelectionMask mask = fixture.Workspace.Selection!;
        Assert.Equal(new ImageRect(2, 2, 4, 4), mask.Bounds);
        Assert.True(mask.IsSelected(5, 5));
        Assert.False(mask.IsSelected(6, 6));
    }

    [Fact]
    public void ShiftMakesTheWandAddASecondAreaToTheFirst()
    {
        Fixture fixture = new();
        MagicWandTool tool = new();

        fixture.Pixels.SetPixel(1, 1, Red);
        fixture.Pixels.SetPixel(9, 9, Red);

        fixture.Press(tool, 1, 1);
        fixture.Press(tool, 9, 9, shift: true);

        Assert.True(fixture.Workspace.Selection!.IsSelected(1, 1));
        Assert.True(fixture.Workspace.Selection!.IsSelected(9, 9));
    }

    [Fact]
    public void ControlMakesTheWandTakeAnAreaBackOut()
    {
        Fixture fixture = new();
        MagicWandTool tool = new();

        fixture.Pixels.SetPixel(1, 1, Red);
        fixture.Pixels.SetPixel(9, 9, Red);

        fixture.Press(tool, 1, 1);
        fixture.Press(tool, 9, 9, shift: true);
        fixture.Press(tool, 1, 1, control: true);

        Assert.False(fixture.Workspace.Selection!.IsSelected(1, 1));
        Assert.True(fixture.Workspace.Selection!.IsSelected(9, 9));
    }

    [Fact]
    public void CtrlClickingALayerMarksExactlyWhatItPaints()
    {
        Fixture fixture = new();

        fixture.Pixels.SetPixel(3, 3, Red);
        fixture.Pixels.SetPixel(4, 3, Red);

        Assert.True(fixture.Workspace.SelectLayerPixels(fixture.Layer));

        SelectionMask mask = fixture.Workspace.Selection!;
        Assert.True(mask.IsSelected(3, 3));
        Assert.True(mask.IsSelected(4, 3));

        // Transparent pixels are not part of the drawing, whatever the bounding box says.
        Assert.False(mask.IsSelected(3, 4));
        Assert.Equal(new ImageRect(3, 3, 2, 1), mask.Bounds);
    }

    [Fact]
    public void TheOutlineOfARectangleIsFourLines()
    {
        SelectionMask mask = SelectionMask.FromRect(new ImageSize(16, 16), new ImageRect(2, 2, 4, 4));

        IReadOnlyList<SelectionOutline.Segment> outline = SelectionOutline.Trace(mask);

        // Runs are merged, so a box costs four segments rather than four per pixel — which is
        // what keeps the marching ants cheap on a large selection.
        Assert.Equal(4, outline.Count);
    }

    [Fact]
    public void TwoSeparatePixelsAreOutlinedSeparately()
    {
        SelectionMask mask = new(new ImageSize(16, 16));
        mask.SetCoverage(2, 2, byte.MaxValue);
        mask.SetCoverage(9, 9, byte.MaxValue);

        IReadOnlyList<SelectionOutline.Segment> outline = SelectionOutline.Trace(mask);

        // Eight edges, two squares. The bounding box would have said one square of 64 pixels,
        // which is the lie this replaced.
        Assert.Equal(8, outline.Count);
    }

    /// <summary>A 16×16 document with one raster layer, drawn on through a real tool context.</summary>
    private sealed class Fixture
    {
        private readonly LayerCompositor _compositor = new();

        public Fixture()
        {
            Document = PixelDocument.CreateNew(new ImageSize(16, 16));
            Workspace = new DocumentWorkspace(Document);
            Layer = Document.Root.Children[0];

            Context = new ToolContext(Workspace, new ToolSettings(), new ColorSlots { Primary = Red });
        }

        public PixelDocument Document { get; }

        public DocumentWorkspace Workspace { get; }

        public LayerNode Layer { get; }

        public ToolContext Context { get; }

        /// <summary>The active layer's buffer, for setting up a fixture directly.</summary>
        public PixelBuffer Pixels => Document.GetOrCreateCel(Layer, Document.Frames[0]).Pixels;

        /// <summary>
        /// Reads a pixel of the flattened picture, which is the only honest way to ask what is
        /// on the canvas once a layer can sit at an offset.
        /// </summary>
        public PixelColor Sample(int x, int y)
        {
            PixelBuffer flattened = new(Document.Size);
            _compositor.Compose(Document, Document.Frames[0], flattened);
            return flattened.GetPixelOrTransparent(x, y);
        }

        public void Drag(ITool tool, int fromX, int fromY, int deltaX, int deltaY)
        {
            Press(tool, fromX, fromY);
            Move(tool, fromX + deltaX, fromY + deltaY);
            Release(tool, fromX + deltaX, fromY + deltaY);
        }

        public void Press(ITool tool, int x, int y, bool shift = false, bool control = false) =>
            tool.OnPointerPressed(Context, Args(x, y, shift, control));

        public void Move(ITool tool, int x, int y) => tool.OnPointerMoved(Context, Args(x, y, false, false));

        public void Release(ITool tool, int x, int y) => tool.OnPointerReleased(Context, Args(x, y, false, false));

        private static ToolPointerEventArgs Args(int x, int y, bool shift, bool control)
        {
            ToolModifiers modifiers = ToolModifiers.None;

            if (shift)
            {
                modifiers |= ToolModifiers.Shift;
            }

            if (control)
            {
                modifiers |= ToolModifiers.Control;
            }

            return new ToolPointerEventArgs(new ImagePoint(x, y), ToolPointerButton.Left, modifiers);
        }
    }
}
