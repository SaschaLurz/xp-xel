using XPXel.Core.Documents;

namespace XPXel.Core.History;

/// <summary>
/// Adds a layer to a document, and removes it again on undo.
/// </summary>
/// <remarks>
/// <para>
/// The command also carries the layer's cels. That matters for two cases: duplicating a
/// layer, where the copy arrives with artwork already in it, and undoing then redoing the
/// addition, where the layer's pixels must come back rather than the layer returning empty.
/// On undo the cels are taken back out of the document and held here; on redo they are put
/// back.
/// </para>
/// </remarks>
public sealed class AddLayerCommand : IUndoableCommand
{
    private readonly PixelDocument _document;
    private readonly LayerNode _layer;
    private readonly GroupLayer _parent;
    private readonly int _index;

    private IReadOnlyList<Cel> _cels;

    /// <summary>Initialises the command.</summary>
    /// <param name="name">The action name shown in the Edit menu.</param>
    /// <param name="document">The document to add to.</param>
    /// <param name="layer">The layer to add.</param>
    /// <param name="parent">The group to add it to.</param>
    /// <param name="index">The position within the parent, or -1 for the top.</param>
    /// <param name="cels">
    /// Any cels belonging to the layer that should be restored alongside it, used when
    /// duplicating a layer. The cels must already have been removed from the document.
    /// </param>
    public AddLayerCommand(
        string name,
        PixelDocument document,
        LayerNode layer,
        GroupLayer parent,
        int index = -1,
        IEnumerable<Cel>? cels = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(parent);

        Name = name;
        _document = document;
        _layer = layer;
        _parent = parent;
        _index = index;
        _cels = cels is null ? [] : [.. cels];
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>The layer this command adds.</summary>
    public LayerNode Layer => _layer;

    /// <inheritdoc />
    public void Redo()
    {
        _document.Cels.RestoreRange(_cels);
        _document.InsertLayer(_layer, _parent, _index);
    }

    /// <inheritdoc />
    public void Undo() => _cels = _document.RemoveLayer(_layer);
}
