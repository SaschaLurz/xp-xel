using XPXel.Core.Documents;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Rendering;

namespace XPXel.Core.Animation;

/// <summary>
/// Flattens the current frame together with faded ghosts of its neighbours.
/// </summary>
/// <remarks>
/// <para>
/// A drop-in replacement for <see cref="LayerCompositor"/> from the canvas's point of view:
/// same "give me a buffer of what the artist should see" contract, with the neighbouring
/// frames underneath. When onion skinning is off it delegates straight through, so the
/// ordinary drawing path pays nothing for the feature existing.
/// </para>
/// <para>
/// <b>Ghosts are drawn oldest-first and the current frame last.</b> The artist is drawing on
/// the current frame, so it must never be dimmed by something laid over it — everything else
/// is context and belongs underneath.
/// </para>
/// <para>
/// <b>Threading.</b> Like the compositor it wraps, an instance keeps scratch buffers and is
/// not thread-safe. Give each canvas its own.
/// </para>
/// </remarks>
public sealed class OnionSkinCompositor
{
    private readonly LayerCompositor _compositor = new();
    private PixelBuffer? _scratch;

    /// <summary>Flattens a frame, with its neighbours ghosted beneath it.</summary>
    /// <param name="document">The document to composite.</param>
    /// <param name="frameIndex">The timeline position of the frame being edited.</param>
    /// <param name="target">A document-sized buffer, which is overwritten within the region.</param>
    /// <param name="region">The region to recompute, in document coordinates.</param>
    /// <param name="settings">
    /// How to ghost, or <see langword="null"/> to composite the current frame alone.
    /// </param>
    /// <param name="range">
    /// The frames the ghosts may be taken from, so that onion skinning inside a tagged
    /// animation does not reach into the neighbouring one, or <see langword="null"/> for the
    /// whole timeline.
    /// </param>
    public void Compose(
        PixelDocument document,
        int frameIndex,
        PixelBuffer target,
        ImageRect region,
        OnionSkinSettings? settings,
        AnimationTag? range = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(target);

        int current = Math.Clamp(frameIndex, 0, document.Frames.Count - 1);

        if (settings is not { IsEnabled: true } || document.Frames.Count <= 1)
        {
            _compositor.Compose(document, document.Frames[current], target, region);
            return;
        }

        ImageRect area = region.Intersect(document.Bounds);
        if (area.IsEmpty)
        {
            return;
        }

        target.FillRect(area, PixelColor.Transparent);

        // A ghost only makes sense inside the animation being worked on: the last frame of a
        // walk cycle has nothing to do with the first frame of an attack that happens to
        // follow it in the file.
        //
        // The range is clamped to the frames that actually exist, and that is not belt and
        // braces. A tag is positional and independently editable — an artist can set an
        // animation to end at frame 9 in a five-frame document, and undoing a frame deletion
        // reinstates a range before the frame it covers — so the tag's numbers and the
        // timeline can legitimately disagree. Only the timeline can be indexed, so it wins.
        // AnimationSequence and the project deserializer already take this view; this was the
        // one consumer that trusted the tag, and it threw an IndexOutOfRange the moment a
        // range reached past the last frame.
        int lastFrame = document.Frames.Count - 1;
        int first = Math.Clamp(range?.From ?? 0, 0, lastFrame);
        int last = Math.Clamp(range?.To ?? lastFrame, first, lastFrame);

        // Furthest ghost first, so nearer ones lie over further ones and the current frame
        // over all of them.
        for (int distance = settings.FramesBefore; distance >= 1; distance--)
        {
            DrawGhost(document, current - distance, first, last, target, area, settings, distance, isBefore: true);
        }

        for (int distance = settings.FramesAfter; distance >= 1; distance--)
        {
            DrawGhost(document, current + distance, first, last, target, area, settings, distance, isBefore: false);
        }

        PixelBuffer scratch = RentScratch(document.Size);
        _compositor.Compose(document, document.Frames[current], scratch, area);
        BlendOver(scratch, target, area, opacity: 1.0, tint: null);
    }

    private void DrawGhost(
        PixelDocument document,
        int index,
        int first,
        int last,
        PixelBuffer target,
        ImageRect area,
        OnionSkinSettings settings,
        int distance,
        bool isBefore)
    {
        if (index < first || index > last)
        {
            return;
        }

        PixelBuffer scratch = RentScratch(document.Size);
        _compositor.Compose(document, document.Frames[index], scratch, area);

        PixelColor? tint = settings.IsTinted
            ? isBefore ? settings.BeforeTint : settings.AfterTint
            : null;

        BlendOver(scratch, target, area, settings.GetOpacityAtDistance(distance), tint);
    }

    /// <summary>
    /// Composites one buffer over another at a reduced opacity, optionally replacing its
    /// colour while keeping its shape.
    /// </summary>
    /// <remarks>
    /// Tinting replaces the colour outright rather than blending towards it. A ghost's job is
    /// to show a silhouette, and a half-tinted ghost of a red character is hard to tell from
    /// a fully tinted ghost of a grey one — which defeats the point of the colour coding.
    /// </remarks>
    private static void BlendOver(
        PixelBuffer source,
        PixelBuffer target,
        ImageRect area,
        double opacity,
        PixelColor? tint)
    {
        const int bpp = PixelBuffer.BytesPerPixel;

        for (int y = area.Top; y < area.Bottom; y++)
        {
            ReadOnlySpan<byte> sourceRow = source.GetReadOnlyRowSpan(y);
            Span<byte> targetRow = target.GetRowSpan(y);

            for (int x = area.Left; x < area.Right; x++)
            {
                int i = x * bpp;

                byte alpha = sourceRow[i + 3];
                if (alpha == 0)
                {
                    continue;
                }

                byte scaled = (byte)Math.Round(alpha * opacity);
                if (scaled == 0)
                {
                    continue;
                }

                PixelColor over = tint is { } colour
                    ? colour.WithAlpha(scaled)
                    : new PixelColor(sourceRow[i], sourceRow[i + 1], sourceRow[i + 2], scaled);

                PixelColor under = new(targetRow[i], targetRow[i + 1], targetRow[i + 2], targetRow[i + 3]);
                PixelColor result = ColorBlending.SourceOver(over, under);

                targetRow[i] = result.R;
                targetRow[i + 1] = result.G;
                targetRow[i + 2] = result.B;
                targetRow[i + 3] = result.A;
            }
        }
    }

    private PixelBuffer RentScratch(ImageSize size)
    {
        if (_scratch is null || _scratch.Size != size)
        {
            _scratch = new PixelBuffer(size);
        }

        return _scratch;
    }
}
