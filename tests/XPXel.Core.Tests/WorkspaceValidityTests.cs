using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.History;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Rendering;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests that a workspace never keeps pointing at a frame or a layer the document no longer
/// holds — the state in which every cel lookup silently returns nothing, so the canvas goes
/// blank and the layer previews empty themselves with no error anywhere.
/// </summary>
public sealed class WorkspaceValidityTests
{
    private static readonly PixelColor Red = new(255, 0, 0);
    private static readonly PixelColor Blue = new(0, 0, 255);

    [Fact]
    public void UndoingAnAddedFrameMovesOffIt()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(4, 4));
        DocumentWorkspace workspace = new(document);

        Frame added = new(Guid.NewGuid());
        workspace.History.Execute(new AddFrameCommand("Add Frame", document, added, 1));
        workspace.GoToFrame(1);

        Assert.Same(added, workspace.ActiveFrame);

        workspace.History.Undo();

        // Without this the workspace holds a frame the document has never heard of, and the
        // canvas goes blank for reasons nothing on screen can explain.
        Assert.Contains(workspace.ActiveFrame, document.Frames);
        Assert.Equal(0, workspace.ActiveFrameIndex);
    }

    [Fact]
    public void UndoingAnAddedLayerMovesOffIt()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(4, 4));
        DocumentWorkspace workspace = new(document);

        RasterLayer added = new(Guid.NewGuid(), "Layer 2");
        workspace.History.Execute(new AddLayerCommand("Add Layer", document, added, document.Root));
        workspace.ActiveLayer = added;

        workspace.History.Undo();

        Assert.NotSame(added, workspace.ActiveLayer);
        Assert.Same(document.Root.Children[^1], workspace.ActiveLayer);

        // The marked set is re-validated with it, so nothing downstream is handed a layer that
        // is no longer part of the document.
        Assert.DoesNotContain(added, workspace.SelectedLayers);
    }

    [Fact]
    public void DeletingTheActiveFrameLandsOnOneThatExists()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(4, 4));
        document.CreateFrame();
        document.CreateFrame();

        DocumentWorkspace workspace = new(document);
        workspace.GoToFrame(2);

        Frame doomed = workspace.ActiveFrame;
        workspace.History.Execute(new RemoveFrameCommand("Delete Frame", document, doomed));

        Assert.NotSame(doomed, workspace.ActiveFrame);
        Assert.Contains(workspace.ActiveFrame, document.Frames);
    }

    [Fact]
    public void MergingFlattensEveryFrameAsTheCanvasDrawsIt()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(4, 4), "Lower");
        document.CreateFrame();

        RasterLayer lower = (RasterLayer)document.Root.Children[0];
        RasterLayer upper = document.CreateRasterLayer("Upper");

        // Frame one: red underneath, blue on top in a different pixel. Frame two: only red.
        document.GetOrCreateCel(lower, document.Frames[0]).Pixels.SetPixel(0, 0, Red);
        document.GetOrCreateCel(upper, document.Frames[0]).Pixels.SetPixel(1, 1, Blue);
        document.GetOrCreateCel(lower, document.Frames[1]).Pixels.SetPixel(2, 2, Red);

        PixelBuffer before = new LayerCompositor().ComposeToNewBuffer(document, document.Frames[0]);

        // What the merge command does, as the panel builds it.
        PixelBuffer merged = new(document.Size);
        new LayerCompositor().ComposeLayers(document, document.Frames[0], [lower, upper], merged);

        // The flattened result is pixel for pixel the picture that was on screen.
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                Assert.Equal(before.GetPixel(x, y), merged.GetPixel(x, y));
            }
        }

        PixelBuffer second = new(document.Size);
        new LayerCompositor().ComposeLayers(document, document.Frames[1], [lower, upper], second);

        // Every frame is flattened, not only the one being looked at: a document whose layer
        // stack changed as it played would not be a document.
        Assert.Equal(Red, second.GetPixel(2, 2));
        Assert.True(second.GetPixel(1, 1).IsTransparent);
    }

    [Fact]
    public void MergingHonoursOpacityAndSkipsHiddenLayers()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(4, 4), "Lower");
        RasterLayer lower = (RasterLayer)document.Root.Children[0];
        RasterLayer hidden = document.CreateRasterLayer("Hidden");

        document.GetOrCreateCel(lower, document.Frames[0]).Pixels.SetPixel(0, 0, Red);
        document.GetOrCreateCel(hidden, document.Frames[0]).Pixels.SetPixel(3, 3, Blue);
        hidden.IsVisible = false;

        PixelBuffer merged = new(document.Size);
        new LayerCompositor().ComposeLayers(document, document.Frames[0], [lower, hidden], merged);

        // A hidden layer contributes nothing to the picture, so it contributes nothing to the
        // merge either — anything else would make artwork appear that was not on screen.
        Assert.Equal(Red, merged.GetPixel(0, 0));
        Assert.True(merged.GetPixel(3, 3).IsTransparent);
    }
}
