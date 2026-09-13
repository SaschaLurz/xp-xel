using XPXel.Core.Documents;
using XPXel.Core.Drawing;
using XPXel.Core.Editing;
using XPXel.Core.Primitives;
using XPXel.Core.Selection;
using XPXel.Core.Tools;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for the drawing tools, exercised exactly the way the canvas control drives them:
/// pressed, moved, released, with positions already in image coordinates.
/// </summary>
/// <remarks>
/// These tests are possible only because tools take a <see cref="ToolContext"/> instead of
/// touching any UI type. Keep it that way — a tool that reaches for Avalonia cannot be
/// tested here.
/// </remarks>
public sealed class ToolTests
{
    private static readonly PixelColor Red = new(255, 0, 0);
    private static readonly PixelColor Blue = new(0, 0, 255);

    [Fact]
    public void PencilDrawsASinglePixelOnClick()
    {
        Fixture fixture = new();
        PencilTool pencil = new();

        Press(pencil, fixture, 2, 2);
        Release(pencil, fixture, 2, 2);

        Assert.Equal(Red, fixture.ActiveCel.Pixels.GetPixel(2, 2));
        Assert.Equal(1, fixture.Workspace.History.Count);
    }

    [Fact]
    public void PencilInterpolatesBetweenPointerSamples()
    {
        Fixture fixture = new();
        PencilTool pencil = new();

        // A fast drag reports only the endpoints; the stroke must still be continuous.
        Press(pencil, fixture, 0, 0);
        Move(pencil, fixture, 4, 0);
        Release(pencil, fixture, 4, 0);

        for (int x = 0; x <= 4; x++)
        {
            Assert.Equal(Red, fixture.ActiveCel.Pixels.GetPixel(x, 0));
        }
    }

    [Fact]
    public void AnEntireStrokeIsOneUndoStep()
    {
        Fixture fixture = new();
        PencilTool pencil = new();

        Press(pencil, fixture, 0, 0);
        Move(pencil, fixture, 1, 1);
        Move(pencil, fixture, 2, 2);
        Release(pencil, fixture, 2, 2);

        Assert.Equal(1, fixture.Workspace.History.Count);

        fixture.Workspace.History.Undo();
        Assert.Equal(PixelColor.Transparent, fixture.ActiveCel.Pixels.GetPixel(0, 0));
        Assert.Equal(PixelColor.Transparent, fixture.ActiveCel.Pixels.GetPixel(2, 2));
    }

    [Fact]
    public void TheRightButtonDrawsNothingAtAll()
    {
        Fixture fixture = new();
        PencilTool pencil = new();

        Press(pencil, fixture, 1, 1, ToolPointerButton.Right);
        Release(pencil, fixture, 1, 1, ToolPointerButton.Right);

        // A right press opens the tool's settings panel and never reaches a tool. The tools
        // used to carry a branch that painted the secondary colour with it, which the canvas
        // could not deliver, so the code claimed a feature the application did not have.
        Assert.True(fixture.ActiveCel.Pixels.GetPixel(1, 1).IsTransparent);
    }

    [Fact]
    public void BrushSizeStampsABlockOfPixels()
    {
        Fixture fixture = new();
        fixture.Settings.BrushSize = 3;
        fixture.Settings.BrushShape = BrushShape.Square;
        PencilTool pencil = new();

        Press(pencil, fixture, 3, 3);
        Release(pencil, fixture, 3, 3);

        for (int y = 2; y <= 4; y++)
        {
            for (int x = 2; x <= 4; x++)
            {
                Assert.Equal(Red, fixture.ActiveCel.Pixels.GetPixel(x, y));
            }
        }

        Assert.Equal(PixelColor.Transparent, fixture.ActiveCel.Pixels.GetPixel(1, 3));
    }

    [Fact]
    public void EraserClearsPixelsToTransparent()
    {
        Fixture fixture = new();
        fixture.ActiveCel.Pixels.Fill(Red);
        EraserTool eraser = new();

        Press(eraser, fixture, 1, 1);
        Release(eraser, fixture, 1, 1);

        Assert.Equal(PixelColor.Transparent, fixture.ActiveCel.Pixels.GetPixel(1, 1));
        Assert.Equal(Red, fixture.ActiveCel.Pixels.GetPixel(0, 0));
    }

    [Fact]
    public void CancellingAStrokeRevertsItAndRecordsNothing()
    {
        Fixture fixture = new();
        PencilTool pencil = new();

        Press(pencil, fixture, 1, 1);
        Move(pencil, fixture, 3, 3);
        pencil.Cancel(fixture.Context);

        Assert.Equal(PixelColor.Transparent, fixture.ActiveCel.Pixels.GetPixel(1, 1));
        Assert.Equal(0, fixture.Workspace.History.Count);
    }

    [Fact]
    public void ToolsDoNothingOnALockedLayer()
    {
        Fixture fixture = new();
        fixture.Layer.IsLocked = true;
        PencilTool pencil = new();

        Press(pencil, fixture, 1, 1);
        Release(pencil, fixture, 1, 1);

        Assert.Equal(PixelColor.Transparent, fixture.ActiveCel.Pixels.GetPixel(1, 1));
        Assert.Equal(0, fixture.Workspace.History.Count);
    }

    [Fact]
    public void BucketFillFillsTheCanvasInOneUndoStep()
    {
        Fixture fixture = new();
        BucketFillTool bucket = new();

        Press(bucket, fixture, 4, 4);

        Assert.Equal(Red, fixture.ActiveCel.Pixels.GetPixel(0, 0));
        Assert.Equal(Red, fixture.ActiveCel.Pixels.GetPixel(7, 7));
        Assert.Equal(1, fixture.Workspace.History.Count);

        fixture.Workspace.History.Undo();
        Assert.Equal(PixelColor.Transparent, fixture.ActiveCel.Pixels.GetPixel(0, 0));
    }

    [Fact]
    public void BucketFillRespectsASelection()
    {
        Fixture fixture = new();
        fixture.Workspace.Selection = SelectionMask.FromRect(
            fixture.Workspace.Document.Size,
            new ImageRect(0, 0, 2, 2));

        BucketFillTool bucket = new();
        Press(bucket, fixture, 0, 0);

        Assert.Equal(Red, fixture.ActiveCel.Pixels.GetPixel(1, 1));
        Assert.Equal(PixelColor.Transparent, fixture.ActiveCel.Pixels.GetPixel(3, 3));
    }

    [Fact]
    public void EyedropperPrefersTheFlattenedImage()
    {
        Fixture fixture = new();
        fixture.Context.CompositeSampler = (_, _) => Blue;
        fixture.Colors.Primary = Red;

        EyedropperTool eyedropper = new();
        Press(eyedropper, fixture, 1, 1);

        Assert.Equal(Blue, fixture.Colors.Primary);
    }

    [Fact]
    public void EyedropperWithAltSamplesTheActiveLayer()
    {
        Fixture fixture = new();
        fixture.ActiveCel.Pixels.Fill(Red);
        fixture.Context.CompositeSampler = (_, _) => Blue;
        fixture.Colors.Primary = PixelColor.White;

        EyedropperTool eyedropper = new();
        eyedropper.OnPointerPressed(
            fixture.Context,
            new ToolPointerEventArgs(new ImagePoint(1, 1), ToolPointerButton.Left, ToolModifiers.Alt));

        Assert.Equal(Red, fixture.Colors.Primary);
    }

    [Fact]
    public void EyedropperDoesNotCreateAnUndoStep()
    {
        Fixture fixture = new();
        EyedropperTool eyedropper = new();

        Press(eyedropper, fixture, 1, 1);

        Assert.Equal(0, fixture.Workspace.History.Count);
    }

    [Fact]
    public void HoldingShiftLocksAStrokeToTheDirectionItSetsOff()
    {
        Fixture fixture = new();
        PencilTool pencil = new();

        Press(pencil, fixture, 0, 4);
        MoveWithShift(pencil, fixture, 3, 4);

        // Set off horizontally, so the vertical component of everything after it is refused.
        MoveWithShift(pencil, fixture, 6, 7);
        Release(pencil, fixture, 6, 7);

        for (int x = 0; x <= 6; x++)
        {
            Assert.Equal(Red, fixture.ActiveCel.Pixels.GetPixel(x, 4));
        }

        Assert.Equal(PixelColor.Transparent, fixture.ActiveCel.Pixels.GetPixel(6, 7));
    }

    [Fact]
    public void AShiftLockedStrokeChoosesItsAxisFromTheFirstMovement()
    {
        Fixture fixture = new();
        PencilTool pencil = new();

        Press(pencil, fixture, 4, 0);
        MoveWithShift(pencil, fixture, 5, 3);
        Release(pencil, fixture, 5, 3);

        // Three down against one across: the artist meant down.
        for (int y = 0; y <= 3; y++)
        {
            Assert.Equal(Red, fixture.ActiveCel.Pixels.GetPixel(4, y));
        }

        Assert.Equal(PixelColor.Transparent, fixture.ActiveCel.Pixels.GetPixel(5, 3));
    }

    [Fact]
    public void LettingGoOfShiftAndPressingItAgainPicksTheOtherAxis()
    {
        Fixture fixture = new();
        PencilTool pencil = new();

        Press(pencil, fixture, 0, 0);
        MoveWithShift(pencil, fixture, 4, 0);

        // Releasing shift is what re-arms the choice, so the corner is turned without ever
        // lifting the button.
        Move(pencil, fixture, 4, 0);
        MoveWithShift(pencil, fixture, 4, 5);
        Release(pencil, fixture, 4, 5);

        for (int x = 0; x <= 4; x++)
        {
            Assert.Equal(Red, fixture.ActiveCel.Pixels.GetPixel(x, 0));
        }

        for (int y = 0; y <= 5; y++)
        {
            Assert.Equal(Red, fixture.ActiveCel.Pixels.GetPixel(4, y));
        }
    }

    [Fact]
    public void AReleasedStrokeIsNotRolledBackByTheNextOne()
    {
        Fixture fixture = new();
        PencilTool pencil = new();
        EraserTool eraser = new();

        Press(pencil, fixture, 1, 1);
        Release(pencil, fixture, 1, 1);

        // The canvas hands a stroke over when the tool changes under a held button, which ends
        // the outgoing tool's session properly. What must never happen is the erased pixel
        // coming back when the pencil is next pressed, which is what an abandoned session did.
        Press(eraser, fixture, 1, 1);
        Release(eraser, fixture, 1, 1);

        Press(pencil, fixture, 5, 5);
        Release(pencil, fixture, 5, 5);

        Assert.Equal(PixelColor.Transparent, fixture.ActiveCel.Pixels.GetPixel(1, 1));
    }

    private static void Press(ITool tool, Fixture fixture, int x, int y, ToolPointerButton button = ToolPointerButton.Left) =>
        tool.OnPointerPressed(fixture.Context, new ToolPointerEventArgs(new ImagePoint(x, y), button));

    private static void Move(ITool tool, Fixture fixture, int x, int y, ToolPointerButton button = ToolPointerButton.Left) =>
        tool.OnPointerMoved(fixture.Context, new ToolPointerEventArgs(new ImagePoint(x, y), button));

    private static void Release(ITool tool, Fixture fixture, int x, int y, ToolPointerButton button = ToolPointerButton.Left) =>
        tool.OnPointerReleased(fixture.Context, new ToolPointerEventArgs(new ImagePoint(x, y), button));

    private static void MoveWithShift(ITool tool, Fixture fixture, int x, int y) =>
        tool.OnPointerMoved(
            fixture.Context,
            new ToolPointerEventArgs(new ImagePoint(x, y), ToolPointerButton.Left, ToolModifiers.Shift));

    /// <summary>An 8x8 document with one layer, wired up exactly as the application does.</summary>
    private sealed class Fixture
    {
        public Fixture()
        {
            PixelDocument document = PixelDocument.CreateNew(new ImageSize(8, 8));
            Workspace = new DocumentWorkspace(document);
            Layer = (RasterLayer)document.Root.Children[0];
            ActiveCel = document.GetOrCreateCel(Layer, document.Frames[0]);

            Colors = new ColorSlots { Primary = Red, Secondary = Blue };
            Settings = new ToolSettings();
            Context = new ToolContext(Workspace, Settings, Colors);
        }

        public DocumentWorkspace Workspace { get; }

        public RasterLayer Layer { get; }

        public Cel ActiveCel { get; }

        public ColorSlots Colors { get; }

        public ToolSettings Settings { get; }

        public ToolContext Context { get; }
    }
}
