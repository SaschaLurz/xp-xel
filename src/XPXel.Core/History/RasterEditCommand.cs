using XPXel.Core.Documents;
using XPXel.Core.Primitives;

namespace XPXel.Core.History;

/// <summary>
/// An undoable change to the pixels of a single cel, stored as the before and after
/// contents of the rectangle that actually changed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a rectangle rather than the whole cel.</b> Storing two full copies of a cel per
/// history entry would make memory usage proportional to canvas area times history depth —
/// a 1024x1024 document with 250 undo steps would need two gigabytes. Storing only the
/// changed rectangle makes a typical pencil dab cost a few dozen bytes, while a
/// whole-canvas operation still costs no more than two full copies.
/// </para>
/// <para>
/// Commands of this type are normally produced by <see cref="RasterEditSession"/> rather
/// than constructed directly.
/// </para>
/// </remarks>
public sealed class RasterEditCommand : IUndoableCommand
{
    private readonly PixelDocument _document;
    private readonly Cel _cel;
    private readonly ImageRect _region;
    private readonly byte[] _before;
    private readonly byte[] _after;

    /// <summary>Initialises the command.</summary>
    /// <param name="name">The action name shown in the Edit menu.</param>
    /// <param name="document">The document to notify when the command is applied.</param>
    /// <param name="cel">The cel whose pixels changed.</param>
    /// <param name="region">The changed rectangle, in the cel buffer's own coordinates.</param>
    /// <param name="before">The region's contents before the change.</param>
    /// <param name="after">The region's contents after the change.</param>
    public RasterEditCommand(
        string name,
        PixelDocument document,
        Cel cel,
        ImageRect region,
        byte[] before,
        byte[] after)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(cel);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        Name = name;
        _document = document;
        _cel = cel;
        _region = region;
        _before = before;
        _after = after;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>The changed rectangle, in the cel buffer's own coordinates.</summary>
    public ImageRect Region => _region;

    /// <inheritdoc />
    public void Redo() => Apply(_after);

    /// <inheritdoc />
    public void Undo() => Apply(_before);

    private void Apply(byte[] pixels)
    {
        _cel.Pixels.ReplaceRegion(_region, pixels);

        // The cel's buffer may sit at an offset within the canvas, so translate the dirty
        // rectangle into document space before telling the document about it.
        _document.NotifyPixelsChanged(_region.Translate(_cel.Offset.X, _cel.Offset.Y));
    }
}
