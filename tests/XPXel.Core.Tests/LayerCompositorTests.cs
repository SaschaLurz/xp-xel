using XPXel.Core.Documents;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Rendering;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for <see cref="LayerCompositor"/>. These are the tests that pin down what the
/// artist actually sees, so they check stacking order, visibility, opacity, blend modes,
/// group isolation and — importantly — that partial recomposition of a dirty rectangle
/// produces the same result as a full pass.
/// </summary>
public sealed class LayerCompositorTests
{
    private static readonly PixelColor Red = new(255, 0, 0);
    private static readonly PixelColor Blue = new(0, 0, 255);

    [Fact]
    public void EmptyDocumentCompositesToTransparent()
    {
        PixelDocument document = new(new ImageSize(2, 2));
        PixelBuffer result = new LayerCompositor().ComposeToNewBuffer(document, document.Frames[0]);

        Assert.Equal(PixelColor.Transparent, result.GetPixel(0, 0));
    }

    [Fact]
    public void UpperLayerCoversLowerLayer()
    {
        PixelDocument document = new(new ImageSize(2, 2));
        FillNewLayer(document, "Bottom", Red);
        FillNewLayer(document, "Top", Blue);

        PixelBuffer result = new LayerCompositor().ComposeToNewBuffer(document, document.Frames[0]);

        // Children are stored bottom-first, so the layer added second is on top.
        Assert.Equal(Blue, result.GetPixel(0, 0));
    }

    [Fact]
    public void HiddenLayerIsSkipped()
    {
        PixelDocument document = new(new ImageSize(2, 2));
        FillNewLayer(document, "Bottom", Red);
        RasterLayer top = FillNewLayer(document, "Top", Blue);
        top.IsVisible = false;

        PixelBuffer result = new LayerCompositor().ComposeToNewBuffer(document, document.Frames[0]);

        Assert.Equal(Red, result.GetPixel(0, 0));
    }

    [Fact]
    public void LayerOpacityBlendsTowardsTheLayerBelow()
    {
        PixelDocument document = new(new ImageSize(1, 1));
        FillNewLayer(document, "Bottom", Red);
        RasterLayer top = FillNewLayer(document, "Top", Blue);
        top.Opacity = 0.5;

        PixelColor result = new LayerCompositor()
            .ComposeToNewBuffer(document, document.Frames[0])
            .GetPixel(0, 0);

        // Half of pure blue over pure red: both channels land on the midpoint, allowing for
        // rounding of the 0..255 quantisation.
        Assert.InRange(result.R, 127, 128);
        Assert.InRange(result.B, 127, 128);
        Assert.Equal(255, result.A);
    }

    [Fact]
    public void MultiplyBlendModeDarkens()
    {
        PixelDocument document = new(new ImageSize(1, 1));
        FillNewLayer(document, "Bottom", new PixelColor(128, 128, 128));
        RasterLayer top = FillNewLayer(document, "Top", new PixelColor(128, 128, 128));
        top.BlendMode = BlendMode.Multiply;

        PixelColor result = new LayerCompositor()
            .ComposeToNewBuffer(document, document.Frames[0])
            .GetPixel(0, 0);

        // 0.5 * 0.5 = 0.25, which is 64 of 255.
        Assert.InRange(result.R, 63, 65);
    }

    [Fact]
    public void GroupOpacityAppliesToTheGroupAsAWhole()
    {
        PixelDocument document = new(new ImageSize(1, 1));
        GroupLayer group = new(Guid.NewGuid(), "Folder") { Opacity = 0.5 };
        document.InsertLayer(group, document.Root);

        RasterLayer inside = document.CreateRasterLayer("Inside", group);
        document.GetOrCreateCel(inside, document.Frames[0]).Pixels.Fill(Red);

        PixelColor result = new LayerCompositor()
            .ComposeToNewBuffer(document, document.Frames[0])
            .GetPixel(0, 0);

        Assert.Equal(255, result.R);
        Assert.InRange(result.A, 127, 128);
    }

    [Fact]
    public void PartialRecompositionMatchesAFullPass()
    {
        PixelDocument document = new(new ImageSize(8, 8));
        RasterLayer bottom = FillNewLayer(document, "Bottom", Red);
        _ = bottom;

        RasterLayer top = document.CreateRasterLayer("Top");
        document.GetOrCreateCel(top, document.Frames[0]).Pixels.FillRect(new ImageRect(2, 2, 3, 3), Blue);

        LayerCompositor compositor = new();
        PixelBuffer full = compositor.ComposeToNewBuffer(document, document.Frames[0]);

        // Compose the whole canvas, dirty a region, then recompose only that region.
        PixelBuffer partial = compositor.ComposeToNewBuffer(document, document.Frames[0]);
        partial.FillRect(new ImageRect(2, 2, 3, 3), PixelColor.White);
        compositor.Compose(document, document.Frames[0], partial, new ImageRect(2, 2, 3, 3));

        Assert.True(full.ContentEquals(partial));
    }

    [Fact]
    public void ComposeRejectsATargetOfTheWrongSize()
    {
        PixelDocument document = new(new ImageSize(4, 4));
        PixelBuffer wrongSize = new(2, 2);

        Assert.Throws<ArgumentException>(
            () => new LayerCompositor().Compose(document, document.Frames[0], wrongSize));
    }

    private static RasterLayer FillNewLayer(PixelDocument document, string name, PixelColor color)
    {
        RasterLayer layer = document.CreateRasterLayer(name);
        document.GetOrCreateCel(layer, document.Frames[0]).Pixels.Fill(color);
        return layer;
    }
}
