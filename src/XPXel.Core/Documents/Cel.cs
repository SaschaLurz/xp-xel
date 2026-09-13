using XPXel.Core.ComponentModel;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;

namespace XPXel.Core.Documents;

/// <summary>
/// The pixel content of one layer on one frame. The term is borrowed from traditional
/// animation, where a "cel" is a single sheet of celluloid, and is the same word Aseprite
/// and other sprite editors use.
/// </summary>
/// <remarks>
/// <para>
/// A cel is the intersection of the document's two axes: the layer tree (structure) and the
/// frame list (time). Drawing tools always operate on a cel, never on a layer directly.
/// </para>
/// <para>
/// <b>About <see cref="Offset"/>.</b> A cel buffer starts out the full canvas size at (0, 0),
/// and stops being either the moment its layer is moved: what slides off the edge is kept, so
/// the buffer grows and its top-left corner goes wherever it has to. The compositor has
/// honoured the offset since v0.1, which is why moving a layer needed no change there.
/// </para>
/// <para>
/// <b>The one invariant.</b> A cel's bounds always contain the whole canvas. Drawing tools
/// write in document coordinates and would otherwise have to grow the buffer mid-stroke, which
/// would invalidate every pixel index the undo session has recorded. Keeping the canvas covered
/// at all times means the only two operations that can change a cel's geometry — moving a layer
/// and resizing the canvas — are also the only two that have to think about it, and both do it
/// through <see cref="CreateMoved"/>.
/// </para>
/// </remarks>
public sealed class Cel : ObservableModel
{
    private ImagePoint _offset;
    private double _opacity = 1.0;

    /// <summary>Initialises a cel over the given pixel buffer.</summary>
    /// <param name="layerId">The identity of the owning layer.</param>
    /// <param name="frameId">The identity of the frame this content belongs to.</param>
    /// <param name="pixels">The pixel data; the cel takes ownership of the buffer.</param>
    public Cel(Guid layerId, Guid frameId, PixelBuffer pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        LayerId = layerId;
        FrameId = frameId;
        Pixels = pixels;
    }

    /// <summary>The identity of the layer this cel belongs to.</summary>
    public Guid LayerId { get; }

    /// <summary>The identity of the frame this cel belongs to.</summary>
    public Guid FrameId { get; }

    /// <summary>
    /// The cel's pixel data. The reference never changes for the lifetime of the cel, so
    /// tools may hold on to it for the duration of a stroke.
    /// </summary>
    public PixelBuffer Pixels { get; }

    /// <summary>
    /// Where the buffer's top-left corner sits in document coordinates. (0, 0) until the
    /// layer is moved or the canvas is resized around it; see the type remarks.
    /// </summary>
    public ImagePoint Offset
    {
        get => _offset;
        set => SetProperty(ref _offset, value);
    }

    /// <summary>
    /// An additional per-cel opacity multiplier, 0..1, applied on top of the layer's own
    /// opacity. Animators use it to fade a single frame without touching the layer.
    /// </summary>
    public double Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, Math.Clamp(value, 0.0, 1.0));
    }

    /// <summary>The region of document space this cel currently covers.</summary>
    public ImageRect Bounds => new(Offset, Pixels.Size);

    /// <summary>
    /// Creates a copy of this cel with its content shifted, big enough to cover a canvas and
    /// to keep whatever now hangs over its edges.
    /// </summary>
    /// <param name="delta">How far to move the content, in document coordinates.</param>
    /// <param name="canvas">The canvas the result must cover.</param>
    /// <returns>A new cel with the same identity, a new buffer and a new offset.</returns>
    /// <remarks>
    /// <para>
    /// This is the only way a cel's geometry ever changes, and it is used both by the move tool
    /// and by a canvas resize — which are the same operation seen from opposite sides, one
    /// moving the artwork under a fixed canvas and the other moving the canvas around fixed
    /// artwork.
    /// </para>
    /// <para>
    /// <b>Nothing painted is discarded, and nothing blank is kept.</b> The new buffer covers the
    /// canvas union whatever part of this one actually holds pixels, so trimming empty margins
    /// is free and trimming artwork merely hides it. That is what lets an artist crop too far
    /// and drag the frame back out again.
    /// </para>
    /// <para>
    /// A new cel rather than a resized one, because <see cref="Pixels"/> is documented as never
    /// changing for the life of a cel: a stroke in progress keeps writing into a buffer that is
    /// simply no longer in the document, and its session rolls that back harmlessly.
    /// </para>
    /// </remarks>
    public Cel CreateMoved(ImagePoint delta, ImageRect canvas)
    {
        ImagePoint origin = new(Offset.X + delta.X, Offset.Y + delta.Y);
        ImageRect occupied = Pixels.GetOccupiedBounds();

        ImageRect target = occupied.IsEmpty
            ? canvas
            : canvas.Union(occupied.Translate(origin.X, origin.Y));

        PixelBuffer moved = new(target.Size);
        Pixels.CopyRegionTo(Pixels.Bounds, moved, new ImagePoint(origin.X - target.X, origin.Y - target.Y));

        return new Cel(LayerId, FrameId, moved)
        {
            Offset = new ImagePoint(target.X, target.Y),
            Opacity = Opacity,
        };
    }

    /// <summary>
    /// Creates a copy of this cel whose buffer is large enough to hold a given rectangle,
    /// without moving anything already in it.
    /// </summary>
    /// <param name="required">The region that must be writable, in document coordinates.</param>
    /// <param name="canvas">The canvas the result must still cover.</param>
    /// <returns>
    /// A new cel with the same identity and a larger buffer, or this one when the buffer is
    /// already big enough.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is what stops a transform from cutting the artwork off at the canvas edge.
    /// <see cref="CreateMoved"/> already keeps whatever a <em>move</em> pushes over the side,
    /// because it knows in advance where the pixels are going; a transform does not, since the
    /// box can be dragged anywhere and resized on the way. So it grows the buffer at the moment
    /// it places the artwork, and everything hanging over the edge is kept exactly as a moved
    /// layer's is — invisible until the canvas grows or the artwork is dragged back.
    /// </para>
    /// <para>
    /// Returning <see langword="this"/> when nothing needs to change matters: the caller uses
    /// the identity of the result to decide whether the swap has to be recorded in the history
    /// at all.
    /// </para>
    /// </remarks>
    public Cel CreateExpanded(ImageRect required, ImageRect canvas)
    {
        ImageRect current = Bounds;

        if (required.IsEmpty || current.Contains(required))
        {
            return this;
        }

        ImageRect target = current.Union(canvas).Union(required);

        PixelBuffer grown = new(target.Size);
        Pixels.CopyRegionTo(Pixels.Bounds, grown, new ImagePoint(Offset.X - target.X, Offset.Y - target.Y));

        return new Cel(LayerId, FrameId, grown)
        {
            Offset = new ImagePoint(target.X, target.Y),
            Opacity = Opacity,
        };
    }

    /// <summary>
    /// Creates a copy of this cel's content under a different layer and frame identity.
    /// </summary>
    /// <param name="layerId">The identity of the layer the copy belongs to.</param>
    /// <param name="frameId">The identity of the frame the copy belongs to.</param>
    /// <returns>A new cel with cloned pixel data and the same offset and opacity.</returns>
    public Cel CopyTo(Guid layerId, Guid frameId) => new(layerId, frameId, Pixels.Clone())
    {
        Offset = Offset,
        Opacity = Opacity,
    };
}
