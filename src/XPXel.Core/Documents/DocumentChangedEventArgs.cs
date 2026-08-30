using XPXel.Core.Primitives;

namespace XPXel.Core.Documents;

/// <summary>
/// The category of a change reported by <see cref="PixelDocument.Changed"/>.
/// </summary>
public enum DocumentChangeKind
{
    /// <summary>
    /// Pixel data changed inside <see cref="DocumentChangedEventArgs.Region"/>. The canvas
    /// only needs to recomposite and re-upload that rectangle.
    /// </summary>
    Pixels,

    /// <summary>
    /// Layers or frames were added, removed or reordered. The whole image must be
    /// recomposited and the layer panel rebuilt.
    /// </summary>
    Structure,

    /// <summary>
    /// A layer or cel property changed — visibility, opacity, blend mode, name. The whole
    /// image must be recomposited but the panel structure is unchanged.
    /// </summary>
    Properties,

    /// <summary>
    /// The canvas dimensions changed. Every cached buffer sized to the document must be
    /// discarded and rebuilt.
    /// </summary>
    Size,
}

/// <summary>
/// Describes what changed in a document, carrying a dirty rectangle where one applies.
/// </summary>
/// <remarks>
/// <para>
/// This event is the sole channel through which the UI learns that a document needs
/// redrawing. It exists <em>instead of</em> per-pixel change notifications, which would be
/// unusably slow: a single bucket fill can touch hundreds of thousands of pixels, and one
/// <c>INotifyPropertyChanged</c> callback each would freeze the application.
/// </para>
/// <para>
/// Handlers should treat <see cref="Region"/> as a hint that is always correct but not
/// always minimal — a coarse region costs a little extra work, never a wrong picture.
/// </para>
/// </remarks>
public sealed class DocumentChangedEventArgs : EventArgs
{
    /// <summary>Initialises the arguments.</summary>
    /// <param name="kind">What kind of change occurred.</param>
    /// <param name="region">The affected region, for <see cref="DocumentChangeKind.Pixels"/>.</param>
    public DocumentChangedEventArgs(DocumentChangeKind kind, ImageRect region)
    {
        Kind = kind;
        Region = region;
    }

    /// <summary>What kind of change occurred.</summary>
    public DocumentChangeKind Kind { get; }

    /// <summary>
    /// The affected region in document coordinates. Meaningful for
    /// <see cref="DocumentChangeKind.Pixels"/>; for the other kinds it is the whole canvas.
    /// </summary>
    public ImageRect Region { get; }
}
