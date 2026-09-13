using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Tools;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for drawing across every marked layer, which is the rule the whole application now
/// follows: marking rows in the layers panel means the same thing whether the next thing the
/// artist does is press Delete or draw a line.
/// </summary>
public sealed class MarkedLayerDrawingTests
{
    private static readonly PixelColor Red = new(255, 0, 0);
    private static readonly PixelColor Blue = new(0, 0, 255);

    [Fact]
    public void AStrokeLandsOnEveryMarkedLayer()
    {
        Fixture fixture = new();
        fixture.MarkBoth();

        PencilTool pencil = new();
        fixture.Press(pencil, 2, 2);
        fixture.Release(pencil, 2, 2);

        Assert.Equal(Red, fixture.PixelOf(fixture.Lower, 2, 2));
        Assert.Equal(Red, fixture.PixelOf(fixture.Upper, 2, 2));
    }

    [Fact]
    public void AStrokeOnSeveralLayersIsStillOneUndoStep()
    {
        Fixture fixture = new();
        fixture.MarkBoth();

        PencilTool pencil = new();
        fixture.Press(pencil, 1, 1);
        fixture.Move(pencil, 4, 4);
        fixture.Release(pencil, 4, 4);

        // One stroke is one thing the artist did. Undoing it must not cost as many presses as
        // they happened to have layers marked.
        Assert.Equal(1, fixture.Workspace.History.Count);

        fixture.Workspace.History.Undo();
        Assert.True(fixture.PixelOf(fixture.Lower, 1, 1).IsTransparent);
        Assert.True(fixture.PixelOf(fixture.Upper, 4, 4).IsTransparent);
    }

    [Fact]
    public void MarkingOneLayerDrawsOnOnlyThatLayer()
    {
        Fixture fixture = new();

        PencilTool pencil = new();
        fixture.Press(pencil, 2, 2);
        fixture.Release(pencil, 2, 2);

        Assert.Equal(Red, fixture.PixelOf(fixture.Upper, 2, 2));
        Assert.True(fixture.PixelOf(fixture.Lower, 2, 2).IsTransparent);
    }

    [Fact]
    public void ALockedOrHiddenMarkedLayerIsSkippedRatherThanRefusingTheStroke()
    {
        Fixture fixture = new();
        fixture.MarkBoth();
        fixture.Lower.IsLocked = true;

        PencilTool pencil = new();
        fixture.Press(pencil, 2, 2);
        fixture.Release(pencil, 2, 2);

        // The artist plainly meant the layers that can take paint, so the stroke lands on
        // those and the locked one is left alone rather than the whole gesture being refused.
        Assert.Equal(Red, fixture.PixelOf(fixture.Upper, 2, 2));
        Assert.True(fixture.PixelOf(fixture.Lower, 2, 2).IsTransparent);
    }

    [Fact]
    public void TheEraserClearsEveryMarkedLayerAtOnce()
    {
        Fixture fixture = new();
        fixture.Fill(fixture.Lower, Blue);
        fixture.Fill(fixture.Upper, Blue);
        fixture.MarkBoth();

        EraserTool eraser = new();
        fixture.Press(eraser, 3, 3);
        fixture.Release(eraser, 3, 3);

        Assert.True(fixture.PixelOf(fixture.Lower, 3, 3).IsTransparent);
        Assert.True(fixture.PixelOf(fixture.Upper, 3, 3).IsTransparent);
        Assert.Equal(Blue, fixture.PixelOf(fixture.Upper, 0, 0));
    }

    [Fact]
    public void ABucketFillTakesItsShapeFromTheLayerBeingAimedAt()
    {
        Fixture fixture = new();

        // A hole in the layer being aimed at, and nothing at all on the other one. Flooding
        // each layer from its own colours would fill the empty one edge to edge, which is
        // never what anybody meant - so the shape comes from the layer the artist can see.
        fixture.Fill(fixture.Upper, Blue);
        fixture.PixelsOf(fixture.Upper).SetPixel(1, 1, PixelColor.Transparent);
        fixture.MarkBoth();

        BucketFillTool bucket = new();
        fixture.Press(bucket, 1, 1);

        Assert.Equal(Red, fixture.PixelOf(fixture.Upper, 1, 1));
        Assert.Equal(Red, fixture.PixelOf(fixture.Lower, 1, 1));

        // Only the one-pixel hole was filled, on both layers.
        Assert.Equal(Blue, fixture.PixelOf(fixture.Upper, 3, 3));
        Assert.True(fixture.PixelOf(fixture.Lower, 3, 3).IsTransparent);
    }

    [Fact]
    public void EachToolRemembersItsOwnSize()
    {
        ToolSettings settings = new() { ActiveToolId = PencilTool.ToolId };
        settings.BrushSize = 1;

        settings.ActiveToolId = EraserTool.ToolId;
        settings.BrushSize = 20;

        // Going back to the pen-xel gets the pen-xel's size back. One shared number meant that
        // rubbing out a patch left the pen twenty pixels wide.
        settings.ActiveToolId = PencilTool.ToolId;
        Assert.Equal(1, settings.BrushSize);
        Assert.Equal(20, settings.GetBrushSize(EraserTool.ToolId));

        // A tool nobody has adjusted starts at a single pixel, which is what pixel art is
        // actually drawn with.
        settings.ActiveToolId = BrushTool.ToolId;
        Assert.Equal(ToolSettings.DefaultBrushSize, settings.BrushSize);
    }

    private sealed class Fixture
    {
        private readonly ColorSlots _colors = new();

        public Fixture()
        {
            Document = PixelDocument.CreateNew(new ImageSize(8, 8), "Lower");
            Workspace = new DocumentWorkspace(Document);

            Lower = (RasterLayer)Document.Root.Children[0];
            Upper = Document.CreateRasterLayer("Upper");

            Document.GetOrCreateCel(Lower, Document.Frames[0]);
            Document.GetOrCreateCel(Upper, Document.Frames[0]);

            Workspace.ActiveLayer = Upper;
            Workspace.SetSelectedLayers([Upper]);

            _colors.Primary = Red;
            Settings = new ToolSettings { ActiveToolId = PencilTool.ToolId };
            Context = new ToolContext(Workspace, Settings, _colors);
        }

        public PixelDocument Document { get; }

        public DocumentWorkspace Workspace { get; }

        public ToolSettings Settings { get; }

        public ToolContext Context { get; }

        public RasterLayer Lower { get; }

        public RasterLayer Upper { get; }

        public void MarkBoth() => Workspace.SetSelectedLayers([Lower, Upper]);

        public PixelBuffer PixelsOf(RasterLayer layer) =>
            Document.Cels.Find(layer.Id, Document.Frames[0].Id)!.Pixels;

        public PixelColor PixelOf(RasterLayer layer, int x, int y) =>
            PixelsOf(layer).GetPixelOrTransparent(x, y);

        public void Fill(RasterLayer layer, PixelColor color) => PixelsOf(layer).Fill(color);

        public void Press(ITool tool, int x, int y) =>
            tool.OnPointerPressed(Context, Args(x, y));

        public void Move(ITool tool, int x, int y) =>
            tool.OnPointerMoved(Context, Args(x, y));

        public void Release(ITool tool, int x, int y) =>
            tool.OnPointerReleased(Context, Args(x, y));

        private static ToolPointerEventArgs Args(int x, int y) =>
            new(new ImagePoint(x, y), ToolPointerButton.Left);
    }
}
