using XPXel.Core.Documents;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Selection;

namespace XPXel.Core.Editing;

/// <summary>
/// A rectangle of pixels on their way between two places: the artwork that was copied or cut,
/// and the artwork about to be pasted.
/// </summary>
/// <remarks>
/// <para>
/// <b>It carries a coverage mask as well as the pixels</b>, because a selection is not
/// necessarily a rectangle. The wand marks an area of one colour and the pixel tool marks
/// pixels one at a time; copying either and pasting it as the rectangle that bounds it would
/// bring along everything in the gaps. Coverage is per-pixel and every pixel outside the marked
/// shape is simply not pasted — which is also what makes pasting over existing artwork leave
/// the artwork alone except where the clip actually is.
/// </para>
/// <para>
/// <b>It remembers where it came from.</b> Cut something, switch to another layer, and paste:
/// what an artist means by that is "put it back, on this layer instead", and a clip that landed
/// in the middle of the canvas would have to be dragged back into place every time. A clip from
/// outside the application has no origin and is centred instead, which is the only honest answer
/// there — the other program's coordinates mean nothing here.
/// </para>
/// <para>
/// <b>Immutable, and a copy.</b> The pixels are lifted out of the layer at the moment of
/// copying, so editing or undoing afterwards cannot reach back and change what is on the
/// clipboard.
/// </para>
/// </remarks>
public sealed class PixelClip
{
    /// <summary>Marks the serialised form as XP-xel's, so foreign bytes are declined.</summary>
    private const uint Magic = 0x5850434C; // "XPCL"

    /// <summary>The serialised layout's version. A clip from a different one is declined.</summary>
    private const int Version = 1;

    /// <summary>
    /// The largest edge a serialised clip may claim.
    /// </summary>
    /// <remarks>
    /// A length read out of the clipboard is a length some other program wrote, and allocating
    /// whatever it says is how a corrupt buffer becomes an out-of-memory crash. Well past any
    /// sprite anyone will paste, and far short of dangerous.
    /// </remarks>
    private const int MaximumSide = 16384;

    private readonly PixelColor[] _pixels;
    private readonly byte[] _coverage;

    /// <summary>Initialises a clip.</summary>
    /// <param name="size">The clip's dimensions.</param>
    /// <param name="pixels">The colours, row by row.</param>
    /// <param name="coverage">How much of each pixel belongs to the clip, row by row.</param>
    /// <param name="origin">Where the clip was taken from, or <see langword="null"/> if it came from elsewhere.</param>
    public PixelClip(ImageSize size, PixelColor[] pixels, byte[] coverage, ImagePoint? origin)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(coverage);

        int count = size.Width * size.Height;

        if (size.Width <= 0 || size.Height <= 0 || pixels.Length != count || coverage.Length != count)
        {
            throw new ArgumentException("The pixels and coverage must both match the clip's size.", nameof(pixels));
        }

        Size = size;
        _pixels = pixels;
        _coverage = coverage;
        Origin = origin;
    }

    /// <summary>The clip's dimensions.</summary>
    public ImageSize Size { get; }

    /// <summary>Where on the canvas the clip was taken from, if it was taken from one.</summary>
    public ImagePoint? Origin { get; }

    /// <summary>The colours, row by row.</summary>
    public ReadOnlySpan<PixelColor> Pixels => _pixels;

    /// <summary>How much of each pixel belongs to the clip, row by row.</summary>
    public ReadOnlySpan<byte> Coverage => _coverage;

    /// <summary>Gets a value indicating whether any pixel of the clip is actually covered.</summary>
    public bool IsEmpty => Array.TrueForAll(_coverage, c => c == 0);

    /// <summary>
    /// Copies whatever is marked, on whichever layers are marked.
    /// </summary>
    /// <param name="workspace">The document to copy from.</param>
    /// <returns>
    /// The clip, or <see langword="null"/> when there is nothing to copy: no selection, an
    /// empty one, or marked layers with nothing on this frame.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The marked layers, not the flattened picture.</b> The artist said which layers they
    /// meant by marking them in the panel, and copying the composite instead would bring along
    /// a background they did not ask for and could not then separate again. One marked layer -
    /// the ordinary case - copies that layer's own pixels, alpha and all.
    /// </para>
    /// <para>
    /// <b>Several marked layers are flattened together</b>, bottom-most first, honouring
    /// visibility and opacity: what the artist sees across those layers is what lands on the
    /// clipboard. A clip is one rectangle of pixels and has nowhere to keep a stack, and the
    /// alternative - copying only the top-most - would silently throw away most of what was
    /// marked.
    /// </para>
    /// <para>
    /// <b>Blend modes are not applied.</b> A Multiply layer darkens what is beneath it, and
    /// the artwork beneath it is precisely what a copy leaves behind, so there would be
    /// nothing to multiply against. Every marked layer is composited plainly over the ones
    /// below it inside the clip.
    /// </para>
    /// </remarks>
    public static PixelClip? FromSelection(DocumentWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (workspace.Selection is not { IsEmpty: false } selection)
        {
            return null;
        }

        ImageRect source = selection.Bounds.Intersect(workspace.Document.Bounds);

        if (source.Width <= 0 || source.Height <= 0)
        {
            return null;
        }

        if (CollectSources(workspace) is not { Count: > 0 } sources)
        {
            return null;
        }

        PixelColor[] pixels = new PixelColor[source.Width * source.Height];
        byte[] coverage = new byte[pixels.Length];
        bool any = false;

        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                int index = (y * source.Width) + x;
                int sx = source.Left + x;
                int sy = source.Top + y;

                byte covered = selection.GetCoverage(sx, sy);

                if (covered == 0)
                {
                    continue;
                }

                coverage[index] = covered;
                pixels[index] = Flatten(sources, sx, sy);
                any = true;
            }
        }

        return any ? new PixelClip(source.Size, pixels, coverage, new ImagePoint(source.Left, source.Top)) : null;
    }

    /// <summary>
    /// Gathers the cels a copy should read, bottom-most first.
    /// </summary>
    /// <remarks>
    /// A single marked layer is taken whatever its state, because the artist pointed straight
    /// at it; copying a hidden layer they deliberately selected is not a surprise. Once several
    /// are marked the question changes to "what does this look like", so the hidden ones drop
    /// out - they contribute nothing to what is on screen, and flattening them in would put
    /// pixels on the clipboard that are nowhere to be seen in the picture.
    /// </remarks>
    private static List<(Cel Cel, double Opacity)> CollectSources(DocumentWorkspace workspace)
    {
        IReadOnlyList<LayerNode> leaves = workspace.GetSelectedLeaves();
        bool alone = leaves.Count == 1;

        List<(Cel Cel, double Opacity)> sources = [];

        foreach (LayerNode leaf in leaves)
        {
            if (!alone && (!leaf.IsEffectivelyVisible || leaf.Opacity <= 0.0))
            {
                continue;
            }

            if (workspace.Document.Cels.Find(leaf.Id, workspace.ActiveFrame.Id) is { } cel)
            {
                sources.Add((cel, alone ? 1.0 : leaf.Opacity));
            }
        }

        return sources;
    }

    /// <summary>Composites one document pixel down through the marked cels.</summary>
    private static PixelColor Flatten(List<(Cel Cel, double Opacity)> sources, int x, int y)
    {
        // The single-layer case is the overwhelmingly common one, and it must return the
        // layer's own pixel untouched rather than the same pixel composited over nothing:
        // source-over against a transparent backdrop is a round trip through the blend
        // arithmetic that a half-transparent pixel does not survive exactly.
        if (sources.Count == 1)
        {
            (Cel only, _) = sources[0];
            return only.Pixels.GetPixelOrTransparent(x - only.Offset.X, y - only.Offset.Y);
        }

        PixelColor result = PixelColor.Transparent;

        foreach ((Cel cel, double opacity) in sources)
        {
            PixelColor above = cel.Pixels.GetPixelOrTransparent(x - cel.Offset.X, y - cel.Offset.Y);

            if (opacity < 1.0)
            {
                above = above.WithAlpha((byte)Math.Clamp(Math.Round(above.A * opacity), 0.0, 255.0));
            }

            result = ColorBlending.SourceOver(above, result);
        }

        return result;
    }

    /// <summary>
    /// Wraps a rectangle of pixels that came from outside the application.
    /// </summary>
    /// <param name="size">The image's dimensions.</param>
    /// <param name="pixels">The colours, row by row.</param>
    /// <returns>A fully covered clip with no origin.</returns>
    /// <remarks>
    /// Every pixel is covered, including the transparent ones, because an image from elsewhere
    /// is a rectangle and nothing here knows which parts of it the artist considers to be the
    /// picture. Transparent pixels still paste as transparent, so a PNG with a cut-out
    /// background lands looking exactly as it did in the program it came from.
    /// </remarks>
    public static PixelClip FromImage(ImageSize size, PixelColor[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        byte[] coverage = new byte[pixels.Length];
        Array.Fill(coverage, byte.MaxValue);

        return new PixelClip(size, pixels, coverage, origin: null);
    }

    /// <summary>
    /// Writes the clip out as bytes, for the system clipboard to carry.
    /// </summary>
    /// <returns>The serialised clip.</returns>
    /// <remarks>
    /// <para>
    /// This travels on the clipboard <em>alongside</em> an ordinary image, under a format only
    /// XP-xel reads. Other programs take the image; XP-xel takes this, and gets back exactly
    /// what was copied — the alpha channel intact, the coverage mask intact, and the origin
    /// intact. A round trip through a plain bitmap loses all three on most platforms, which is
    /// why copying and pasting inside the editor must not go through one.
    /// </para>
    /// <para>
    /// The layout is deliberately dull: a magic number, a version, the dimensions, the origin,
    /// then the colours and the coverage. Anything it cannot read it declines rather than
    /// guesses, because the alternative is pasting rubbish into somebody's artwork.
    /// </para>
    /// </remarks>
    public byte[] ToBytes()
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(Size.Width);
        writer.Write(Size.Height);
        writer.Write(Origin is not null);
        writer.Write(Origin?.X ?? 0);
        writer.Write(Origin?.Y ?? 0);

        foreach (PixelColor pixel in _pixels)
        {
            writer.Write(pixel.ToRgba());
        }

        writer.Write(_coverage);
        writer.Flush();

        return stream.ToArray();
    }

    /// <summary>Reads a clip back out of bytes written by <see cref="ToBytes"/>.</summary>
    /// <param name="bytes">The serialised clip, or <see langword="null"/>.</param>
    /// <param name="clip">The clip that was read.</param>
    /// <returns><see langword="true"/> if the bytes were a clip this version understands.</returns>
    public static bool TryFromBytes(byte[]? bytes, out PixelClip? clip)
    {
        clip = null;

        if (bytes is null || bytes.Length < 8)
        {
            return false;
        }

        try
        {
            using MemoryStream stream = new(bytes, writable: false);
            using BinaryReader reader = new(stream);

            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
            {
                return false;
            }

            int width = reader.ReadInt32();
            int height = reader.ReadInt32();

            // A clip large enough to be a mistake is declined rather than allocated for.
            if (width is <= 0 or > MaximumSide || height is <= 0 or > MaximumSide)
            {
                return false;
            }

            bool hasOrigin = reader.ReadBoolean();
            int originX = reader.ReadInt32();
            int originY = reader.ReadInt32();

            int count = width * height;
            PixelColor[] pixels = new PixelColor[count];

            for (int i = 0; i < count; i++)
            {
                pixels[i] = PixelColor.FromRgba(reader.ReadUInt32());
            }

            byte[] coverage = reader.ReadBytes(count);

            if (coverage.Length != count)
            {
                return false;
            }

            clip = new PixelClip(
                new ImageSize(width, height),
                pixels,
                coverage,
                hasOrigin ? new ImagePoint(originX, originY) : null);

            return true;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or ArgumentException)
        {
            // Truncated, or written by something that is not this. Either way it is not a clip,
            // and the caller falls back to whatever else the clipboard is carrying.
            return false;
        }
    }

    /// <summary>
    /// Works out where on the canvas this clip should land.
    /// </summary>
    /// <param name="canvas">The document's bounds.</param>
    /// <returns>The top-left corner to paste at.</returns>
    /// <remarks>
    /// Back where it came from when it came from here and still fits; centred otherwise. A clip
    /// larger than the canvas is centred too, which puts the middle of it on screen and lets the
    /// artist drag whichever part they wanted into view — better than pinning a corner and
    /// hiding the rest off the edge.
    /// </remarks>
    public ImagePoint GetPasteOrigin(ImageRect canvas)
    {
        if (Origin is { } origin && canvas.Contains(new ImageRect(origin.X, origin.Y, Size.Width, Size.Height)))
        {
            return origin;
        }

        return new ImagePoint(
            canvas.X + ((canvas.Width - Size.Width) / 2),
            canvas.Y + ((canvas.Height - Size.Height) / 2));
    }
}
