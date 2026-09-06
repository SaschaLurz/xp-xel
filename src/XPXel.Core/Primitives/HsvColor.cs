namespace XPXel.Core.Primitives;

/// <summary>
/// A colour expressed as hue, saturation and value.
/// </summary>
/// <remarks>
/// <para>
/// RGB is what a colour <em>is</em>; HSV is how an artist <em>thinks</em> about changing one.
/// "A bit darker" and "less washed out" are one axis each here and three coupled axes in RGB,
/// which is why the colour editor adjusts in this space and stores in the other.
/// </para>
/// <para>
/// Alpha is deliberately absent. Opacity is not a property of a colour's appearance and
/// carrying it through every conversion would only invite it to be lost or doubled; the
/// callers that need it keep it beside the value and hand it back to
/// <see cref="ToColor(byte)"/>.
/// </para>
/// </remarks>
/// <param name="Hue">The position on the colour wheel in degrees, 0 to 360. Red is 0.</param>
/// <param name="Saturation">How much colour there is, 0 (grey) to 1 (pure).</param>
/// <param name="Value">How bright it is, 0 (black) to 1.</param>
public readonly record struct HsvColor(double Hue, double Saturation, double Value)
{
    /// <summary>Converts an RGB colour to hue, saturation and value.</summary>
    /// <param name="color">The colour to convert. Its alpha is ignored.</param>
    /// <returns>The equivalent position in HSV space.</returns>
    /// <remarks>
    /// A grey has no meaningful hue — every hue produces the same grey — so one is reported
    /// as zero. Round-tripping a grey therefore returns the same grey but not the same hue,
    /// which is why the colour editor keeps the hue the artist last chose rather than reading
    /// it back from the colour after every edit.
    /// </remarks>
    public static HsvColor FromColor(PixelColor color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double hue = 0.0;
        if (delta > 0)
        {
            if (max == r)
            {
                hue = 60.0 * (((g - b) / delta) % 6.0);
            }
            else if (max == g)
            {
                hue = 60.0 * (((b - r) / delta) + 2.0);
            }
            else
            {
                hue = 60.0 * (((r - g) / delta) + 4.0);
            }
        }

        if (hue < 0)
        {
            hue += 360.0;
        }

        return new HsvColor(hue, max <= 0 ? 0.0 : delta / max, max);
    }

    /// <summary>Converts back to an RGB colour.</summary>
    /// <param name="alpha">The alpha to give the result.</param>
    /// <returns>The equivalent RGB colour.</returns>
    public PixelColor ToColor(byte alpha = 255)
    {
        double h = Wrap(Hue);
        double s = Math.Clamp(Saturation, 0.0, 1.0);
        double v = Math.Clamp(Value, 0.0, 1.0);

        double c = v * s;
        double x = c * (1.0 - Math.Abs(((h / 60.0) % 2.0) - 1.0));
        double m = v - c;

        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return new PixelColor(ToByte(r + m), ToByte(g + m), ToByte(b + m), alpha);
    }

    /// <summary>Returns the same colour at a different hue.</summary>
    /// <param name="hue">The new hue in degrees; wrapped into 0 to 360.</param>
    /// <returns>The adjusted colour.</returns>
    public HsvColor WithHue(double hue) => this with { Hue = Wrap(hue) };

    /// <summary>Returns the same colour at a different saturation.</summary>
    /// <param name="saturation">The new saturation, clamped to 0 to 1.</param>
    /// <returns>The adjusted colour.</returns>
    public HsvColor WithSaturation(double saturation) =>
        this with { Saturation = Math.Clamp(saturation, 0.0, 1.0) };

    /// <summary>Returns the same colour at a different value.</summary>
    /// <param name="value">The new value, clamped to 0 to 1.</param>
    /// <returns>The adjusted colour.</returns>
    public HsvColor WithValue(double value) => this with { Value = Math.Clamp(value, 0.0, 1.0) };

    /// <summary>Brings a hue in degrees into the range 0 to 360.</summary>
    /// <param name="hue">Any angle in degrees, positive or negative.</param>
    /// <returns>The equivalent angle in 0 to 360.</returns>
    public static double Wrap(double hue)
    {
        double wrapped = hue % 360.0;
        return wrapped < 0 ? wrapped + 360.0 : wrapped;
    }

    private static byte ToByte(double channel) => (byte)Math.Clamp(Math.Round(channel * 255.0), 0, 255);
}
