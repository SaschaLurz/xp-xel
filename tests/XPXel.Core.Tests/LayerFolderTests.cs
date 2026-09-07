using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.History;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Rendering;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for layer folders, which the compositor has supported since the first version and
/// which only needed a way to create one.
/// </summary>
public sealed class LayerFolderTests
{
    private static readonly PixelColor Red = new(255, 0, 0);

    [Fact]
    public void AFolderCanBeCreatedAndHoldsLayers()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(4, 4));

        GroupLayer folder = document.CreateGroupLayer("Character");
        RasterLayer inside = document.CreateRasterLayer("Arm", folder);

        Assert.Contains(folder, document.Root.Children);
        Assert.Same(folder, inside.Parent);
        Assert.Contains(inside, document.EnumerateLayers());
    }

    [Fact]
    public void GroupingALayerPutsTheFolderWhereTheLayerWas()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(4, 4));
        DocumentWorkspace workspace = new(document);

        RasterLayer middle = document.CreateRasterLayer("Middle");
        document.CreateRasterLayer("Top");

        int originalIndex = document.Root.IndexOf(middle);

        GroupLayer folder = new(Guid.NewGuid(), "Folder");
        workspace.History.Execute(new GroupLayersCommand("Group Layer", document, folder, [middle]));

        Assert.Equal(originalIndex, document.Root.IndexOf(folder));
        Assert.Same(folder, middle.Parent);
        Assert.Single(folder.Children);
        Assert.Equal(3, document.Root.Children.Count);
    }

    [Fact]
    public void GroupingIsUndoneByPuttingTheLayerBackAndRemovingTheFolder()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(4, 4));
        DocumentWorkspace workspace = new(document);

        RasterLayer layer = document.CreateRasterLayer("Body");
        document.GetOrCreateCel(layer, document.Frames[0]).Pixels.SetPixel(1, 1, Red);

        int originalIndex = document.Root.IndexOf(layer);

        GroupLayer folder = new(Guid.NewGuid(), "Folder");
        workspace.History.Execute(new GroupLayersCommand("Group Layer", document, folder, [layer]));
        workspace.History.Undo();

        Assert.Same(document.Root, layer.Parent);
        Assert.Equal(originalIndex, document.Root.IndexOf(layer));
        Assert.DoesNotContain(folder, document.Root.Children);

        // The artwork must survive the round trip: cels are keyed by layer identity, so
        // reparenting never touches them.
        Assert.Equal(Red, document.Cels.Find(layer.Id, document.Frames[0].Id)!.Pixels.GetPixel(1, 1));
    }

    [Fact]
    public void GroupingCanBeRedone()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(4, 4));
        DocumentWorkspace workspace = new(document);

        RasterLayer layer = document.CreateRasterLayer("Body");
        GroupLayer folder = new(Guid.NewGuid(), "Folder");

        workspace.History.Execute(new GroupLayersCommand("Group Layer", document, folder, [layer]));
        workspace.History.Undo();
        workspace.History.Redo();

        Assert.Same(folder, layer.Parent);
        Assert.Contains(folder, document.Root.Children);
    }

    [Fact]
    public void GroupingDoesNotChangeTheCompositedImage()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(4, 4));
        LayerNode layer = document.Root.Children[0];
        document.GetOrCreateCel(layer, document.Frames[0]).Pixels.Fill(Red);

        LayerCompositor compositor = new();
        PixelBuffer before = compositor.ComposeToNewBuffer(document, document.Frames[0]);

        GroupLayer folder = new(Guid.NewGuid(), "Folder");
        new GroupLayersCommand("Group Layer", document, folder, [layer]).Redo();

        PixelBuffer after = compositor.ComposeToNewBuffer(document, document.Frames[0]);

        // A fresh folder has neutral opacity and blend mode, so putting a layer inside one
        // must be invisible on the canvas.
        Assert.True(before.ContentEquals(after));
    }

    [Fact]
    public void HidingAFolderHidesEverythingInside()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(4, 4));

        GroupLayer folder = document.CreateGroupLayer("Character");
        RasterLayer inside = document.CreateRasterLayer("Arm", folder);
        document.GetOrCreateCel(inside, document.Frames[0]).Pixels.Fill(Red);

        folder.IsVisible = false;

        Assert.False(inside.IsEffectivelyVisible);

        PixelBuffer flattened = new LayerCompositor().ComposeToNewBuffer(document, document.Frames[0]);
        Assert.True(flattened.GetPixel(1, 1).IsTransparent);
    }

    [Fact]
    public void ALayerCanBeMovedIntoAFolderAndBackOutAgain()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(4, 4));
        DocumentWorkspace workspace = new(document);

        GroupLayer folder = document.CreateGroupLayer("Character");
        RasterLayer layer = document.CreateRasterLayer("Arm");
        document.GetOrCreateCel(layer, document.Frames[0]).Pixels.SetPixel(2, 2, Red);

        int originalIndex = document.Root.IndexOf(layer);

        // This is what dropping a row onto a folder row does: move to the end of the folder's
        // children, which is the top of the folder as the layers panel displays it.
        workspace.History.Execute(new MoveLayerCommand(
            "Move Into Folder", document, layer, folder, folder.Children.Count));

        Assert.Same(folder, layer.Parent);
        Assert.Contains(layer, folder.Children);

        workspace.History.Undo();

        Assert.Same(document.Root, layer.Parent);
        Assert.Equal(originalIndex, document.Root.IndexOf(layer));
        Assert.Empty(folder.Children);

        // Reparenting never touches cels, which are keyed by layer identity.
        Assert.Equal(Red, document.Cels.Find(layer.Id, document.Frames[0].Id)!.Pixels.GetPixel(2, 2));
    }

    [Fact]
    public void DeletingALayerInsideAFolderLeavesTheFolderBehind()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(4, 4));

        GroupLayer folder = document.CreateGroupLayer("Character");
        RasterLayer inside = document.CreateRasterLayer("Arm", folder);

        document.RemoveLayer(inside);

        // The layers panel allows this even when the folder is the only top-level layer,
        // because there is still somewhere to draw once a new layer is added.
        Assert.Contains(folder, document.Root.Children);
        Assert.Empty(folder.Children);
    }

    [Fact]
    public void RemovingAFolderTakesItsChildrensArtworkWithIt()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(4, 4));

        GroupLayer folder = document.CreateGroupLayer("Character");
        RasterLayer inside = document.CreateRasterLayer("Arm", folder);
        document.GetOrCreateCel(inside, document.Frames[0]).Pixels.Fill(Red);

        IReadOnlyList<Cel> removed = document.RemoveLayer(folder);

        // The cels come back to the caller so that deleting a folder stays undoable.
        Assert.Single(removed);
        Assert.Null(document.Cels.Find(inside.Id, document.Frames[0].Id));
    }
}
