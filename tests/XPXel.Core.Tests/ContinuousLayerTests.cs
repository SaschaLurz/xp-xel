using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.History;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Rendering;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for layers whose content is one drawing shared by every frame — the answer to
/// "I want a white background behind the whole animation", which otherwise means making the
/// same drawing once per frame and maintaining every copy by hand.
/// </summary>
public sealed class ContinuousLayerTests
{
    private static readonly PixelColor White = new(255, 255, 255);
    private static readonly PixelColor Red = new(255, 0, 0);

    private static PixelDocument CreateDocument(int frames)
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(4, 4));

        for (int i = 1; i < frames; i++)
        {
            document.CreateFrame();
        }

        return document;
    }

    [Fact]
    public void AContinuousLayerShowsOnEveryFrame()
    {
        PixelDocument document = CreateDocument(3);
        RasterLayer background = (RasterLayer)document.Root.Children[0];

        document.GetOrCreateCel(background, document.Frames[0]).Pixels.Fill(White);
        document.SetLayerContinuous(background, continuous: true);

        // One drawing, resolved from any frame. Nothing downstream — the compositor, the
        // thumbnails, the exporters — needed to know that continuous layers exist.
        foreach (Frame frame in document.Frames)
        {
            PixelBuffer flattened = new LayerCompositor().ComposeToNewBuffer(document, frame);
            Assert.Equal(White, flattened.GetPixel(2, 2));
        }
    }

    [Fact]
    public void AFrameAddedAfterwardsGetsItToo()
    {
        PixelDocument document = CreateDocument(2);
        RasterLayer background = (RasterLayer)document.Root.Children[0];

        document.GetOrCreateCel(background, document.Frames[0]).Pixels.Fill(White);
        document.SetLayerContinuous(background, continuous: true);

        Frame added = document.CreateFrame();

        // The whole point: the background does not have to be maintained as the animation
        // grows, which is what makes it different from copying the cel to every frame.
        PixelBuffer flattened = new LayerCompositor().ComposeToNewBuffer(document, added);
        Assert.Equal(White, flattened.GetPixel(0, 0));
    }

    [Fact]
    public void EditingItFromAnyFrameChangesItEverywhere()
    {
        PixelDocument document = CreateDocument(3);
        RasterLayer background = (RasterLayer)document.Root.Children[0];

        document.GetOrCreateCel(background, document.Frames[0]).Pixels.Fill(White);
        document.SetLayerContinuous(background, continuous: true);

        // Drawn on the last frame; there is only one drawing, so frame one changes with it.
        document.GetOrCreateCel(background, document.Frames[2]).Pixels.SetPixel(1, 1, Red);

        PixelBuffer first = new LayerCompositor().ComposeToNewBuffer(document, document.Frames[0]);
        Assert.Equal(Red, first.GetPixel(1, 1));
    }

    [Fact]
    public void DeletingAFrameLeavesTheBackgroundAlone()
    {
        PixelDocument document = CreateDocument(3);
        RasterLayer background = (RasterLayer)document.Root.Children[0];

        document.GetOrCreateCel(background, document.Frames[0]).Pixels.Fill(White);
        document.SetLayerContinuous(background, continuous: true);

        document.RemoveFrame(document.Frames[1]);

        // The one drawing belongs to every frame, so losing one frame is no reason to take the
        // background away from the others.
        PixelBuffer flattened = new LayerCompositor().ComposeToNewBuffer(document, document.Frames[0]);
        Assert.Equal(White, flattened.GetPixel(0, 0));
    }

    [Fact]
    public void TurningItOffGivesEveryFrameItsOwnCopy()
    {
        PixelDocument document = CreateDocument(3);
        RasterLayer background = (RasterLayer)document.Root.Children[0];

        document.GetOrCreateCel(background, document.Frames[0]).Pixels.Fill(White);
        document.SetLayerContinuous(background, continuous: true);
        document.SetLayerContinuous(background, continuous: false);

        // Each frame now has its own drawing, so changing one leaves the others alone.
        document.GetOrCreateCel(background, document.Frames[1]).Pixels.SetPixel(0, 0, Red);

        Assert.Equal(White, document.Cels.Find(background.Id, document.Frames[0].Id)!.Pixels.GetPixel(0, 0));
        Assert.Equal(Red, document.Cels.Find(background.Id, document.Frames[1].Id)!.Pixels.GetPixel(0, 0));
    }

    [Fact]
    public void TurningItOnIsUndoable()
    {
        PixelDocument document = CreateDocument(2);
        DocumentWorkspace workspace = new(document);
        RasterLayer layer = (RasterLayer)document.Root.Children[0];

        document.GetOrCreateCel(layer, document.Frames[0]).Pixels.Fill(White);
        document.GetOrCreateCel(layer, document.Frames[1]).Pixels.Fill(Red);

        workspace.History.Execute(new SetLayerContinuousCommand(
            "Same on Every Frame", document, layer, continuous: true, keepFrom: document.Frames[0]));

        // Collapsing several frames into one has to choose, and the frame asked for wins.
        Assert.True(layer.IsContinuous);
        Assert.Equal(White, document.Cels.Find(layer.Id, document.Frames[1].Id)!.Pixels.GetPixel(0, 0));

        workspace.History.Undo();

        // What the other frames held comes back, or the command would be a quiet way to lose
        // an animation.
        Assert.False(layer.IsContinuous);
        Assert.Equal(White, document.Cels.Find(layer.Id, document.Frames[0].Id)!.Pixels.GetPixel(0, 0));
        Assert.Equal(Red, document.Cels.Find(layer.Id, document.Frames[1].Id)!.Pixels.GetPixel(0, 0));
    }

    [Fact]
    public void CopyingACelToAllFramesLeavesThemIndependent()
    {
        PixelDocument document = CreateDocument(3);
        DocumentWorkspace workspace = new(document);
        RasterLayer layer = (RasterLayer)document.Root.Children[0];

        document.GetOrCreateCel(layer, document.Frames[0]).Pixels.Fill(White);

        workspace.History.Execute(new CopyCelToAllFramesCommand(
            "Copy to All Frames", document, layer, document.Frames[0]));

        Assert.Equal(White, document.Cels.Find(layer.Id, document.Frames[2].Id)!.Pixels.GetPixel(3, 3));

        // Copies, not one shared drawing: changing frame three must not change frame one.
        document.Cels.Find(layer.Id, document.Frames[2].Id)!.Pixels.SetPixel(0, 0, Red);
        Assert.Equal(White, document.Cels.Find(layer.Id, document.Frames[0].Id)!.Pixels.GetPixel(0, 0));

        workspace.History.Undo();
        Assert.Null(document.Cels.Find(layer.Id, document.Frames[2].Id));
    }

    [Fact]
    public void AContinuousLayerIsNotDuplicatedOnceEveryFrame()
    {
        PixelDocument document = CreateDocument(4);
        RasterLayer background = (RasterLayer)document.Root.Children[0];

        document.GetOrCreateCel(background, document.Frames[0]).Pixels.Fill(White);
        document.SetLayerContinuous(background, continuous: true);

        // One cel in the store, not four. Anything that walks every cel — a canvas resize, a
        // save — would otherwise do the same work four times and end up with four separate
        // drawings, which would silently break the sharing.
        Assert.Equal(1, document.Cels.Count);
        Assert.Single(document.Cels.All);
    }
}
