using System.Collections.Concurrent;

namespace XPXel.Core.Imaging;

/// <summary>
/// Computes one channel of a separable blend: given the backdrop and source values of the
/// same channel, both normalised to 0..1, returns the blended value.
/// </summary>
/// <param name="backdrop">The existing value beneath, 0..1.</param>
/// <param name="source">The incoming value on top, 0..1.</param>
/// <returns>The blended value, expected to stay within 0..1.</returns>
public delegate double SeparableBlendFunction(double backdrop, double source);

/// <summary>
/// The registry mapping every <see cref="BlendMode"/> to the function that implements it.
/// </summary>
/// <remarks>
/// <para>
/// This is the extension point that lets XP-xel gain blend modes without any change to the
/// compositor. All modes shipped so far are <em>separable</em>: they operate on each colour
/// channel independently. Non-separable modes (Hue, Saturation, Colour, Luminosity) work on
/// the whole RGB triple at once and will need a second registry alongside this one when
/// they are added — deliberately left out for now rather than guessed at.
/// </para>
/// <para>
/// <b>Performance.</b> Resolve the delegate once per layer with <see cref="Resolve"/> and
/// then loop over pixels. Looking the mode up inside a per-pixel loop would dominate the
/// cost of compositing.
/// </para>
/// </remarks>
public static class BlendFunctions
{
    private static readonly ConcurrentDictionary<BlendMode, SeparableBlendFunction> Registry = new(
        new Dictionary<BlendMode, SeparableBlendFunction>
        {
            [BlendMode.Normal] = static (_, s) => s,
            [BlendMode.Multiply] = static (b, s) => b * s,
            [BlendMode.Screen] = Screen,
            [BlendMode.Overlay] = static (b, s) => HardLight(s, b),
            [BlendMode.Darken] = Math.Min,
            [BlendMode.Lighten] = Math.Max,
            [BlendMode.ColorDodge] = ColorDodge,
            [BlendMode.ColorBurn] = ColorBurn,
            [BlendMode.HardLight] = HardLight,
            [BlendMode.SoftLight] = SoftLight,
            [BlendMode.Difference] = static (b, s) => Math.Abs(b - s),
            [BlendMode.Exclusion] = static (b, s) => b + s - (2.0 * b * s),
            [BlendMode.Addition] = static (b, s) => Math.Min(1.0, b + s),
            [BlendMode.Subtract] = static (b, s) => Math.Max(0.0, b - s),
        });

    /// <summary>Gets every blend mode that currently has an implementation.</summary>
    public static IReadOnlyCollection<BlendMode> RegisteredModes => (IReadOnlyCollection<BlendMode>)Registry.Keys;

    /// <summary>
    /// Returns the function implementing a mode, falling back to
    /// <see cref="BlendMode.Normal"/> for a mode that has no implementation.
    /// </summary>
    /// <param name="mode">The blend mode to resolve.</param>
    /// <returns>The per-channel blend function.</returns>
    /// <remarks>
    /// The fallback is intentional and must not be turned into an exception: a project file
    /// written by a newer version of XP-xel may name a mode this build does not know, and
    /// rendering it as Normal is far friendlier than refusing to open the document.
    /// </remarks>
    public static SeparableBlendFunction Resolve(BlendMode mode) =>
        Registry.TryGetValue(mode, out SeparableBlendFunction? function)
            ? function
            : Registry[BlendMode.Normal];

    /// <summary>
    /// Registers or replaces the implementation of a blend mode. Intended for adding new
    /// modes; call it during start-up, before any compositing happens.
    /// </summary>
    /// <param name="mode">The mode to implement.</param>
    /// <param name="function">The per-channel blend function.</param>
    public static void Register(BlendMode mode, SeparableBlendFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        Registry[mode] = function;
    }

    private static double Screen(double b, double s) => b + s - (b * s);

    private static double HardLight(double b, double s) =>
        s <= 0.5 ? b * 2.0 * s : Screen(b, (2.0 * s) - 1.0);

    private static double ColorDodge(double b, double s)
    {
        if (b <= 0.0)
        {
            return 0.0;
        }

        return s >= 1.0 ? 1.0 : Math.Min(1.0, b / (1.0 - s));
    }

    private static double ColorBurn(double b, double s)
    {
        if (b >= 1.0)
        {
            return 1.0;
        }

        return s <= 0.0 ? 0.0 : 1.0 - Math.Min(1.0, (1.0 - b) / s);
    }

    private static double SoftLight(double b, double s)
    {
        if (s <= 0.5)
        {
            return b - ((1.0 - (2.0 * s)) * b * (1.0 - b));
        }

        double d = b <= 0.25
            ? ((((16.0 * b) - 12.0) * b) + 4.0) * b
            : Math.Sqrt(b);

        return b + (((2.0 * s) - 1.0) * (d - b));
    }
}
