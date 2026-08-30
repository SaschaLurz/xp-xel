using XPXel.Core.Imaging;
using XPXel.Core.Primitives;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for <see cref="PixelBuffer"/>, the storage type every other part of the editor
/// builds on. The region extract/replace round-trip matters most: the undo system depends
/// on it being exact.
/// </summary>
public sealed class PixelBufferTests
{
    private static readonly PixelColor Red = new(255, 0, 0);
    private static readonly PixelColor Blue = new(0, 0, 255);

    [Fact]
    public void NewBufferIsFullyTransparent()
    {
        PixelBuffer buffer = new(4, 3);

        Assert.Equal(new ImageSize(4, 3), buffer.Size);
        for (int y = 0; y < buffer.Height; y++)
        {
            for (int x = 0; x < buffer.Width; x++)
            {
                Assert.Equal(PixelColor.Transparent, buffer.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void SetPixelReplacesRatherThanBlends()
    {
        PixelBuffer buffer = new(2, 2);
        PixelColor halfTransparentRed = new(255, 0, 0, 128);

        buffer.SetPixel(0, 0, halfTransparentRed);
        buffer.SetPixel(0, 0, halfTransparentRed);

        // Drawing the same semi-transparent colour twice must not accumulate opacity.
        Assert.Equal(halfTransparentRed, buffer.GetPixel(0, 0));
    }

    [Fact]
    public void GetPixelOrTransparentReturnsTransparentOutsideBounds()
    {
        PixelBuffer buffer = new(2, 2);
        buffer.Fill(Red);

        Assert.Equal(Red, buffer.GetPixelOrTransparent(1, 1));
        Assert.Equal(PixelColor.Transparent, buffer.GetPixelOrTransparent(-1, 0));
        Assert.Equal(PixelColor.Transparent, buffer.GetPixelOrTransparent(2, 0));
    }

    [Fact]
    public void GetPixelThrowsOutsideBounds()
    {
        PixelBuffer buffer = new(2, 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetPixel(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetPixel(0, -1));
    }

    [Fact]
    public void FillRectClipsToBufferBounds()
    {
        PixelBuffer buffer = new(3, 3);

        buffer.FillRect(new ImageRect(2, 2, 10, 10), Red);

        Assert.Equal(Red, buffer.GetPixel(2, 2));
        Assert.Equal(PixelColor.Transparent, buffer.GetPixel(1, 2));
        Assert.Equal(PixelColor.Transparent, buffer.GetPixel(2, 1));
    }

    [Fact]
    public void ExtractAndReplaceRegionRoundTripsExactly()
    {
        PixelBuffer buffer = new(5, 5);
        buffer.SetPixel(1, 1, Red);
        buffer.SetPixel(3, 3, Blue);

        ImageRect region = new(1, 1, 3, 3);
        byte[] snapshot = buffer.ExtractRegion(region);

        buffer.FillRect(region, PixelColor.White);
        Assert.Equal(PixelColor.White, buffer.GetPixel(1, 1));

        buffer.ReplaceRegion(region, snapshot);

        Assert.Equal(Red, buffer.GetPixel(1, 1));
        Assert.Equal(Blue, buffer.GetPixel(3, 3));
        Assert.Equal(PixelColor.Transparent, buffer.GetPixel(2, 2));
    }

    [Fact]
    public void ExtractRegionRejectsRegionsOutsideTheBuffer()
    {
        PixelBuffer buffer = new(4, 4);

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.ExtractRegion(new ImageRect(2, 2, 4, 4)));
    }

    [Fact]
    public void CloneProducesAnIndependentCopy()
    {
        PixelBuffer original = new(2, 2);
        original.Fill(Red);

        PixelBuffer copy = original.Clone();
        copy.SetPixel(0, 0, Blue);

        Assert.Equal(Red, original.GetPixel(0, 0));
        Assert.Equal(Blue, copy.GetPixel(0, 0));
        Assert.False(original.ContentEquals(copy));
    }

    [Fact]
    public void CopyRegionToClipsAgainstBothBuffers()
    {
        PixelBuffer source = new(4, 4);
        source.Fill(Red);
        PixelBuffer destination = new(4, 4);

        // Landing the copy near the edge must clip rather than throw or wrap.
        source.CopyRegionTo(new ImageRect(0, 0, 4, 4), destination, new ImagePoint(2, 2));

        Assert.Equal(Red, destination.GetPixel(2, 2));
        Assert.Equal(Red, destination.GetPixel(3, 3));
        Assert.Equal(PixelColor.Transparent, destination.GetPixel(1, 1));
    }

    [Fact]
    public void FromRawPixelsRejectsMismatchedLength()
    {
        Assert.Throws<ArgumentException>(() => PixelBuffer.FromRawPixels(new ImageSize(2, 2), new byte[8]));
    }
}
