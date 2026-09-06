using XPXel.Core.Drawing;
using XPXel.Core.Primitives;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for the soft edge that makes a brush a brush rather than a pencil.
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
}
