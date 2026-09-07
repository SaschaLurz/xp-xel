using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.History;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Selection;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for marking more than one layer at a time: what copying, cutting and grouping do once
/// the panel says "these layers" rather than "this layer".
/// </summary>
public sealed class MarkedLayerTests
{
    private static readonly PixelColor Red = new(255, 0, 0);
    private static readonly PixelColor Blue = new(0, 0, 255);
    private static readonly PixelColor Half = new(0, 0, 255, 128);

    [Fact]
    public void MarkingNothingFallsBackToTheActiveLayer()
    {
        Fixture fixture = new();

        Assert.Equal([fixture.Bottom], fixture.Workspace.GetSelectedLeaves());
    }

    [Fact]
    public void MarkingAFolderMarksWhatIsInsideIt()
    {
        Fixture fixture = new();

        GroupLayer folder = new(Guid.NewGuid(), "Folder");
        fixture.Document.InsertLayer(folder, fixture.Document.Root, 0);
        fixture.Document.RemoveLayer(fixture.Top);
        folder.Add(fixture.Top);

        fixture.Workspace.SetSelectedLayers([folder]);

        Assert.Equal([fixture.Top], fixture.Workspace.GetSelectedLeaves());
    }

    [Fact]
    public void CopyingOneLayerTakesItsOwnPixelsUntouched()
    {
        Fixture fixture = new();
        fixture.Paint(fixture.Bottom, 0, 0, Red);
        fixture.Paint(fixture.Top, 0, 0, Half);

        fixture.Workspace.SetSelectedLayers([fixture.Top]);
        fixture.Select(new ImageRect(0, 0, 1, 1));

        PixelClip clip = PixelClip.FromSelection(fixture.Workspace)!;

        // The half-transparent blue exactly as it is on the layer: not composited over the red
        // beneath it, and not run through the blend arithmetic on the way out.
        Assert.Equal(Half, clip.Pixels[0]);
    }

    [Fact]
    public void CopyingSeveralLayersFlattensThemInStackOrder()
    {
        Fixture fixture = new();
        fixture.Paint(fixture.Bottom, 0, 0, Red);
        fixture.Paint(fixture.Top, 0, 0, Blue);

        fixture.Workspace.SetSelectedLayers([fixture.Bottom, fixture.Top]);
        fixture.Select(new ImageRect(0, 0, 1, 1));

        PixelClip clip = PixelClip.FromSelection(fixture.Workspace)!;

        // The upper layer is opaque, so it wins — which is what the artist can see.
        Assert.Equal(Blue, clip.Pixels[0]);
    }

    [Fact]
    public void AHiddenLayerContributesNothingToAMergedCopy()
    {
        Fixture fixture = new();
        fixture.Paint(fixture.Bottom, 0, 0, Red);
        fixture.Paint(fixture.Top, 0, 0, Blue);
        fixture.Top.IsVisible = false;

        fixture.Workspace.SetSelectedLayers([fixture.Bottom, fixture.Top]);
        fixture.Select(new ImageRect(0, 0, 1, 1));

        PixelClip clip = PixelClip.FromSelection(fixture.Workspace)!;

        Assert.Equal(Red, clip.Pixels[0]);
    }

    [Fact]
    public void CuttingClearsEveryMarkedLayerAsOneUndoStep()
    {
        Fixture fixture = new();
        fixture.Paint(fixture.Bottom, 1, 1, Red);
        fixture.Paint(fixture.Top, 1, 1, Blue);

        fixture.Workspace.SetSelectedLayers([fixture.Bottom, fixture.Top]);
        fixture.Select(new ImageRect(1, 1, 1, 1));

        Assert.True(fixture.Workspace.EraseSelection("Cut"));
        Assert.Equal(1, fixture.Workspace.History.Count);

        Assert.True(fixture.PixelsOf(fixture.Bottom).GetPixel(1, 1).IsTransparent);
        Assert.True(fixture.PixelsOf(fixture.Top).GetPixel(1, 1).IsTransparent);

        fixture.Workspace.History.Undo();

        Assert.Equal(Red, fixture.PixelsOf(fixture.Bottom).GetPixel(1, 1));
        Assert.Equal(Blue, fixture.PixelsOf(fixture.Top).GetPixel(1, 1));
    }

    [Fact]
    public void ALockedLayerIsSkippedRatherThanStoppingTheErase()
    {
        Fixture fixture = new();
        fixture.Paint(fixture.Bottom, 1, 1, Red);
        fixture.Paint(fixture.Top, 1, 1, Blue);
        fixture.Bottom.IsLocked = true;

        fixture.Workspace.SetSelectedLayers([fixture.Bottom, fixture.Top]);
        fixture.Select(new ImageRect(1, 1, 1, 1));

        Assert.True(fixture.Workspace.EraseSelection());

        Assert.Equal(Red, fixture.PixelsOf(fixture.Bottom).GetPixel(1, 1));
        Assert.True(fixture.PixelsOf(fixture.Top).GetPixel(1, 1).IsTransparent);
    }

    [Fact]
    public void GroupingSeveralLayersPutsThemAllInOneFolderInOrder()
    {
        Fixture fixture = new();
        GroupLayer folder = new(Guid.NewGuid(), "Folder");

        fixture.Workspace.History.Execute(new GroupLayersCommand(
            "Group Layers", fixture.Document, folder, [fixture.Bottom, fixture.Top]));

        Assert.Equal(2, folder.Children.Count);
        Assert.Same(fixture.Bottom, folder.Children[0]);
        Assert.Same(fixture.Top, folder.Children[1]);
        Assert.Single(fixture.Document.Root.Children);
    }

    [Fact]
    public void UngroupingByUndoPutsEveryLayerBackWhereItWas()
    {
        Fixture fixture = new();
        RasterLayer middle = fixture.AddLayer("Middle");

        int bottomIndex = fixture.Document.Root.IndexOf(fixture.Bottom);
        int topIndex = fixture.Document.Root.IndexOf(fixture.Top);

        GroupLayer folder = new(Guid.NewGuid(), "Folder");

        fixture.Workspace.History.Execute(new GroupLayersCommand(
            "Group Layers", fixture.Document, folder, [fixture.Bottom, fixture.Top]));

        fixture.Workspace.History.Undo();

        Assert.Equal(bottomIndex, fixture.Document.Root.IndexOf(fixture.Bottom));
        Assert.Equal(topIndex, fixture.Document.Root.IndexOf(fixture.Top));
        Assert.Contains(middle, fixture.Document.Root.Children);
        Assert.DoesNotContain(folder, fixture.Document.Root.Children);
    }

    [Fact]
    public void ALayerInsideAFolderBeingGroupedIsNotTornOutOfIt()
    {
        Fixture fixture = new();

        GroupLayer existing = new(Guid.NewGuid(), "Existing");
        fixture.Document.InsertLayer(existing, fixture.Document.Root, 0);
        fixture.Document.RemoveLayer(fixture.Top);
        existing.Add(fixture.Top);

        GroupLayer folder = new(Guid.NewGuid(), "Folder");

        // Both the folder and its child are handed in, as they would be by a panel where the
        // artist marked a folder and then Ctrl-clicked a row inside it.
        fixture.Workspace.History.Execute(new GroupLayersCommand(
            "Group Layers", fixture.Document, folder, [existing, fixture.Top]));

        Assert.Single(folder.Children);
        Assert.Same(existing, folder.Children[0]);
        Assert.Same(existing, fixture.Top.Parent);
    }

    /// <summary>An 8×8 document with two raster layers, bottom-most first.</summary>
    private sealed class Fixture
    {
        public Fixture()
        {
            Document = PixelDocument.CreateNew(new ImageSize(8, 8), layerName: "Bottom");
            Workspace = new DocumentWorkspace(Document);

            Bottom = (RasterLayer)Document.Root.Children[0];
            Top = AddLayer("Top");

            Document.GetOrCreateCel(Bottom, Document.Frames[0]);
        }

        public PixelDocument Document { get; }

        public DocumentWorkspace Workspace { get; }

        public RasterLayer Bottom { get; }

        public RasterLayer Top { get; }

        public RasterLayer AddLayer(string name)
        {
            RasterLayer layer = Document.CreateRasterLayer(name);
            Document.GetOrCreateCel(layer, Document.Frames[0]);
            return layer;
        }

        public PixelBuffer PixelsOf(RasterLayer layer) =>
            Document.GetOrCreateCel(layer, Document.Frames[0]).Pixels;

        public void Paint(RasterLayer layer, int x, int y, PixelColor color) =>
            PixelsOf(layer).SetPixel(x, y, color);

        public void Select(ImageRect region) =>
            Workspace.Selection = SelectionMask.FromRect(new ImageSize(8, 8), region);
    }
}
