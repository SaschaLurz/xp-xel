using XPXel.Core.Drawing;
using XPXel.Core.Primitives;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for the two things that make a brush a brush rather than a pencil: a soft edge and a
/// stabilised path.
/// </summary>
public sealed class BrushDynamicsTests
{
    [Fact]
    public void ASoftStampWithNoSoftnessMatchesTheHardStamp()
    {
        HashSet<(int X, int Y)> hard = [];
        BrushStamp.Stamp(new ImagePoint(10, 10), 5, BrushShape.Circle, (x, y) => hard.Add((x, y)));

        Dictionary<(int X, int Y), double> soft = [];
        BrushStamp.StampSoft(new ImagePoint(10, 10), 5, BrushShape.Circle, 0.0, (x, y, c) => soft[(x, y)] = c);

        Assert.Equal(hard, soft.Keys.ToHashSet());
        Assert.All(soft.Values, coverage => Assert.Equal(1.0, coverage));
    }

    [Fact]
    public void ASoftStampFadesTowardsTheRim()
    {
        Dictionary<(int X, int Y), double> covered = [];
        BrushStamp.StampSoft(new ImagePoint(0, 0), 11, BrushShape.Circle, 1.0, (x, y, c) => covered[(x, y)] = c);

        // The centre is fully covered and the outermost ring barely at all.
        Assert.Equal(1.0, covered[(0, 0)], 3);
        Assert.True(covered[(4, 0)] < covered[(1, 0)]);
        Assert.All(covered.Values, coverage => Assert.InRange(coverage, 0.0, 1.0));
    }

    [Fact]
    public void ASoftSquareBrushFadesTowardsItsEdgesNotItsCorners()
    {
        Dictionary<(int X, int Y), double> covered = [];
        BrushStamp.StampSoft(new ImagePoint(0, 0), 9, BrushShape.Square, 1.0, (x, y, c) => covered[(x, y)] = c);

        // A square's distance is measured along whichever axis is furthest out, so a point on
        // the diagonal is no further from the centre than one straight out to the side.
        Assert.Equal(covered[(3, 0)], covered[(3, 3)], 3);
    }

    [Fact]
    public void ASizeOneBrushIsAlwaysASingleFullyCoveredPixel()
    {
        Dictionary<(int X, int Y), double> covered = [];
        BrushStamp.StampSoft(new ImagePoint(4, 7), 1, BrushShape.Circle, 1.0, (x, y, c) => covered[(x, y)] = c);

        KeyValuePair<(int X, int Y), double> only = Assert.Single(covered);
        Assert.Equal((4, 7), only.Key);
        Assert.Equal(1.0, only.Value);
    }

    [Fact]
    public void AtZeroStrengthTheStabiliserFollowsThePointerExactly()
    {
        StrokeStabilizer stabilizer = new();
        stabilizer.Begin(new ImagePoint(0, 0));

        Assert.Equal(new ImagePoint(10, 4), stabilizer.Push(new ImagePoint(10, 4), 0.0));
        Assert.Equal(new ImagePoint(-3, 9), stabilizer.Push(new ImagePoint(-3, 9), 0.0));
    }

    [Fact]
    public void TheStabiliserLagsBehindThePointerAndCatchesUpOverTime()
    {
        StrokeStabilizer stabilizer = new();
        stabilizer.Begin(new ImagePoint(0, 0));

        ImagePoint first = stabilizer.Push(new ImagePoint(100, 0), 1.0);
        Assert.InRange(first.X, 1, 99);

        // Repeated samples at the same place converge on it rather than stalling short.
        ImagePoint last = first;
        for (int i = 0; i < 200; i++)
        {
            last = stabilizer.Push(new ImagePoint(100, 0), 1.0);
        }

        Assert.Equal(new ImagePoint(100, 0), last);
    }

    [Fact]
    public void FlushingTheStabiliserReturnsTheExactReleasePoint()
    {
        StrokeStabilizer stabilizer = new();
        stabilizer.Begin(new ImagePoint(0, 0));
        stabilizer.Push(new ImagePoint(50, 50), 1.0);

        Assert.Equal(new ImagePoint(50, 50), stabilizer.Flush(new ImagePoint(50, 50)));
        Assert.Equal(new ImagePoint(50, 50), stabilizer.Current);
    }

    [Fact]
    public void TheFirstPointOfAStrokeIsNeverSmoothed()
    {
        StrokeStabilizer stabilizer = new();

        // An artist clicking once to place a single dab expects it exactly where they clicked,
        // whatever the stabiliser is set to.
        Assert.Equal(new ImagePoint(7, 9), stabilizer.Push(new ImagePoint(7, 9), 1.0));
    }
}
