namespace XPXel.Core.Primitives;

/// <summary>
/// One colour suggested alongside another, together with why it was suggested.
/// </summary>
/// <param name="Color">The suggested colour.</param>
/// <param name="Name">
/// What the suggestion is — "Complement", "Shadow 2" — so the artist can tell a highlight
/// from an analogous hue without having to work it out from the swatch.
/// </param>
public readonly record struct ColorSuggestion(PixelColor Color, string Name);

/// <summary>
/// Produces the colours that go with a given colour: a shading ramp through it, and its
/// relations on the colour wheel.
/// </summary>
/// <remarks>
/// <para>
/// <b>The ramp shifts hue, and that is the whole point.</b> Darkening a colour by lowering
/// its value alone gives the muddy, plastic shading that marks out beginner pixel art. Real
/// shading moves the hue as well — shadows towards the blues and purples, highlights towards
/// the yellows — because that is what light actually does and what every pixel art tutorial
/// teaches. A ramp generated here is therefore usable as-is rather than being a starting
/// point the artist has to fix.
/// </para>
/// <para>
/// The harmonies are the standard colour-wheel relations. They are offered because the
/// hardest colour in a sprite is usually the second one, and "the complement of what I
/// already picked" is a better first guess than anything a grid of swatches can offer.
/// </para>
/// <para>
/// This is in Core rather than the UI because it is arithmetic on colours with no view
/// concepts in it, and because it is worth testing without a display.
/// </para>
/// </remarks>
public static class ColorHarmony
{
    /// <summary>How far the hue rotates between adjacent steps of a ramp, in degrees.</summary>
    /// <remarks>
    /// Fifteen degrees per step is enough to read as a deliberate choice and not so much that
    /// a five-step ramp stops looking like one colour. Aseprite's shading ink and most hand
    /// built ramps land in the same neighbourhood.
    /// </remarks>
    private const double HueShiftPerStep = 15.0;

    /// <summary>The hue shadows are pulled towards: blue.</summary>
    private const double ShadowHue = 240.0;

    /// <summary>The hue highlights are pulled towards: yellow.</summary>
    private const double HighlightHue = 60.0;

    /// <summary>
    /// Builds a shading ramp running through a colour, darkest first.
    /// </summary>
    /// <param name="color">The colour the ramp passes through, which sits in the middle.</param>
    /// <param name="steps">
    /// How many colours either side of the given one. The result holds
    /// <c>(steps * 2) + 1</c> entries.
    /// </param>
    /// <returns>The ramp, from the deepest shadow to the brightest highlight.</returns>
    public static IReadOnlyList<ColorSuggestion> GetShadingRamp(PixelColor color, int steps = 3)
    {
        steps = Math.Clamp(steps, 1, 8);

        HsvColor origin = HsvColor.FromColor(color);
        List<ColorSuggestion> ramp = new((steps * 2) + 1);

        for (int i = steps; i >= 1; i--)
        {
            ramp.Add(new ColorSuggestion(Shift(origin, -i, color.A), $"Shadow {i}"));
        }

        ramp.Add(new ColorSuggestion(color, "Base"));

        for (int i = 1; i <= steps; i++)
        {
            ramp.Add(new ColorSuggestion(Shift(origin, i, color.A), $"Light {i}"));
        }

        return ramp;
    }

    /// <summary>
    /// Returns the colours that sit in a fixed relation to this one on the colour wheel.
    /// </summary>
    /// <param name="color">The colour to relate to.</param>
    /// <returns>The complement, the two analogous hues, and the two triadic hues.</returns>
    public static IReadOnlyList<ColorSuggestion> GetHarmonies(PixelColor color)
    {
        HsvColor origin = HsvColor.FromColor(color);

        // A grey has no hue to rotate, so every "harmony" of one would come back the same
        // grey. Offering five identical swatches is worse than offering none.
        if (origin.Saturation <= 0.01)
        {
            return [];
        }

        return
        [
            new ColorSuggestion(origin.WithHue(origin.Hue + 180).ToColor(color.A), "Complement"),
            new ColorSuggestion(origin.WithHue(origin.Hue - 30).ToColor(color.A), "Analogous −30°"),
            new ColorSuggestion(origin.WithHue(origin.Hue + 30).ToColor(color.A), "Analogous +30°"),
            new ColorSuggestion(origin.WithHue(origin.Hue - 120).ToColor(color.A), "Triadic −120°"),
            new ColorSuggestion(origin.WithHue(origin.Hue + 120).ToColor(color.A), "Triadic +120°"),
        ];
    }

    /// <summary>
    /// Moves a colour along the ramp by a number of steps, darker for negative and lighter
    /// for positive.
    /// </summary>
    private static PixelColor Shift(HsvColor origin, int step, byte alpha)
    {
        double distance = Math.Abs(step);

        // Value moves towards black or white, but never all the way: a ramp that bottoms out
        // at pure black and tops out at pure white wastes its two most useful ends on colours
        // the artist can already reach.
        double value = step < 0
            ? origin.Value * Math.Pow(0.72, distance)
            : origin.Value + ((1.0 - origin.Value) * (1.0 - Math.Pow(0.6, distance)));

        // Shadows gain saturation and highlights lose it, which is the other half of the same
        // observation: a lit surface reflects more of the light's own colour and less of its
        // own, while a shadowed one is coloured almost entirely by itself.
        double saturation = step < 0
            ? origin.Saturation + ((1.0 - origin.Saturation) * (1.0 - Math.Pow(0.85, distance)))
            : origin.Saturation * Math.Pow(0.82, distance);

        // Shadows rotate towards blue and highlights towards yellow, always by the shorter
        // way round the wheel and never past the target. Rotating in a fixed direction
        // instead would send a blue shadow towards cyan, which is the one direction it must
        // not go.
        double target = step < 0 ? ShadowHue : HighlightHue;
        double hue = RotateTowards(origin.Hue, target, HueShiftPerStep * distance);

        return new HsvColor(hue, saturation, value).ToColor(alpha);
    }

    /// <summary>Rotates a hue towards another by at most a given amount, the short way round.</summary>
    private static double RotateTowards(double hue, double target, double amount)
    {
        // Wrapping the difference through 180 turns "which way round the wheel" into the sign
        // of a number in −180..180, which is the shorter arc by construction.
        double delta = HsvColor.Wrap(target - hue + 180.0) - 180.0;
        double travel = Math.Min(amount, Math.Abs(delta));

        return HsvColor.Wrap(hue + (Math.Sign(delta) * travel));
    }
}
