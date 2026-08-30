using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace XPXel.Core.Primitives;

/// <summary>
/// A single 32-bit colour: eight bits each of red, green, blue and alpha, with
/// <em>straight</em> (non-premultiplied) alpha.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why straight alpha and not premultiplied.</b> Rendering stacks (Skia, and therefore
/// Avalonia) prefer premultiplied alpha because it makes compositing a cheaper operation.
/// XP-xel deliberately does not follow them in the document model, because premultiplying
/// is lossy: a pixel of 20% alpha keeps only about 51 of the 256 possible levels per
/// channel, so a save/load or an undo could silently alter the artist's colours. In a
/// pixel art editor, where a single pixel is a deliberate decision, that is unacceptable.
/// </para>
/// <para>
/// The conversion to premultiplied BGRA happens exactly once, at the very end of the
/// pipeline, when the composited image is uploaded to the on-screen bitmap in XPXel.UI.
/// Nothing in Core or Formats should ever premultiply.
/// </para>
/// <para>
/// Channel order in memory is R, G, B, A — matching PNG, which keeps the codec path free
/// of swizzling.
/// </para>
/// </remarks>
public readonly struct PixelColor : IEquatable<PixelColor>
{
    /// <summary>Initialises a colour from its four channels.</summary>
    /// <param name="r">The red channel.</param>
    /// <param name="g">The green channel.</param>
    /// <param name="b">The blue channel.</param>
    /// <param name="a">The alpha channel, where 0 is fully transparent and 255 fully opaque.</param>
    public PixelColor(byte r, byte g, byte b, byte a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    /// <summary>Initialises an fully opaque colour.</summary>
    /// <param name="r">The red channel.</param>
    /// <param name="g">The green channel.</param>
    /// <param name="b">The blue channel.</param>
    public PixelColor(byte r, byte g, byte b)
        : this(r, g, b, byte.MaxValue)
    {
    }

    /// <summary>The red channel.</summary>
    public byte R { get; }

    /// <summary>The green channel.</summary>
    public byte G { get; }

    /// <summary>The blue channel.</summary>
    public byte B { get; }

    /// <summary>The alpha channel: 0 is fully transparent, 255 fully opaque.</summary>
    public byte A { get; }

    /// <summary>Fully transparent black — the value every new pixel buffer is filled with.</summary>
    public static PixelColor Transparent => default;

    /// <summary>Opaque black.</summary>
    public static PixelColor Black => new(0, 0, 0);

    /// <summary>Opaque white.</summary>
    public static PixelColor White => new(255, 255, 255);

    /// <summary>Gets a value indicating whether the colour is fully transparent.</summary>
    public bool IsTransparent => A == 0;

    /// <summary>Gets a value indicating whether the colour is fully opaque.</summary>
    public bool IsOpaque => A == byte.MaxValue;

    /// <summary>Returns this colour with a different alpha channel.</summary>
    /// <param name="alpha">The new alpha value.</param>
    /// <returns>The adjusted colour.</returns>
    public PixelColor WithAlpha(byte alpha) => new(R, G, B, alpha);

    /// <summary>
    /// Packs the colour into a <see cref="uint"/> as 0xRRGGBBAA. This layout is used for
    /// palette storage and JSON serialization because it reads the same way as the hex
    /// string form.
    /// </summary>
    /// <returns>The packed value.</returns>
    public uint ToRgba() => ((uint)R << 24) | ((uint)G << 16) | ((uint)B << 8) | A;

    /// <summary>Unpacks a colour previously produced by <see cref="ToRgba"/>.</summary>
    /// <param name="rgba">A value laid out as 0xRRGGBBAA.</param>
    /// <returns>The unpacked colour.</returns>
    public static PixelColor FromRgba(uint rgba) => new(
        (byte)(rgba >> 24),
        (byte)(rgba >> 16),
        (byte)(rgba >> 8),
        (byte)rgba);

    /// <summary>
    /// Formats the colour as <c>#RRGGBB</c> when opaque, or <c>#RRGGBBAA</c> otherwise.
    /// This is the canonical text form used in project files and the colour input field.
    /// </summary>
    /// <returns>The hexadecimal representation, including the leading hash.</returns>
    public string ToHex() => IsOpaque
        ? string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}")
        : string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}{A:X2}");

    /// <summary>
    /// Parses a hexadecimal colour. Accepts <c>#RGB</c>, <c>#RGBA</c>, <c>#RRGGBB</c> and
    /// <c>#RRGGBBAA</c>, with or without the leading hash, in either letter case.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="color">The parsed colour when parsing succeeds.</param>
    /// <returns><see langword="true"/> if <paramref name="text"/> was a valid colour.</returns>
    public static bool TryParseHex(ReadOnlySpan<char> text, out PixelColor color)
    {
        color = default;

        text = text.Trim();
        if (text.Length > 0 && text[0] == '#')
        {
            text = text[1..];
        }

        // The 3- and 4-digit shorthands expand each digit to a full byte (F -> FF), which
        // is the same rule CSS uses.
        switch (text.Length)
        {
            case 3:
            case 4:
            {
                Span<byte> channels = stackalloc byte[4] { 0, 0, 0, 0xFF };
                for (int i = 0; i < text.Length; i++)
                {
                    if (!TryParseNibble(text[i], out byte nibble))
                    {
                        return false;
                    }

                    channels[i] = (byte)((nibble << 4) | nibble);
                }

                color = new PixelColor(channels[0], channels[1], channels[2], channels[3]);
                return true;
            }

            case 6:
            case 8:
            {
                Span<byte> channels = stackalloc byte[4] { 0, 0, 0, 0xFF };
                for (int i = 0; i < text.Length / 2; i++)
                {
                    if (!TryParseNibble(text[i * 2], out byte high) ||
                        !TryParseNibble(text[(i * 2) + 1], out byte low))
                    {
                        return false;
                    }

                    channels[i] = (byte)((high << 4) | low);
                }

                color = new PixelColor(channels[0], channels[1], channels[2], channels[3]);
                return true;
            }

            default:
                return false;
        }
    }

    private static bool TryParseNibble(char c, out byte value)
    {
        if (c is >= '0' and <= '9')
        {
            value = (byte)(c - '0');
            return true;
        }

        if (c is >= 'a' and <= 'f')
        {
            value = (byte)(c - 'a' + 10);
            return true;
        }

        if (c is >= 'A' and <= 'F')
        {
            value = (byte)(c - 'A' + 10);
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>
    /// Determines whether two colours are within <paramref name="tolerance"/> of each other
    /// on every channel. Used by the bucket fill and the magic wand: a tolerance of 0 means
    /// an exact match, which is the normal mode for pixel art.
    /// </summary>
    /// <param name="other">The colour to compare against.</param>
    /// <param name="tolerance">The maximum permitted per-channel difference, 0 to 255.</param>
    /// <returns><see langword="true"/> if every channel differs by at most the tolerance.</returns>
    public bool IsSimilarTo(PixelColor other, int tolerance)
    {
        if (tolerance <= 0)
        {
            return Equals(other);
        }

        // Fully transparent pixels are treated as identical regardless of their stored RGB,
        // because those channels are not meaningful and vary between source files.
        if (A == 0 && other.A == 0)
        {
            return true;
        }

        return Math.Abs(R - other.R) <= tolerance
            && Math.Abs(G - other.G) <= tolerance
            && Math.Abs(B - other.B) <= tolerance
            && Math.Abs(A - other.A) <= tolerance;
    }

    /// <inheritdoc />
    public bool Equals(PixelColor other) => R == other.R && G == other.G && B == other.B && A == other.A;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is PixelColor other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => (int)ToRgba();

    /// <summary>Determines whether two colours are identical.</summary>
    /// <param name="left">The first colour.</param>
    /// <param name="right">The second colour.</param>
    /// <returns><see langword="true"/> if the colours are equal.</returns>
    public static bool operator ==(PixelColor left, PixelColor right) => left.Equals(right);

    /// <summary>Determines whether two colours differ.</summary>
    /// <param name="left">The first colour.</param>
    /// <param name="right">The second colour.</param>
    /// <returns><see langword="true"/> if the colours differ.</returns>
    public static bool operator !=(PixelColor left, PixelColor right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => ToHex();
}
