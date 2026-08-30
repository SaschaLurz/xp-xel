using XPXel.Core.Primitives;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for the image-space primitives. <see cref="ImageRect"/> gets the most attention
/// because the dirty-rectangle machinery throughout the editor depends on its union and
/// intersection semantics being exactly right.
/// </summary>
public sealed class PrimitivesTests
{
    [Fact]
    public void RectIsHalfOpen()
    {
        ImageRect rect = new(1, 2, 3, 4);

        Assert.Equal(4, rect.Right);
        Assert.Equal(6, rect.Bottom);
        Assert.True(rect.Contains(3, 5));
        Assert.False(rect.Contains(4, 5));
    }

    [Fact]
    public void UnionTreatsEmptyAsIdentity()
    {
        ImageRect rect = new(2, 2, 3, 3);

        Assert.Equal(rect, ImageRect.Empty.Union(rect));
        Assert.Equal(rect, rect.Union(ImageRect.Empty));
    }

    [Fact]
    public void UnionWithPointGrowsToIncludeIt()
    {
        ImageRect accumulated = ImageRect.Empty;

        accumulated = accumulated.Union(5, 5);
        accumulated = accumulated.Union(2, 8);

        Assert.Equal(ImageRect.FromEdges(2, 5, 6, 9), accumulated);
    }

    [Fact]
    public void IntersectOfDisjointRectanglesIsEmpty()
    {
        ImageRect a = new(0, 0, 2, 2);
        ImageRect b = new(5, 5, 2, 2);

        Assert.True(a.Intersect(b).IsEmpty);
        Assert.False(a.IntersectsWith(b));
    }

    [Fact]
    public void FromEdgesClampsInvertedEdgesToEmpty()
    {
        Assert.True(ImageRect.FromEdges(5, 5, 1, 1).IsEmpty);
    }

    [Fact]
    public void ContainsRectRequiresFullContainment()
    {
        ImageRect outer = new(0, 0, 4, 4);

        Assert.True(outer.Contains(new ImageRect(1, 1, 2, 2)));
        Assert.False(outer.Contains(new ImageRect(2, 2, 4, 4)));
    }

    [Theory]
    [InlineData("#FF0000", 255, 0, 0, 255)]
    [InlineData("00FF00", 0, 255, 0, 255)]
    [InlineData("#f00", 255, 0, 0, 255)]
    [InlineData("#11223344", 0x11, 0x22, 0x33, 0x44)]
    [InlineData("#1234", 0x11, 0x22, 0x33, 0x44)]
    public void ColorParsesEveryHexForm(string text, int r, int g, int b, int a)
    {
        Assert.True(PixelColor.TryParseHex(text, out PixelColor color));
        Assert.Equal(new PixelColor((byte)r, (byte)g, (byte)b, (byte)a), color);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public void ColorRejectsMalformedHex(string text) =>
        Assert.False(PixelColor.TryParseHex(text, out _));

    [Fact]
    public void ColorHexRoundTripsAndOmitsAlphaWhenOpaque()
    {
        Assert.Equal("#FF8000", new PixelColor(255, 128, 0).ToHex());
        Assert.Equal("#FF800080", new PixelColor(255, 128, 0, 128).ToHex());
    }

    [Fact]
    public void ColorSimilarityTreatsAllTransparentPixelsAsEqual()
    {
        PixelColor transparentRed = new(255, 0, 0, 0);
        PixelColor transparentBlue = new(0, 0, 255, 0);

        // With any tolerance the stored RGB of a fully transparent pixel is meaningless, so
        // a bucket fill must not treat these as different colours.
        Assert.True(transparentRed.IsSimilarTo(transparentBlue, tolerance: 1));

        // At zero tolerance the comparison is exact, by design.
        Assert.False(transparentRed.IsSimilarTo(transparentBlue, tolerance: 0));
    }
}
