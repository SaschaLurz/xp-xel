using XPXel.Core.Drawing;
using XPXel.Core.Primitives;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for <see cref="ShapeRasterizer"/>.
/// </summary>
/// <remarks>
/// The ellipse tests exist mainly as a guard against the algorithm's loops running away.
/// Its first version had the terminating condition of the flat-ellipse pass inverted, which
/// did not produce a wrong shape — it froze the editor for two billion iterations the first
/// time anyone dragged one out. Anything that runs off a pointer drag is worth a test that
/// simply proves it stops.
/// </remarks>
public sealed class ShapeRasterizerTests
{
    [Fact]
    public void RectangleOutlineCoversTheBorderOnly()
    {
        HashSet<(int X, int Y)> plotted = Collect(
            plot => ShapeRasterizer.PlotRectangle(new ImageRect(2, 3, 4, 5), isFilled: false, plot));

        // A 4x5 outline is its perimeter: 2*(4+5) - 4 shared corners.
        Assert.Equal(14, plotted.Count);
        Assert.Contains((2, 3), plotted);
        Assert.Contains((5, 7), plotted);
        Assert.DoesNotContain((3, 5), plotted);
    }

    [Fact]
    public void FilledRectangleCoversEveryPixel()
    {
        HashSet<(int X, int Y)> plotted = Collect(
            plot => ShapeRasterizer.PlotRectangle(new ImageRect(0, 0, 3, 4), isFilled: true, plot));

        Assert.Equal(12, plotted.Count);
        Assert.Contains((1, 2), plotted);
    }

    [Fact]
    public void AnEmptyRectanglePlotsNothing()
    {
        HashSet<(int X, int Y)> plotted = Collect(
            plot => ShapeRasterizer.PlotRectangle(ImageRect.Empty, isFilled: true, plot));

        Assert.Empty(plotted);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 9)]
    [InlineData(9, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 4)]
    [InlineData(5, 5)]
    [InlineData(31, 7)]
    [InlineData(7, 31)]
    public void EllipseTerminatesAndStaysInsideItsBounds(int width, int height)
    {
        ImageRect bounds = new(3, 5, width, height);
        List<(int X, int Y)> plotted = [];

        // The cap is generous but finite: it fails the test rather than hanging the run if the
        // algorithm ever loses its terminating condition again.
        int budget = (width + height) * 64;

        ShapeRasterizer.PlotEllipse(bounds, isFilled: false, (x, y) =>
        {
            Assert.True(--budget > 0, "The ellipse rasteriser did not terminate.");
            plotted.Add((x, y));
        });

        Assert.NotEmpty(plotted);

        foreach ((int x, int y) in plotted)
        {
            Assert.InRange(x, bounds.Left, bounds.Right - 1);
            Assert.InRange(y, bounds.Top, bounds.Bottom - 1);
        }
    }

    [Fact]
    public void ACircleIsSymmetricAboutBothAxes()
    {
        HashSet<(int X, int Y)> plotted = Collect(
            plot => ShapeRasterizer.PlotEllipse(new ImageRect(0, 0, 9, 9), isFilled: false, plot));

        // Mirroring a circle about either axis of its bounding box must land on itself.
        foreach ((int x, int y) in plotted)
        {
            Assert.Contains((8 - x, y), plotted);
            Assert.Contains((x, 8 - y), plotted);
        }
    }

    [Fact]
    public void AFilledEllipseContainsItsOwnOutline()
    {
        HashSet<(int X, int Y)> outline = Collect(
            plot => ShapeRasterizer.PlotEllipse(new ImageRect(0, 0, 11, 7), isFilled: false, plot));

        HashSet<(int X, int Y)> filled = Collect(
            plot => ShapeRasterizer.PlotEllipse(new ImageRect(0, 0, 11, 7), isFilled: true, plot));

        Assert.True(outline.IsSubsetOf(filled));
        Assert.True(filled.Count > outline.Count);
    }

    [Fact]
    public void ConstrainToSquareKeepsTheAnchorAndTheDragDirection()
    {
        ImagePoint anchor = new(10, 10);

        Assert.Equal(new ImagePoint(20, 20), ShapeRasterizer.ConstrainToSquare(anchor, new ImagePoint(20, 13)));
        Assert.Equal(new ImagePoint(2, 2), ShapeRasterizer.ConstrainToSquare(anchor, new ImagePoint(2, 7)));
        Assert.Equal(new ImagePoint(18, 2), ShapeRasterizer.ConstrainToSquare(anchor, new ImagePoint(18, 4)));
    }

    [Fact]
    public void ConstrainToAxisSnapsToTheNearestOfTheEightDirections()
    {
        ImagePoint anchor = new(0, 0);

        // Nearly horizontal snaps flat.
        Assert.Equal(new ImagePoint(20, 0), ShapeRasterizer.ConstrainToAxis(anchor, new ImagePoint(20, 2)));

        // Nearly vertical snaps upright.
        Assert.Equal(new ImagePoint(0, 20), ShapeRasterizer.ConstrainToAxis(anchor, new ImagePoint(2, 20)));

        // Near the diagonal snaps to it, in the quadrant the drag went.
        Assert.Equal(new ImagePoint(-18, 18), ShapeRasterizer.ConstrainToAxis(anchor, new ImagePoint(-18, 16)));
    }

    private static HashSet<(int X, int Y)> Collect(Action<Action<int, int>> plotShape)
    {
        HashSet<(int X, int Y)> plotted = [];
        plotShape((x, y) => plotted.Add((x, y)));
        return plotted;
    }
}
