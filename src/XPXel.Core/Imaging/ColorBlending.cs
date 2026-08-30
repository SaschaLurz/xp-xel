using XPXel.Core.Primitives;

namespace XPXel.Core.Imaging;

/// <summary>
/// The pixel-level compositing mathematics shared by the layer compositor and the drawing
/// tools.
/// </summary>
/// <remarks>
/// <para>
/// Everything here operates on <em>straight</em> (non-premultiplied) alpha, matching
/// <see cref="PixelColor"/>. The formulae are those of the W3C Compositing and Blending
/// specification:
/// </para>
/// <list type="number">
///   <item><description>
///     Blend the colour channels: <c>Cs' = (1 - ab) * Cs + ab * B(Cb, Cs)</c>, where
///     <c>B</c> is the mode's function from <see cref="BlendFunctions"/>. The weighting by
///     the backdrop's alpha is what makes a blend mode fade out over transparent areas
///     instead of producing hard edges.
///   </description></item>
///   <item><description>
///     Composite source-over: <c>ao = as + ab * (1 - as)</c> and
///     <c>Co = (Cs' * as + Cb * ab * (1 - as)) / ao</c>.
///   </description></item>
/// </list>
/// <para>
/// The channel arithmetic runs in <see cref="double"/> and is rounded once on the way out.
/// Working in floating point avoids the accumulated drift that byte-domain approximations
/// cause when many layers stack up, which artists notice as colour shift in the darkest
/// tones.
/// </para>
/// </remarks>
public static class ColorBlending
{
    private const double ByteScale = 1.0 / 255.0;

    /// <summary>
    /// Composites <paramref name="source"/> over <paramref name="backdrop"/> using plain
    /// source-over — equivalent to <see cref="BlendMode.Normal"/> at full opacity.
    /// </summary>
    /// <param name="source">The colour on top.</param>
    /// <param name="backdrop">The colour underneath.</param>
    /// <returns>The composited colour.</returns>
    public static PixelColor SourceOver(PixelColor source, PixelColor backdrop)
    {
        // Fast paths matter here: in typical pixel art most of a layer is transparent and
        // most drawn pixels are fully opaque, so these two checks skip the general maths
        // for the overwhelming majority of pixels.
        if (source.A == 0)
        {
            return backdrop;
        }

        if (source.A == byte.MaxValue || backdrop.A == 0)
        {
            return source;
        }

        return CompositeCore(
            backdrop,
            source.A * ByteScale,
            source.R * ByteScale,
            source.G * ByteScale,
            source.B * ByteScale);
    }

    /// <summary>
    /// Composites <paramref name="source"/> over <paramref name="backdrop"/> with a blend
    /// mode and an opacity multiplier.
    /// </summary>
    /// <param name="source">The colour on top.</param>
    /// <param name="backdrop">The colour underneath.</param>
    /// <param name="blend">
    /// The per-channel blend function, obtained once per layer from
    /// <see cref="BlendFunctions.Resolve"/>.
    /// </param>
    /// <param name="opacity">
    /// An extra multiplier applied to the source alpha, 0..1. This is where layer and cel
    /// opacity enter the pipeline.
    /// </param>
    /// <returns>The composited colour.</returns>
    public static PixelColor Composite(
        PixelColor source,
        PixelColor backdrop,
        SeparableBlendFunction blend,
        double opacity)
    {
        ArgumentNullException.ThrowIfNull(blend);

        double sourceAlpha = source.A * ByteScale * Math.Clamp(opacity, 0.0, 1.0);
        if (sourceAlpha <= 0.0)
        {
            return backdrop;
        }

        double backdropAlpha = backdrop.A * ByteScale;

        double sourceRed = source.R * ByteScale;
        double sourceGreen = source.G * ByteScale;
        double sourceBlue = source.B * ByteScale;

        double blendedRed = sourceRed;
        double blendedGreen = sourceGreen;
        double blendedBlue = sourceBlue;

        // With nothing underneath there is nothing to blend with, so the blend function is
        // skipped entirely - both faster and mathematically identical.
        if (backdropAlpha > 0.0)
        {
            double backdropRed = backdrop.R * ByteScale;
            double backdropGreen = backdrop.G * ByteScale;
            double backdropBlue = backdrop.B * ByteScale;

            blendedRed = Mix(sourceRed, blend(backdropRed, sourceRed), backdropAlpha);
            blendedGreen = Mix(sourceGreen, blend(backdropGreen, sourceGreen), backdropAlpha);
            blendedBlue = Mix(sourceBlue, blend(backdropBlue, sourceBlue), backdropAlpha);
        }

        return CompositeCore(backdrop, sourceAlpha, blendedRed, blendedGreen, blendedBlue);
    }

    /// <summary>
    /// Converts a straight-alpha colour to premultiplied BGRA bytes, the layout renderers
    /// expect. Used only at the boundary where a composited image is handed to the UI.
    /// </summary>
    /// <param name="color">The straight-alpha colour.</param>
    /// <param name="destination">A span of at least four bytes to receive B, G, R, A.</param>
    public static void WritePremultipliedBgra(PixelColor color, Span<byte> destination)
    {
        if (color.A == byte.MaxValue)
        {
            destination[0] = color.B;
            destination[1] = color.G;
            destination[2] = color.R;
            destination[3] = byte.MaxValue;
            return;
        }

        if (color.A == 0)
        {
            destination[0] = 0;
            destination[1] = 0;
            destination[2] = 0;
            destination[3] = 0;
            return;
        }

        destination[0] = Premultiply(color.B, color.A);
        destination[1] = Premultiply(color.G, color.A);
        destination[2] = Premultiply(color.R, color.A);
        destination[3] = color.A;
    }

    private static PixelColor CompositeCore(
        PixelColor backdrop,
        double sourceAlpha,
        double blendedRed,
        double blendedGreen,
        double blendedBlue)
    {
        double backdropAlpha = backdrop.A * ByteScale;
        double inverseSourceAlpha = 1.0 - sourceAlpha;
        double outputAlpha = sourceAlpha + (backdropAlpha * inverseSourceAlpha);

        if (outputAlpha <= 0.0)
        {
            return PixelColor.Transparent;
        }

        double backdropWeight = backdropAlpha * inverseSourceAlpha;

        return new PixelColor(
            ToByte((((blendedRed * sourceAlpha) + (backdrop.R * ByteScale * backdropWeight)) / outputAlpha)),
            ToByte((((blendedGreen * sourceAlpha) + (backdrop.G * ByteScale * backdropWeight)) / outputAlpha)),
            ToByte((((blendedBlue * sourceAlpha) + (backdrop.B * ByteScale * backdropWeight)) / outputAlpha)),
            ToByte(outputAlpha));
    }

    private static double Mix(double plain, double blended, double weight) =>
        ((1.0 - weight) * plain) + (weight * blended);

    private static byte ToByte(double normalised) =>
        (byte)Math.Clamp((normalised * 255.0) + 0.5, 0.0, 255.0);

    private static byte Premultiply(byte channel, byte alpha) =>
        (byte)(((channel * alpha) + 127) / 255);
}
