using XPXel.Core.Documents;
using XPXel.Core.Primitives;

namespace XPXel.Core.History;

/// <summary>
/// Changes the canvas dimensions, and puts the artwork back exactly as it was on undo.
/// </summary>
/// <remarks>
/// <para>
/// <b>The old cels are stored, not recomputed.</b> A crop throws pixels away, so no inverse
/// offset can bring them back — running the resize backwards would leave the artist with a
/// canvas the right size and a border of missing artwork, which is the worst kind of undo
/// because it looks like it worked.
/// </para>
/// <para>
/// That makes this the most expensive command in the history: it holds a full copy of every
/// cel. It is also one an artist performs a handful of times per document rather than
/// hundreds of times per minute, so the trade is the right way round — unlike a brush stroke,
/// which is why <see cref="RasterEditSession"/> works so hard to store only a rectangle.
/// </para>
/// </remarks>
public sealed class ResizeCanvasCommand : IUndoableCommand
{
    private readonly PixelDocument _document;
    private readonly ImageSize _oldSize;
    private readonly ImageSize _newSize;
    private readonly ImagePoint _offset;

    private IReadOnlyList<Cel> _previousCels = [];

    /// <summary>Initialises the command.</summary>
    /// <param name="name">The action name shown in the Edit menu.</param>
    /// <param name="document">The document to resize.</param>
    /// <param name="newSize">The dimensions to adopt.</param>
    /// <param name="offset">Where the old canvas's top-left corner lands in the new one.</param>
    public ResizeCanvasCommand(string name, PixelDocument document, ImageSize newSize, ImagePoint offset)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(document);

        Name = name;
        _document = document;
        _oldSize = document.Size;
        _newSize = newSize;
        _offset = offset;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>Gets a value indicating whether the command would actually change anything.</summary>
    public bool HasChanges => _newSize != _oldSize || _offset != ImagePoint.Origin;

    /// <inheritdoc />
    public void Redo() => _previousCels = _document.Resize(_newSize, _offset);

    /// <inheritdoc />
    public void Undo() => _document.RestoreCanvas(_oldSize, _previousCels);
}
