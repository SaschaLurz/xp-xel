using XPXel.Core.Imaging;
using XPXel.Core.Primitives;

namespace XPXel.Formats.Gif;

/// <summary>
/// Reduces an image's colours to a fixed number by repeatedly splitting the colour cube.
/// </summary>
/// <remarks>
/// <para>
/// Only reached when artwork genuinely holds more than 255 distinct colours, which for pixel
/// art means soft brush work or a gradient. Anything drawn with a deliberate palette skips
/// this entirely and keeps its exact colours — see <see cref="GifPalette"/>.
/// </para>
/// <para>
/// <b>How median cut works.</b> Put every colour in one box. Repeatedly take the box with the
/// widest spread along any channel, sort its colours by that channel, and split it at the
/// median. Stop when there are as many boxes as colours wanted, then average each box. The
/// result adapts to the image: a picture that is mostly greens spends most of its palette on
/// greens.
/// </para>
/// <para>
/// Chosen over the alternatives on the grounds of predictability. A fixed colour cube is
/// faster but wastes most of its entries on colours the artwork does not contain; an octree
/// gives slightly better results for a lot more code. Median cut is a page of arithmetic with
/// no tuning constants, and its output is deterministic — the same document exports to
/// byte-identical files, which matters for anything under version control.
/// </para>
/// </remarks>
internal static class MedianCut
{
    /// <summary>
    /// Colours are counted at reduced precision so that a photographic gradient does not
    /// produce hundreds of thousands of near-identical entries to sort.
    /// </summary>
    /// <remarks>
    /// Five bits per channel is 32 levels, which is finer than the difference median cut can
    /// preserve anyway once it has averaged each box.
    /// </remarks>
    private const int PrecisionBits = 5;

    /// <summary>Chooses a representative set of colours for a set of frames.</summary>
    /// <param name="frames">The images to sample.</param>
    /// <param name="maximumColors">How many colours to produce, at most.</param>
    /// <param name="opacityThreshold">The alpha below which a pixel is ignored as transparent.</param>
    /// <returns>The chosen colours, fewer than requested if the artwork has fewer.</returns>
    public static IReadOnlyList<PixelColor> Reduce(
        IReadOnlyList<PixelBuffer> frames,
        int maximumColors,
        byte opacityThreshold)
    {
        ArgumentNullException.ThrowIfNull(frames);

        List<PixelColor> samples = Collect(frames, opacityThreshold);

        if (samples.Count == 0)
        {
            return [PixelColor.Black];
        }

        if (samples.Count <= maximumColors)
        {
            return samples;
        }

        List<List<PixelColor>> boxes = [samples];

        while (boxes.Count < maximumColors)
        {
            int widest = -1;
            int widestSpread = 0;

            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Count < 2)
                {
                    continue;
                }

                int spread = Spread(boxes[i], out _);
                if (spread > widestSpread)
                {
                    widestSpread = spread;
                    widest = i;
                }
            }

            // Every remaining box holds a single colour, so there is nothing left to split
            // and the palette is simply smaller than asked for.
            if (widest < 0)
            {
                break;
            }

            List<PixelColor> box = boxes[widest];
            _ = Spread(box, out int channel);

            box.Sort((a, b) => Channel(a, channel).CompareTo(Channel(b, channel)));

            int middle = box.Count / 2;
            boxes[widest] = box[..middle];
            boxes.Add(box[middle..]);
        }

        return [.. boxes.Where(b => b.Count > 0).Select(Average)];
    }

    /// <summary>Gathers the distinct colours of every frame at reduced precision.</summary>
    private static List<PixelColor> Collect(IReadOnlyList<PixelBuffer> frames, byte opacityThreshold)
    {
        const int shift = 8 - PrecisionBits;

        HashSet<uint> seen = [];
        List<PixelColor> samples = [];

        foreach (PixelBuffer frame in frames)
        {
            ReadOnlySpan<byte> pixels = frame.AsReadOnlySpan();

            for (int i = 0; i < pixels.Length; i += PixelBuffer.BytesPerPixel)
            {
                if (pixels[i + 3] < opacityThreshold)
                {
                    continue;
                }

                // Rounded back up after truncation so a quantised white stays white rather
                // than becoming the slightly grey 248, 248, 248.
                byte r = Expand(pixels[i], shift);
                byte g = Expand(pixels[i + 1], shift);
                byte b = Expand(pixels[i + 2], shift);

                PixelColor color = new(r, g, b);
                if (seen.Add(color.ToRgba()))
                {
                    samples.Add(color);
                }
            }
        }

        return samples;
    }

    private static byte Expand(byte value, int shift)
    {
        int reduced = value >> shift;
        int levels = (1 << PrecisionBits) - 1;
        return (byte)(reduced * 255 / levels);
    }

    /// <summary>The widest spread across any channel, and which channel that was.</summary>
    private static int Spread(List<PixelColor> box, out int channel)
    {
        int minR = 255, minG = 255, minB = 255;
        int maxR = 0, maxG = 0, maxB = 0;

        foreach (PixelColor color in box)
        {
            minR = Math.Min(minR, color.R);
            maxR = Math.Max(maxR, color.R);
            minG = Math.Min(minG, color.G);
            maxG = Math.Max(maxG, color.G);
            minB = Math.Min(minB, color.B);
            maxB = Math.Max(maxB, color.B);
        }

        int spreadR = maxR - minR;
        int spreadG = maxG - minG;
        int spreadB = maxB - minB;

        if (spreadG >= spreadR && spreadG >= spreadB)
        {
            channel = 1;
            return spreadG;
        }

        if (spreadR >= spreadB)
        {
            channel = 0;
            return spreadR;
        }

        channel = 2;
        return spreadB;
    }

    private static int Channel(PixelColor color, int channel) => channel switch
    {
        0 => color.R,
        1 => color.G,
        _ => color.B,
    };

    private static PixelColor Average(List<PixelColor> box)
    {
        long r = 0, g = 0, b = 0;

        foreach (PixelColor color in box)
        {
            r += color.R;
            g += color.G;
            b += color.B;
        }

        return new PixelColor(
            (byte)(r / box.Count),
            (byte)(g / box.Count),
            (byte)(b / box.Count));
    }
}
