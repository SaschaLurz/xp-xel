using XPXel.Core.Primitives;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for HSV conversion and for the colours suggested alongside a chosen one.
/// </summary>
public sealed class ColorHarmonyTests
{
    [Theory]
    [InlineData(255, 0, 0)]
    [InlineData(0, 255, 0)]
    [InlineData(0, 0, 255)]
    [InlineData(18, 52, 86)]
    [InlineData(200, 130, 90)]
    [InlineData(255, 255, 255)]
    [InlineData(0, 0, 0)]
    public void ConvertingToHsvAndBackReturnsTheSameColour(byte r, byte g, byte b)
    {
        PixelColor original = new(r, g, b);

        PixelColor round = HsvColor.FromColor(original).ToColor();

        Assert.Equal(original, round);
    }

    [Fact]
    public void ConversionKeepsTheAlphaItIsGiven()
    {
        PixelColor faded = new(120, 30, 200, 64);

        Assert.Equal(64, HsvColor.FromColor(faded).ToColor(faded.A).A);
    }

    [Theory]
    [InlineData(255, 0, 0, 0)]
    [InlineData(0, 255, 0, 120)]
    [InlineData(0, 0, 255, 240)]
    public void PrimaryHuesLandWhereExpected(byte r, byte g, byte b, double hue)
    {
        Assert.Equal(hue, HsvColor.FromColor(new PixelColor(r, g, b)).Hue, 3);
    }

    [Fact]
    public void AGreyHasNoSaturation()
    {
        Assert.Equal(0.0, HsvColor.FromColor(new PixelColor(128, 128, 128)).Saturation, 5);
    }

    [Fact]
    public void HuesWrapRatherThanRunningOffTheEnd()
    {
        Assert.Equal(10.0, HsvColor.Wrap(370.0), 5);
        Assert.Equal(350.0, HsvColor.Wrap(-10.0), 5);
        Assert.Equal(0.0, HsvColor.Wrap(360.0), 5);
    }

    [Fact]
    public void ARampRunsFromDarkestToLightestThroughTheChosenColour()
    {
        PixelColor blue = new(60, 90, 200);

        IReadOnlyList<ColorSuggestion> ramp = ColorHarmony.GetShadingRamp(blue, steps: 3);

        Assert.Equal(7, ramp.Count);
        Assert.Equal(blue, ramp[3].Color);

        for (int i = 1; i < ramp.Count; i++)
        {
            double previous = HsvColor.FromColor(ramp[i - 1].Color).Value;
            double current = HsvColor.FromColor(ramp[i].Color).Value;

            Assert.True(current > previous, $"step {i} is not lighter than step {i - 1}");
        }
    }

    [Fact]
    public void ShadowsShiftTowardsBlueAndHighlightsTowardsYellow()
    {
        // Red sits at hue 0, so both targets are reached by rotating in opposite directions
        // and the shift is unambiguous.
        IReadOnlyList<ColorSuggestion> ramp = ColorHarmony.GetShadingRamp(new PixelColor(220, 40, 40), steps: 2);

        double shadow = HsvColor.FromColor(ramp[1].Color).Hue;
        double light = HsvColor.FromColor(ramp[3].Color).Hue;

        // Towards blue from red is anticlockwise, which wraps past 360.
        Assert.InRange(shadow, 300.0, 359.9);

        // Towards yellow is clockwise, into the oranges.
        Assert.InRange(light, 0.1, 60.0);
    }

    [Fact]
    public void ARampNeverRotatesPastItsTarget()
    {
        // Blue is already at the shadow hue, so no amount of darkening may carry it round
        // into cyan or violet — the shorter-arc rotation has nowhere to go.
        IReadOnlyList<ColorSuggestion> ramp = ColorHarmony.GetShadingRamp(new PixelColor(0, 0, 255), steps: 3);

        Assert.Equal(240.0, HsvColor.FromColor(ramp[0].Color).Hue, 1);
    }

    [Fact]
    public void ARampStaysInsideTheColourSpaceAtTheExtremes()
    {
        foreach (PixelColor extreme in (ReadOnlySpan<PixelColor>)[PixelColor.Black, PixelColor.White])
        {
            IReadOnlyList<ColorSuggestion> ramp = ColorHarmony.GetShadingRamp(extreme, steps: 3);

            Assert.Equal(7, ramp.Count);
            Assert.All(ramp, entry => Assert.True(entry.Color.IsOpaque));
        }
    }

    [Fact]
    public void ARampKeepsTheAlphaOfTheColourItCameFrom()
    {
        IReadOnlyList<ColorSuggestion> ramp = ColorHarmony.GetShadingRamp(new PixelColor(200, 60, 60, 128));

        Assert.All(ramp, entry => Assert.Equal(128, entry.Color.A));
    }

    [Fact]
    public void TheComplementSitsOppositeOnTheWheel()
    {
        PixelColor red = new(255, 0, 0);

        ColorSuggestion complement = ColorHarmony.GetHarmonies(red)[0];

        Assert.Equal("Complement", complement.Name);
        Assert.Equal(180.0, HsvColor.FromColor(complement.Color).Hue, 1);
    }

    [Fact]
    public void AGreyIsOfferedNoHarmoniesBecauseItHasNoHue()
    {
        // Every rotation of a grey is the same grey, so five identical swatches would be
        // worse than none at all.
        Assert.Empty(ColorHarmony.GetHarmonies(new PixelColor(128, 128, 128)));
    }
}
