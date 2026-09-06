using XPXel.Core.Imaging;
using XPXel.Core.Primitives;

namespace XPXel.Formats.Gif;

/// <summary>
/// A GIF colour table together with the means to map a pixel onto it.
/// </summary>
/// <remarks>
/// <para>
/// GIF stores at most 256 colours per image and has no alpha channel — one palette index can
/// be declared transparent, and that is all. Pixel art usually fits comfortably: a sprite
/// drawn with a deliberate palette rarely exceeds a couple of dozen colours, and the exact
/// palette is then preserved perfectly.
/// </para>
/// <para>
/// <b>The exact palette is tried first, and that matters more than it sounds.</b> Quantising
/// artwork that already fits would shift colours the artist chose by hand for no reason at
/// all. Only when the frames genuinely hold more than 255 distinct colours — soft brush work,
/// or a gradient — does the median-cut fallback run.
/// </para>
/// <para>
/// <b>Partial alpha cannot survive.</b> Anything below the threshold becomes fully
/// transparent and anything above becomes fully opaque, because the format offers no third
/// option. It is the one real loss in a GIF export, and the reason APNG exists alongside it.
/// </para>
/// </remarks>
internal sealed class GifPalette
{
    /// <summary>The alpha at or above which a pixel is treated as opaque.</summary>
    /// <remarks>
    /// Half, so that an anti-aliased edge splits evenly rather than eroding or fattening the
    /// shape. Pixel art drawn with hard edges is unaffected either way.
    /// </remarks>
    private const byte OpacityThreshold = 128;

    /// <summary>The most colours a GIF colour table can hold.</summary>
    private const int MaximumColors = 256;

    private readonly PixelColor[] _colors;
    private readonly Dictionary<uint, int> _exact;

    private GifPalette(PixelColor[] colors, int transparentIndex)
    {
        _colors = colors;
        TransparentIndex = transparentIndex;

        _exact = new Dictionary<uint, int>(colors.Length);
        for (int i = 0; i < colors.Length; i++)
        {
            _exact.TryAdd(colors[i].ToRgba(), i);
        }
    }

    /// <summary>The colours, in table order.</summary>
    public IReadOnlyList<PixelColor> Colors => _colors;

    /// <summary>The index reserved for transparent pixels.</summary>
    public int TransparentIndex { get; }

    /// <summary>
    /// The number of bits needed to address the table, which GIF requires to be at least 1.
    /// </summary>
    public int BitsPerColor
    {
        get
        {
            int bits = 1;
            while (1 << bits < _colors.Length)
            {
                bits++;
            }

            return Math.Clamp(bits, 1, 8);
        }
    }

    /// <summary>The table size GIF actually writes, always a power of two.</summary>
    public int TableSize => 1 << BitsPerColor;

    /// <summary>Builds a palette covering every frame of an animation.</summary>
    /// <param name="frames">The frames that will share the table.</param>
    /// <returns>The palette.</returns>
    /// <remarks>
    /// One global table for the whole animation rather than one per frame. Per-frame tables
    /// would preserve more colour in a heavily quantised animation, at the cost of 768 bytes
    /// per frame and of colours visibly shifting between frames — which on flat pixel art
    /// looks like flickering.
    /// </remarks>
    public static GifPalette Build(IReadOnlyList<PixelBuffer> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);

        HashSet<uint> distinct = [];
        bool hasTransparency = false;
        bool overflowed = false;

        foreach (PixelBuffer frame in frames)
        {
            ReadOnlySpan<byte> pixels = frame.AsReadOnlySpan();

            for (int i = 0; i < pixels.Length; i += PixelBuffer.BytesPerPixel)
            {
                if (pixels[i + 3] < OpacityThreshold)
                {
                    hasTransparency = true;
                    continue;
                }

                // Alpha is dropped from the key: two pixels of the same colour at different
                // opacities become the same opaque entry, which is all GIF can represent.
                distinct.Add(new PixelColor(pixels[i], pixels[i + 1], pixels[i + 2]).ToRgba());

                if (distinct.Count > MaximumColors)
                {
                    overflowed = true;
                    break;
                }
            }

            if (overflowed)
            {
                break;
            }
        }

        // One slot is always reserved for transparency, even when the artwork is fully
        // opaque: a frame smaller than the canvas still needs somewhere to put "nothing".
        int budget = MaximumColors - 1;

        List<PixelColor> colors = [PixelColor.Transparent];

        if (!overflowed && distinct.Count <= budget)
        {
            colors.AddRange(distinct.Select(PixelColor.FromRgba));
        }
        else
        {
            colors.AddRange(MedianCut.Reduce(frames, budget, OpacityThreshold));
        }

        _ = hasTransparency;
        return new GifPalette([.. colors], transparentIndex: 0);
    }

    /// <summary>Maps a pixel onto the table.</summary>
    /// <param name="color">The pixel to map.</param>
    /// <returns>The palette index to write.</returns>
    public int IndexOf(PixelColor color)
    {
        if (color.A < OpacityThreshold)
        {
            return TransparentIndex;
        }

        uint key = new PixelColor(color.R, color.G, color.B).ToRgba();
        if (_exact.TryGetValue(key, out int exact))
        {
            return exact;
        }

        int best = TransparentIndex;
        int bestDistance = int.MaxValue;

        // Nearest in plain RGB. A perceptual distance would be a better match for photographs
        // and makes almost no difference to the flat, widely separated colours pixel art uses.
        for (int i = 0; i < _colors.Length; i++)
        {
            if (i == TransparentIndex)
            {
                continue;
            }

            int dr = _colors[i].R - color.R;
            int dg = _colors[i].G - color.G;
            int db = _colors[i].B - color.B;
            int distance = (dr * dr) + (dg * dg) + (db * db);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;

                if (distance == 0)
                {
                    break;
                }
            }
        }

        // Remembered so the next pixel of this colour — and in flat artwork there are
        // thousands — skips the search entirely.
        _exact[key] = best;
        return best;
    }
}
