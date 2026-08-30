using XPXel.Core.Documents;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;

namespace XPXel.Core.Rendering;

/// <summary>
/// Flattens a document's layer tree for one frame into a single <see cref="PixelBuffer"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the only place in XP-xel where layers become an image. The canvas control uses
/// it to produce what the artist sees, and the exporters use it to produce a flattened PNG,
/// so both paths are guaranteed to agree.
/// </para>
/// <para>
/// <b>Dirty rectangles.</b> Every entry point takes a region. Recompositing only the
/// rectangle an edit touched is what keeps drawing responsive: a one-pixel pencil dab costs
/// a handful of blends rather than a full-canvas pass over every layer.
/// </para>
/// <para>
/// <b>Groups are isolated.</b> A <see cref="GroupLayer"/> is composited into a scratch
/// buffer first and that result is then blended into its parent with the group's own
/// opacity and blend mode. This matches Photoshop's behaviour, where fading a folder fades
/// the folder as a single image rather than fading each child in turn.
/// </para>
/// <para>
/// <b>Threading.</b> An instance keeps reusable scratch buffers and is therefore
/// <em>not</em> thread-safe. Give each consumer its own compositor; they are cheap.
/// </para>
/// </remarks>
public sealed class LayerCompositor
{
    private readonly List<PixelBuffer> _scratchByDepth = [];

    /// <summary>Flattens the whole canvas for the given frame.</summary>
    /// <param name="document">The document to composite.</param>
    /// <param name="frame">The frame to composite.</param>
    /// <param name="target">A buffer the same size as the document, which is overwritten.</param>
    public void Compose(PixelDocument document, Frame frame, PixelBuffer target)
    {
        ArgumentNullException.ThrowIfNull(document);
        Compose(document, frame, target, document.Bounds);
    }

    /// <summary>Flattens part of the canvas for the given frame.</summary>
    /// <param name="document">The document to composite.</param>
    /// <param name="frame">The frame to composite.</param>
    /// <param name="target">A buffer the same size as the document.</param>
    /// <param name="region">
    /// The region to recompute, in document coordinates. Pixels outside it are left
    /// untouched, so callers may reuse a previously composited buffer.
    /// </param>
    /// <exception cref="ArgumentException">The target buffer is not document-sized.</exception>
    public void Compose(PixelDocument document, Frame frame, PixelBuffer target, ImageRect region)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(target);

        if (target.Size != document.Size)
        {
            throw new ArgumentException(
                $"The target buffer is {target.Size} but the document is {document.Size}.",
                nameof(target));
        }

        ImageRect area = region.Intersect(document.Bounds);
        if (area.IsEmpty)
        {
            return;
        }

        // Start from transparent: layers composite onto whatever is already there, so stale
        // content from a previous pass would show through where nothing is drawn.
        target.FillRect(area, PixelColor.Transparent);
        ComposeGroup(document.Root, document, frame, target, area, depth: 0);
    }

    /// <summary>
    /// Flattens the whole canvas into a newly allocated buffer. Convenience for exporters,
    /// which have no buffer to reuse.
    /// </summary>
    /// <param name="document">The document to composite.</param>
    /// <param name="frame">The frame to composite.</param>
    /// <returns>A new buffer containing the flattened image.</returns>
    public PixelBuffer ComposeToNewBuffer(PixelDocument document, Frame frame)
    {
        ArgumentNullException.ThrowIfNull(document);

        PixelBuffer buffer = new(document.Size);
        Compose(document, frame, buffer);
        return buffer;
    }

    private void ComposeGroup(
        GroupLayer group,
        PixelDocument document,
        Frame frame,
        PixelBuffer target,
        ImageRect region,
        int depth)
    {
        // Children are stored bottom-most first, so a forward walk is already back-to-front.
        foreach (LayerNode child in group.Children)
        {
            if (!child.IsVisible || child.Opacity <= 0.0)
            {
                continue;
            }

            switch (child)
            {
                case RasterLayer raster:
                {
                    Cel? cel = document.Cels.Find(raster.Id, frame.Id);
                    if (cel is not null)
                    {
                        BlendInto(
                            cel.Pixels,
                            cel.Offset,
                            target,
                            region,
                            raster.BlendMode,
                            raster.Opacity * cel.Opacity);
                    }

                    break;
                }

                case GroupLayer nested:
                {
                    PixelBuffer scratch = RentScratch(depth, target.Size);
                    scratch.FillRect(region, PixelColor.Transparent);
                    ComposeGroup(nested, document, frame, scratch, region, depth + 1);
                    BlendInto(scratch, ImagePoint.Origin, target, region, nested.BlendMode, nested.Opacity);
                    break;
                }

                default:
                    // Unknown layer kinds render as nothing rather than throwing, so a
                    // project written by a newer build still opens and shows what it can.
                    break;
            }
        }
    }

    /// <summary>
    /// Blends <paramref name="source"/> over <paramref name="target"/> within a region.
    /// Written against row spans rather than <c>GetPixel</c>/<c>SetPixel</c> because this
    /// is the innermost loop of the whole application.
    /// </summary>
    private static void BlendInto(
        PixelBuffer source,
        ImagePoint offset,
        PixelBuffer target,
        ImageRect region,
        BlendMode mode,
        double opacity)
    {
        ImageRect area = region
            .Intersect(target.Bounds)
            .Intersect(new ImageRect(offset, source.Size));

        if (area.IsEmpty || opacity <= 0.0)
        {
            return;
        }

        // The overwhelmingly common case - an ordinary layer at full opacity - skips the
        // blend-function indirection entirely.
        bool isPlainSourceOver = mode == BlendMode.Normal && opacity >= 1.0;
        SeparableBlendFunction blend = BlendFunctions.Resolve(mode);

        const int bpp = PixelBuffer.BytesPerPixel;

        for (int y = area.Top; y < area.Bottom; y++)
        {
            ReadOnlySpan<byte> sourceRow = source.GetReadOnlyRowSpan(y - offset.Y);
            Span<byte> targetRow = target.GetRowSpan(y);

            for (int x = area.Left; x < area.Right; x++)
            {
                int si = (x - offset.X) * bpp;
                byte sourceAlpha = sourceRow[si + 3];
                if (sourceAlpha == 0)
                {
                    continue;
                }

                int di = x * bpp;
                PixelColor sourceColor = new(sourceRow[si], sourceRow[si + 1], sourceRow[si + 2], sourceAlpha);
                PixelColor targetColor = new(targetRow[di], targetRow[di + 1], targetRow[di + 2], targetRow[di + 3]);

                PixelColor result = isPlainSourceOver
                    ? ColorBlending.SourceOver(sourceColor, targetColor)
                    : ColorBlending.Composite(sourceColor, targetColor, blend, opacity);

                targetRow[di] = result.R;
                targetRow[di + 1] = result.G;
                targetRow[di + 2] = result.B;
                targetRow[di + 3] = result.A;
            }
        }
    }

    private PixelBuffer RentScratch(int depth, ImageSize size)
    {
        while (_scratchByDepth.Count <= depth)
        {
            _scratchByDepth.Add(new PixelBuffer(size));
        }

        PixelBuffer buffer = _scratchByDepth[depth];
        if (buffer.Size != size)
        {
            buffer = new PixelBuffer(size);
            _scratchByDepth[depth] = buffer;
        }

        return buffer;
    }
}
