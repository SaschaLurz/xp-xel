using XPXel.Core.Primitives;

namespace XPXel.Core.Drawing;

/// <summary>
/// Smooths the path of a freehand stroke, so that a shaky hand or a jittery pointer produces
/// a clean line.
/// </summary>
/// <remarks>
/// <para>
/// <b>This changes the path, not the mark.</b> Softness decides what a single stamp looks
/// like; stabilisation decides where the stamps go. They are independent, which is why they
/// are two separate settings and two separate pieces of code.
/// </para>
/// <para>
/// The filter is an exponential moving average: each new sample pulls the drawn point a
/// fraction of the way towards it. That was chosen over the other common approach — a
/// trailing window average, or a "rope" that the cursor drags a weight along — because it
/// needs no history buffer, has exactly one parameter, and degrades to "no smoothing at all"
/// at strength zero rather than to something subtly different.
/// </para>
/// <para>
/// <b>The trade-off is lag.</b> A stabilised point trails behind the real cursor, and at
/// full strength it trails a long way. The drawn line therefore stops short of where the
/// artist released the button, which looks like a bug. <see cref="Flush"/> exists for that:
/// stroke tools call it on release and draw the final segment to the true position.
/// </para>
/// </remarks>
public sealed class StrokeStabilizer
{
    /// <summary>
    /// The strongest smoothing allowed, as a fraction of the distance ignored per sample.
    /// Stopping short of 1.0 matters: at 1.0 the filter would never move at all and the
    /// stroke would collapse to a single dot.
    /// </summary>
    private const double MaximumSmoothing = 0.92;

    private double _x;
    private double _y;
    private bool _hasPoint;

    /// <summary>The current smoothed position, rounded to a pixel.</summary>
    public ImagePoint Current => new((int)Math.Round(_x), (int)Math.Round(_y));

    /// <summary>Starts a stroke at an exact position, with no smoothing applied to it.</summary>
    /// <param name="point">Where the artist pressed.</param>
    /// <remarks>
    /// The first point is never smoothed. An artist who clicks once to place a single dab
    /// expects it exactly where they clicked, whatever the stabiliser is set to.
    /// </remarks>
    public void Begin(ImagePoint point)
    {
        _x = point.X;
        _y = point.Y;
        _hasPoint = true;
    }

    /// <summary>Feeds in a new pointer sample and returns the position to draw at.</summary>
    /// <param name="point">The raw pointer position.</param>
    /// <param name="strength">How much to smooth, from 0 (not at all) to 1 (as much as possible).</param>
    /// <returns>The smoothed position.</returns>
    public ImagePoint Push(ImagePoint point, double strength)
    {
        if (!_hasPoint)
        {
            Begin(point);
            return point;
        }

        double follow = 1.0 - (Math.Clamp(strength, 0.0, 1.0) * MaximumSmoothing);
        _x += (point.X - _x) * follow;
        _y += (point.Y - _y) * follow;

        return Current;
    }

    /// <summary>
    /// Ends the stroke and returns the true final position, so the drawn line can catch up
    /// with the cursor.
    /// </summary>
    /// <param name="point">Where the artist released.</param>
    /// <returns>The exact release position.</returns>
    public ImagePoint Flush(ImagePoint point)
    {
        _x = point.X;
        _y = point.Y;
        return point;
    }

    /// <summary>Forgets the stroke, so the next <see cref="Push"/> starts fresh.</summary>
    public void Reset() => _hasPoint = false;
}
