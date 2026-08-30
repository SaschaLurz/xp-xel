using XPXel.Core.Documents;

namespace XPXel.Core.History;

/// <summary>
/// Removes a layer from a document, and puts it back — with all of its artwork — on undo.
/// </summary>
/// <remarks>
/// The layer object itself is kept alive by this command while it is removed, so undo
/// restores the very same instance. Anything else still referencing that layer, such as the
/// current selection in the layer panel, therefore stays valid across an undo.
/// </remarks>
public sealed class RemoveLayerCommand : IUndoableCommand
{
    private readonly PixelDocument _document;
    private readonly LayerNode _layer;
    private readonly GroupLayer _parent;
    private readonly int _index;

    private IReadOnlyList<Cel> _cels = [];

    /// <summary>Initialises the command.</summary>
    /// <param name="name">The action name shown in the Edit menu.</param>
    /// <param name="document">The document to remove from.</param>
    /// <param name="layer">The layer to remove; it must currently be in the document.</param>
    /// <exception cref="ArgumentException">The layer is not part of the document.</exception>
    public RemoveLayerCommand(string name, PixelDocument document, LayerNode layer)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layer);

        // The position has to be captured now, while the layer is still in the tree;
        // afterwards there would be nothing left to read it from.
        _parent = layer.Parent
            ?? throw new ArgumentException("The layer is not part of a document.", nameof(layer));

        Name = name;
        _document = document;
        _layer = layer;
        _index = _parent.IndexOf(layer);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>The layer this command removes.</summary>
    public LayerNode Layer => _layer;

    /// <inheritdoc />
    public void Redo() => _cels = _document.RemoveLayer(_layer);

    /// <inheritdoc />
    public void Undo()
    {
        _document.Cels.RestoreRange(_cels);
        _document.InsertLayer(_layer, _parent, _index);
        _cels = [];
    }
}
