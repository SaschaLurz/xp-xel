using XPXel.Core.Documents;
using XPXel.Core.Primitives;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for the presentation settings carried by <see cref="LayerNode"/>, and in particular
/// for the organisational label colour, which must travel with a layer wherever it goes.
/// </summary>
public sealed class LayerTests
{
    private static readonly PixelColor Label = new(228, 87, 46);

    [Fact]
    public void ANewLayerHasNoLabelColour()
    {
        RasterLayer layer = new(Guid.NewGuid(), "Layer 1");

        Assert.Null(layer.TagColor);
    }

    [Fact]
    public void DuplicatingALayerCarriesItsLabelColour()
    {
        PixelDocument document = new(new ImageSize(4, 4));
        RasterLayer source = document.CreateRasterLayer("Arm");
        source.TagColor = Label;

        LayerNode copy = document.DuplicateLayer(source, "Arm copy");

        Assert.Equal(Label, copy.TagColor);
    }

    [Fact]
    public void ChangingTheLabelColourRaisesAChangeNotification()
    {
        RasterLayer layer = new(Guid.NewGuid(), "Layer 1");
        List<string?> changed = [];
        layer.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        layer.TagColor = Label;

        // The layers panel redraws its colour strip from this, so a silent set would leave
        // the artist looking at a layer whose label had already changed.
        Assert.Contains(nameof(LayerNode.TagColor), changed);
    }

    [Fact]
    public void ClearingTheLabelColourIsAllowed()
    {
        RasterLayer layer = new(Guid.NewGuid(), "Layer 1") { TagColor = Label };

        layer.TagColor = null;

        Assert.Null(layer.TagColor);
    }
}
