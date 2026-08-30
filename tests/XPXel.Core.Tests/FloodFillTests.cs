using XPXel.Core.Drawing;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for <see cref="FloodFill"/>. The termination test matters most: filling an area
/// with the colour it already has is the classic way a naive flood fill hangs the
/// application, and a user hits it by double-clicking the bucket.
/// </summary>
public sealed class FloodFillTests
{
    private static readonly PixelColor Red = new(255, 0, 0);
    private static readonly PixelColor Blue = new(0, 0, 255);

    [Fact]
    public void FillsOnlyTheConnectedRegion()
    {
        PixelBuffer buffer = new(5, 1);
        buffer.SetPixel(2, 0, Blue); // a wall splitting the row in two

        List<ImagePoint> filled = [];
        FloodFill.Fill(buffer, new ImagePoint(0, 0), 0, contiguous: true, null, (x, y) => filled.Add(new ImagePoint(x, y)));

        Assert.Equal(2, filled.Count);
        Assert.Contains(new ImagePoint(0, 0), filled);
        Assert.Contains(new ImagePoint(1, 0), filled);
    }

    [Fact]
    public void NonContiguousFillReachesEveryMatchingPixel()
    {
        PixelBuffer buffer = new(5, 1);
        buffer.SetPixel(2, 0, Blue);

        List<ImagePoint> filled = [];
        FloodFill.Fill(buffer, new ImagePoint(0, 0), 0, contiguous: false, null, (x, y) => filled.Add(new ImagePoint(x, y)));

        Assert.Equal(4, filled.Count);
        Assert.Contains(new ImagePoint(4, 0), filled);
    }

    [Fact]
    public void TerminatesWhenFillingWithTheColourAlreadyPresent()
    {
        PixelBuffer buffer = new(16, 16);
        buffer.Fill(Red);

        int filledCount = 0;
        FloodFill.Fill(buffer, new ImagePoint(8, 8), 0, contiguous: true, null, (x, y) =>
        {
            filledCount++;
            buffer.SetPixel(x, y, Red); // a no-op write, exactly as the real tool would do
        });

        Assert.Equal(256, filledCount);
    }

    [Fact]
    public void SpreadsAroundObstaclesInTwoDimensions()
    {
        // A vertical wall with a gap at the bottom: the fill must go around it.
        PixelBuffer buffer = new(3, 3);
        buffer.SetPixel(1, 0, Blue);
        buffer.SetPixel(1, 1, Blue);

        HashSet<ImagePoint> filled = [];
        FloodFill.Fill(buffer, new ImagePoint(0, 0), 0, contiguous: true, null, (x, y) => filled.Add(new ImagePoint(x, y)));

        Assert.Equal(7, filled.Count);
        Assert.Contains(new ImagePoint(2, 0), filled);
    }

    [Fact]
    public void ToleranceWidensWhatCountsAsAMatch()
    {
        PixelBuffer buffer = new(2, 1);
        buffer.SetPixel(0, 0, new PixelColor(100, 100, 100));
        buffer.SetPixel(1, 0, new PixelColor(110, 110, 110));

        List<ImagePoint> exact = [];
        FloodFill.Fill(buffer, new ImagePoint(0, 0), 0, contiguous: true, null, (x, y) => exact.Add(new ImagePoint(x, y)));
        Assert.Single(exact);

        List<ImagePoint> tolerant = [];
        FloodFill.Fill(buffer, new ImagePoint(0, 0), 16, contiguous: true, null, (x, y) => tolerant.Add(new ImagePoint(x, y)));
        Assert.Equal(2, tolerant.Count);
    }

    [Fact]
    public void GateStopsTheFillFromSpreading()
    {
        PixelBuffer buffer = new(5, 1);

        List<ImagePoint> filled = [];
        FloodFill.Fill(
            buffer,
            new ImagePoint(0, 0),
            0,
            contiguous: true,
            canVisit: (x, _) => x < 2,
            (x, y) => filled.Add(new ImagePoint(x, y)));

        Assert.Equal(2, filled.Count);
    }

    [Fact]
    public void StartingOutsideTheBufferDoesNothing()
    {
        PixelBuffer buffer = new(2, 2);
        int count = 0;

        FloodFill.Fill(buffer, new ImagePoint(-1, 0), 0, contiguous: true, null, (_, _) => count++);

        Assert.Equal(0, count);
    }
}
