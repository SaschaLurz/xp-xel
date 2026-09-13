using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Rendering;
using XPXel.Core.Selection;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for copying pixels out of a document and pasting them back in — what Ctrl+C, Ctrl+X
/// and Ctrl+V do to the artwork.
/// </summary>
public sealed class ClipboardTests
{
    private static readonly PixelColor Red = new(255, 0, 0);
    private static readonly PixelColor Blue = new(0, 0, 255);
    private static readonly PixelColor Ghost = new(255, 0, 0, 128);

    [Fact]
    public void CopyingTakesTheMarkedPixelsAndWhereTheyCameFrom()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(2, 3, 2, 2), Red);
        fixture.Select(new ImageRect(2, 3, 2, 2));

        PixelClip clip = PixelClip.FromSelection(fixture.Workspace)!;

        Assert.Equal(new ImageSize(2, 2), clip.Size);
        Assert.Equal(new ImagePoint(2, 3), clip.Origin);
        Assert.Equal(Red, clip.Pixels[0]);
        Assert.Equal(byte.MaxValue, clip.Coverage[0]);
    }

    [Fact]
    public void CopyingWithNothingMarkedTakesTheWholeLayer()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(2, 2, 3, 3), Red);

        // Refusing was a dead end: there was no other way to get a layer onto the clipboard,
        // so "mark something first" left an artist who wanted the whole layer with nowhere to
        // go. The clip is the size of the artwork, not of the canvas.
        PixelClip clip = PixelClip.FromSelection(fixture.Workspace)!;

        Assert.Equal(new ImageSize(3, 3), clip.Size);
        Assert.Equal(new ImagePoint(2, 2), clip.Origin);
        Assert.Equal(Red, clip.Pixels[0]);
    }

    [Fact]
    public void CopyingAnEmptyLayerStillProducesNoClip()
    {
        Fixture fixture = new();

        Assert.Null(PixelClip.FromSelection(fixture.Workspace));
    }

    [Fact]
    public void ANonRectangularSelectionOnlyCopiesWhatIsMarked()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 8, 8), Red);

        // Two pixels on a diagonal, so the clip is a 2×2 box with two corners uncovered.
        SelectionMask mask = new(new ImageSize(8, 8));
        mask.SetCoverage(1, 1, byte.MaxValue);
        mask.SetCoverage(2, 2, byte.MaxValue);
        mask.RecalculateBounds();
        fixture.Workspace.Selection = mask;

        PixelClip clip = PixelClip.FromSelection(fixture.Workspace)!;

        Assert.Equal(new ImageSize(2, 2), clip.Size);
        Assert.Equal(byte.MaxValue, clip.Coverage[0]);
        Assert.Equal(0, clip.Coverage[1]);
        Assert.Equal(0, clip.Coverage[2]);
        Assert.Equal(byte.MaxValue, clip.Coverage[3]);
    }

    [Fact]
    public void PastingStampsTheClipAndLeavesItFloatingInATransformBox()
    {
        Fixture fixture = new();
        PixelClip clip = PixelClip.FromImage(new ImageSize(2, 1), [Red, Blue]);

        Assert.True(fixture.Workspace.BeginPaste(clip));

        // A box with handles rather than pixels merely in the hand, so what arrived can be
        // resized before it is placed instead of afterwards as a second undoable act.
        Assert.NotNull(fixture.Workspace.ActiveFreeTransform);
        Assert.Equal(FreeTransformScope.Clip, fixture.Workspace.ActiveFreeTransform!.Scope);

        // An 8×8 canvas and a 2×1 clip with no origin: centred, so (3, 3).
        Assert.Equal(Red, fixture.Sample(3, 3));
        Assert.Equal(Blue, fixture.Sample(4, 3));

        // Still in the air, so nothing has reached the history yet.
        Assert.Equal(0, fixture.Workspace.History.Count);
    }

    [Fact]
    public void PastedPixelsCanBeDraggedBeforeTheyArePlaced()
    {
        Fixture fixture = new();

        fixture.Workspace.BeginPaste(PixelClip.FromImage(new ImageSize(2, 1), [Red, Blue]));
        fixture.Workspace.ActiveFreeTransform!.MoveBy(-3, -3);

        Assert.True(fixture.Workspace.CommitFreeTransform());
        Assert.Equal(Red, fixture.Sample(0, 0));
        Assert.True(fixture.Sample(3, 3).IsTransparent);
    }

    [Fact]
    public void PastedPixelsCanBeResizedBeforeTheyArePlaced()
    {
        Fixture fixture = new();

        fixture.Workspace.BeginPaste(PixelClip.FromImage(new ImageSize(2, 1), [Red, Blue]));

        FreeTransform box = fixture.Workspace.ActiveFreeTransform!;
        box.BeginDrag();
        box.DragEdges(0, 0, 4, 2);

        Assert.True(fixture.Workspace.CommitFreeTransform());

        // Each pixel of the clip became a two-by-two block, in the colour it arrived with.
        Assert.Equal(Red, fixture.Sample(0, 0));
        Assert.Equal(Red, fixture.Sample(1, 1));
        Assert.Equal(Blue, fixture.Sample(2, 0));
        Assert.Equal(Blue, fixture.Sample(3, 1));
    }

    [Fact]
    public void PlacingAPasteIsOneUndoableStepThatLeavesNoTrace()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 8, 8), Blue);

        fixture.Workspace.BeginPaste(PixelClip.FromImage(new ImageSize(2, 1), [Red, Red]));
        fixture.Workspace.CommitFreeTransform();

        Assert.Equal(1, fixture.Workspace.History.Count);

        fixture.Workspace.History.Undo();

        // The artwork underneath is back: pasting punches no hole, unlike moving.
        Assert.Single(fixture.Workspace.Document.Root.Children);
        Assert.Equal(Blue, fixture.Sample(3, 3));
        Assert.Equal(Blue, fixture.Sample(4, 3));
    }

    [Fact]
    public void ArtworkFromAnotherProgramLandsOnALayerOfItsOwn()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 8, 8), Blue);

        fixture.Workspace.BeginPaste(
            PixelClip.FromImage(new ImageSize(2, 1), [Red, Red]), fromOutside: true);
        fixture.Workspace.CommitFreeTransform();

        // An image dragged in from a browser has nothing to do with the layer it lands on, so
        // the blue underneath it is still there rather than having been written over.
        Assert.Equal(2, fixture.Workspace.Document.Root.Children.Count);
        Assert.Equal(Red, fixture.Sample(3, 3));
        Assert.Equal(Blue, fixture.CelPixel((RasterLayer)fixture.Workspace.Document.Root.Children[0], 3, 3));
    }

    [Fact]
    public void PixelsCopiedInsideXpXelPasteOntoTheLayerYouAreOn()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 8, 8), Blue);

        fixture.Workspace.BeginPaste(PixelClip.FromImage(new ImageSize(2, 1), [Red, Red]));
        fixture.Workspace.CommitFreeTransform();

        // The artist knows where these came from. A layer per paste is what turned "put this
        // background on each frame" into a stack of layers nobody asked for.
        Assert.Single(fixture.Workspace.Document.Root.Children);
        Assert.Equal(Red, fixture.Sample(3, 3));
    }

    [Fact]
    public void PastingFromOutsideOntoTheSameLayerIsAnOptionTheArtistCanTake()
    {
        Fixture fixture = new();
        fixture.Workspace.LayerSettings.PasteToNewLayer = false;
        fixture.Fill(new ImageRect(0, 0, 8, 8), Blue);

        fixture.Workspace.BeginPaste(
            PixelClip.FromImage(new ImageSize(2, 1), [Red, Red]), fromOutside: true);
        fixture.Workspace.CommitFreeTransform();

        Assert.Single(fixture.Workspace.Document.Root.Children);
        Assert.Equal(Red, fixture.Sample(3, 3));
    }

    [Fact]
    public void PlacingAPasteClearsTheMarks()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 4, 4), Red);
        fixture.Select(new ImageRect(0, 0, 4, 4));

        fixture.Workspace.BeginPaste(PixelClip.FromImage(new ImageSize(2, 1), [Red, Blue]));
        fixture.Workspace.CommitFreeTransform();

        // Enter means done: the pixels are placed and the outline that was up before they
        // arrived goes with them.
        Assert.Null(fixture.Workspace.Selection);
    }

    [Fact]
    public void AbandoningAPasteLeavesTheDocumentUntouched()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(0, 0, 8, 8), Blue);

        fixture.Workspace.BeginPaste(PixelClip.FromImage(new ImageSize(2, 1), [Red, Red]));
        fixture.Workspace.CancelFreeTransform();

        Assert.Equal(Blue, fixture.Sample(3, 3));
        Assert.Single(fixture.Workspace.Document.Root.Children);
        Assert.Equal(0, fixture.Workspace.History.Count);
    }

    [Fact]
    public void AClipCutFromTheCanvasPastesBackWhereItCameFrom()
    {
        Fixture fixture = new();
        fixture.Fill(new ImageRect(5, 5, 2, 2), Red);
        fixture.Select(new ImageRect(5, 5, 2, 2));

        PixelClip clip = PixelClip.FromSelection(fixture.Workspace)!;
        fixture.Workspace.EraseSelection("Cut");
        fixture.Workspace.Selection = null;

        fixture.Workspace.BeginPaste(clip);
        fixture.Workspace.CommitFreeTransform();

        Assert.Equal(Red, fixture.Sample(5, 5));
    }

    [Fact]
    public void AClipTooLargeToFitIsCentredInstead()
    {
        // Copied from the corner of a bigger canvas, then pasted into a smaller one: its origin
        // would hang off the edge, so it is centred rather than half lost.
        PixelClip clip = new(
            new ImageSize(2, 2),
            [Red, Red, Red, Red],
            [255, 255, 255, 255],
            new ImagePoint(7, 7));

        Assert.Equal(new ImagePoint(3, 3), clip.GetPasteOrigin(new ImageRect(0, 0, 8, 8)));
    }

    [Fact]
    public void AClipSurvivesTheTripThroughTheClipboardExactly()
    {
        PixelClip original = new(
            new ImageSize(2, 2),
            [Ghost, Blue, PixelColor.Transparent, Red],
            [255, 0, 128, 255],
            new ImagePoint(4, 6));

        Assert.True(PixelClip.TryFromBytes(original.ToBytes(), out PixelClip? restored));

        Assert.Equal(original.Size, restored!.Size);
        Assert.Equal(original.Origin, restored.Origin);
        Assert.True(original.Pixels.SequenceEqual(restored.Pixels));
        Assert.True(original.Coverage.SequenceEqual(restored.Coverage));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(64)]
    public void BytesThatAreNotAClipAreDeclinedRatherThanGuessedAt(int length)
    {
        Assert.False(PixelClip.TryFromBytes(new byte[length], out PixelClip? clip));
        Assert.Null(clip);
    }

    [Fact]
    public void ATruncatedClipIsDeclined()
    {
        PixelClip original = PixelClip.FromImage(new ImageSize(4, 4), new PixelColor[16]);
        byte[] bytes = original.ToBytes();

        Assert.False(PixelClip.TryFromBytes(bytes[..(bytes.Length / 2)], out PixelClip? clip));
        Assert.Null(clip);
    }

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

        /// <summary>Reads a pixel of one layer's own content, in document coordinates.</summary>
        public PixelColor CelPixel(RasterLayer layer, int x, int y)
        {
            Cel cel = Workspace.Document.Cels.Find(layer.Id, Workspace.Document.Frames[0].Id)!;
            return cel.Pixels.GetPixelOrTransparent(x - cel.Offset.X, y - cel.Offset.Y);
        }

        /// <summary>Reads a pixel of the flattened image.</summary>
        public PixelColor Sample(int x, int y)
        {
            PixelBuffer flattened = new(Workspace.Document.Size);
            new LayerCompositor().Compose(Workspace.Document, Workspace.Document.Frames[0], flattened);
            return flattened.GetPixelOrTransparent(x, y);
        }
    }
}
