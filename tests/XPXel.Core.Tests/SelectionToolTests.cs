using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Selection;
using XPXel.Core.Tools;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for the tools that mark pixels, and for the gesture they share: pressing on what has
/// been marked picks it up.
/// </summary>
public sealed class SelectionToolTests
{
    private static readonly PixelColor Red = new(255, 0, 0);

    [Fact]
    public void TheMarqueeMarksTheRectangleThatWasDragged()
    {
        Fixture fixture = new();
        RectangleSelectTool tool = new();

        fixture.Press(tool, 2, 2);
        fixture.Move(tool, 5, 6);
        fixture.Release(tool, 5, 6);

        Assert.Equal(new ImageRect(2, 2, 4, 5), fixture.Workspace.Selection!.Bounds);
    }

    [Fact]
    public void PressingInsideAMarqueePicksThePixelsUpInsteadOfStartingANewOne()
    {
        Fixture fixture = new();
        RectangleSelectTool tool = new();

        fixture.Fill(new ImageRect(2, 2, 3, 3), Red);
        fixture.Workspace.Selection = SelectionMask.FromRect(fixture.Document.Size, new ImageRect(2, 2, 3, 3));

        fixture.Press(tool, 3, 3);

        // The whole point: having marked something, dragging it is the next thing an artist
        // wants, and it must not require a menu or a different tool.
        Assert.NotNull(fixture.Workspace.ActiveTransform);
        Assert.Equal(new ImageRect(2, 2, 3, 3), fixture.Workspace.Selection!.Bounds);
    }

    [Fact]
    public void PressingOutsideAMarqueeStartsANewOne()
    {
        Fixture fixture = new();
        RectangleSelectTool tool = new();

        fixture.Workspace.Selection = SelectionMask.FromRect(fixture.Document.Size, new ImageRect(2, 2, 3, 3));

        fixture.Press(tool, 10, 10);
        fixture.Move(tool, 12, 12);
        fixture.Release(tool, 12, 12);

        Assert.Null(fixture.Workspace.ActiveTransform);
        Assert.Equal(new ImageRect(10, 10, 3, 3), fixture.Workspace.Selection!.Bounds);
    }

    [Fact]
    public void ThePixelToolMarksWhatItIsDraggedOver()
    {
        Fixture fixture = new();
        PixelSelectTool tool = new();

        fixture.Press(tool, 3, 3);
        fixture.Move(tool, 6, 3);
        fixture.Release(tool, 6, 3);

        SelectionMask mask = fixture.Workspace.Selection!;

        for (int x = 3; x <= 6; x++)
        {
            Assert.True(mask.IsSelected(x, 3), $"({x}, 3) should be marked.");
        }

        Assert.False(mask.IsSelected(7, 3));
    }

    [Fact]
    public void ThePixelToolStartsAgainOnAPlainClick()
    {
        Fixture fixture = new();
        PixelSelectTool tool = new();

        fixture.Press(tool, 2, 2);
        fixture.Release(tool, 2, 2);

        fixture.Press(tool, 9, 9);
        fixture.Release(tool, 9, 9);

        // Replacing is what makes a stray click cost nothing: the way out of a wrong mark is
        // to click somewhere else, not to deselect and start over.
        Assert.False(fixture.Workspace.Selection!.IsSelected(2, 2));
        Assert.True(fixture.Workspace.Selection!.IsSelected(9, 9));
    }

    [Fact]
    public void ShiftAddsPixelsThatAreNowhereNearEachOther()
    {
        Fixture fixture = new();
        PixelSelectTool tool = new();

        fixture.Press(tool, 2, 2);
        fixture.Release(tool, 2, 2);

        fixture.Press(tool, 9, 9, shift: true);
        fixture.Release(tool, 9, 9, shift: true);

        fixture.Press(tool, 4, 12, shift: true);
        fixture.Release(tool, 4, 12, shift: true);

        // Three separate pixels, none of them touching: the case a marquee cannot express at
        // all and the reason this tool exists.
        Assert.True(fixture.Workspace.Selection!.IsSelected(2, 2));
        Assert.True(fixture.Workspace.Selection!.IsSelected(9, 9));
        Assert.True(fixture.Workspace.Selection!.IsSelected(4, 12));
        Assert.False(fixture.Workspace.Selection!.IsSelected(5, 5));
    }

    [Fact]
    public void ScatteredPixelsMoveOnTheirOwnAndLeaveTheirNeighboursAlone()
    {
        Fixture fixture = new();
        PixelSelectTool tool = new();

        fixture.Fill(new ImageRect(0, 0, 16, 16), Red);

        fixture.Press(tool, 2, 2);
        fixture.Release(tool, 2, 2);
        fixture.Press(tool, 9, 9, shift: true);
        fixture.Release(tool, 9, 9, shift: true);

        Assert.True(fixture.Workspace.BeginTransform());
        fixture.Workspace.ActiveTransform!.MoveBy(0, 2);
        fixture.Workspace.CommitTransform();

        // Only the two marked pixels moved. Everything between them, inside the same bounding
        // box, is untouched — which is the whole point of marking pixels rather than a box.
        Assert.True(fixture.Pixels.GetPixel(2, 2).IsTransparent);
        Assert.True(fixture.Pixels.GetPixel(9, 9).IsTransparent);
        Assert.Equal(Red, fixture.Pixels.GetPixel(2, 4));
        Assert.Equal(Red, fixture.Pixels.GetPixel(9, 11));
        Assert.Equal(Red, fixture.Pixels.GetPixel(5, 5));
    }

    [Fact]
    public void HoldingControlUnmarksAndTheBoundsShrinkBackWithIt()
    {
        Fixture fixture = new();
        PixelSelectTool tool = new();

        fixture.Press(tool, 4, 4);
        fixture.Move(tool, 8, 4);
        fixture.Release(tool, 8, 4);

        fixture.Press(tool, 8, 4, control: true);
        fixture.Move(tool, 6, 4, control: true);
        fixture.Release(tool, 6, 4, control: true);

        SelectionMask mask = fixture.Workspace.Selection!;

        Assert.False(mask.IsSelected(8, 4));
        Assert.True(mask.IsSelected(4, 4));

        // Marking can only grow the bounds, so a tool that unmarks has to put them right on
        // release; otherwise the ants stay drawn around pixels that are no longer marked.
        Assert.Equal(new ImageRect(4, 4, 2, 1), mask.Bounds);
    }

    [Fact]
    public void UnmarkingEverythingClearsTheSelectionRatherThanLeavingAnEmptyOne()
    {
        Fixture fixture = new();
        PixelSelectTool tool = new();

        fixture.Press(tool, 5, 5);
        fixture.Release(tool, 5, 5);

        fixture.Press(tool, 5, 5, control: true);
        fixture.Release(tool, 5, 5, control: true);

        // An empty mask clips every drawing tool to nothing, which looks exactly like the
        // editor having stopped working.
        Assert.Null(fixture.Workspace.Selection);
    }

    [Fact]
    public void PressingOnPixelsThePixelToolMarkedPicksThemUp()
    {
        Fixture fixture = new();
        PixelSelectTool tool = new();

        fixture.Fill(new ImageRect(4, 4, 2, 2), Red);

        fixture.Press(tool, 4, 4);
        fixture.Move(tool, 5, 5);
        fixture.Release(tool, 5, 5);

        fixture.Press(tool, 5, 5);

        // The same gesture as the marquee's, deliberately: which tool did the marking should
        // not change how the marked pixels are moved.
        Assert.NotNull(fixture.Workspace.ActiveTransform);
    }

    [Fact]
    public void TheCropFrameStartsAsTheWholePicture()
    {
        Fixture fixture = new();
        CropTool tool = new();

        tool.OnActivated(fixture.Context);

        Assert.Equal(fixture.Document.Bounds, fixture.Context.Overlay.CropFrame);
    }

    [Fact]
    public void DraggingTheCropFrameOutwardsMakesTheCanvasBigger()
    {
        Fixture fixture = new();
        CropTool tool = new();

        tool.OnActivated(fixture.Context);

        // Grab the right-hand edge of a 16-wide canvas and pull it out by eight.
        fixture.Press(tool, 16, 8);
        fixture.Move(tool, 24, 8);
        fixture.Release(tool, 24, 8);

        Assert.True(tool.OnKeyDown(fixture.Context, ToolKey.Enter));
        Assert.Equal(new ImageSize(24, 16), fixture.Document.Size);
    }

    [Fact]
    public void DraggingTheCropFrameInwardsTrimsThePicture()
    {
        Fixture fixture = new();
        CropTool tool = new();

        fixture.Fill(new ImageRect(0, 0, 16, 16), Red);
        tool.OnActivated(fixture.Context);

        fixture.Press(tool, 0, 0);
        fixture.Move(tool, 4, 4);
        fixture.Release(tool, 4, 4);

        tool.OnKeyDown(fixture.Context, ToolKey.Enter);

        Assert.Equal(new ImageSize(12, 12), fixture.Document.Size);

        // The pixel that was at (4, 4) is now at the origin.
        Assert.Equal(Red, fixture.Pixels.GetPixel(0, 0));
    }

    [Fact]
    public void ACropIsOneUndoStepThatRestoresTheOldCanvas()
    {
        Fixture fixture = new();
        CropTool tool = new();

        tool.OnActivated(fixture.Context);

        fixture.Press(tool, 16, 16);
        fixture.Move(tool, 8, 8);
        fixture.Release(tool, 8, 8);

        tool.OnKeyDown(fixture.Context, ToolKey.Enter);
        Assert.Equal(new ImageSize(8, 8), fixture.Document.Size);

        Assert.Equal(1, fixture.Workspace.History.Count);
        Assert.True(fixture.Workspace.History.Undo());
        Assert.Equal(new ImageSize(16, 16), fixture.Document.Size);
    }

    [Fact]
    public void EscapePutsTheCropFrameBackAroundThePicture()
    {
        Fixture fixture = new();
        CropTool tool = new();

        tool.OnActivated(fixture.Context);

        fixture.Press(tool, 16, 8);
        fixture.Move(tool, 30, 8);
        fixture.Release(tool, 30, 8);

        Assert.NotEqual(fixture.Document.Bounds, fixture.Context.Overlay.CropFrame);

        Assert.True(tool.OnKeyDown(fixture.Context, ToolKey.Escape));
        Assert.Equal(fixture.Document.Bounds, fixture.Context.Overlay.CropFrame);
        Assert.Equal(0, fixture.Workspace.History.Count);
    }

    [Fact]
    public void LeavingTheCropToolTakesItsFrameWithIt()
    {
        Fixture fixture = new();
        CropTool tool = new();

        tool.OnActivated(fixture.Context);
        tool.OnDeactivated(fixture.Context);

        // A frame left behind would be drawn over artwork the next tool is being used on.
        Assert.Null(fixture.Context.Overlay.CropFrame);
    }

    /// <summary>A 16x16 document with one raster layer.</summary>
    private sealed class Fixture
    {
        public Fixture()
        {
            Document = PixelDocument.CreateNew(new ImageSize(16, 16));
            Workspace = new DocumentWorkspace(Document);

            Context = new ToolContext(Workspace, new ToolSettings(), new ColorSlots { Primary = Red });
        }

        public PixelDocument Document { get; }

        public DocumentWorkspace Workspace { get; }

        public ToolContext Context { get; }

        /// <summary>
        /// The active layer's pixels, read fresh each time.
        /// </summary>
        /// <remarks>
        /// A resize replaces every cel, so a buffer captured in the constructor would go stale
        /// the moment a crop was applied — and an assertion against a stale buffer passes for
        /// the wrong reason.
        /// </remarks>
        public PixelBuffer Pixels =>
            Document.GetOrCreateCel((RasterLayer)Document.Root.Children[0], Document.Frames[0]).Pixels;

        public void Fill(ImageRect region, PixelColor color)
        {
            for (int y = region.Top; y < region.Bottom; y++)
            {
                for (int x = region.Left; x < region.Right; x++)
                {
                    if (Pixels.Size.Contains(x, y))
                    {
                        Pixels.SetPixel(x, y, color);
                    }
                }
            }
        }

        public void Press(ITool tool, int x, int y, bool shift = false, bool control = false) =>
            tool.OnPointerPressed(Context, Args(x, y, shift, control));

        public void Move(ITool tool, int x, int y, bool shift = false, bool control = false) =>
            tool.OnPointerMoved(Context, Args(x, y, shift, control));

        public void Release(ITool tool, int x, int y, bool shift = false, bool control = false) =>
            tool.OnPointerReleased(Context, Args(x, y, shift, control));

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
