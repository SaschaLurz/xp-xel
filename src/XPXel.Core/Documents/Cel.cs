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
/// <b>About <see cref="Offset"/>.</b> In v0.1 every cel buffer is the full canvas size and
/// sits at (0, 0). The offset exists and is honoured by the compositor from day one so that
/// <em>trimmed</em> cels — where the buffer covers only the layer's non-empty bounding box —
/// can be introduced later purely as a storage optimisation. A large animated sprite sheet
/// with mostly-empty layers is the case that will eventually need it, and a compositor that
/// already respects offsets will need no change.
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
    /// Where the buffer's top-left corner sits in document coordinates. Always (0, 0) in
    /// v0.1; see the type remarks for why it exists.
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
