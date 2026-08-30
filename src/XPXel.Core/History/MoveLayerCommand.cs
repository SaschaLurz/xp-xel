using XPXel.Core.Documents;

namespace XPXel.Core.History;

/// <summary>
/// Moves a layer to a different position, and optionally into a different group.
/// </summary>
/// <remarks>
/// This backs both the "raise/lower layer" menu commands and drag-and-drop reordering in
/// the layer panel. Because it can also change the parent group, it will keep working
/// unchanged when layer folders become creatable.
/// </remarks>
public sealed class MoveLayerCommand : IUndoableCommand
{
    private readonly PixelDocument _document;
    private readonly LayerNode _layer;
    private readonly GroupLayer _oldParent;
    private readonly int _oldIndex;
    private readonly GroupLayer _newParent;
    private readonly int _newIndex;

    /// <summary>Initialises the command.</summary>
    /// <param name="name">The action name shown in the Edit menu.</param>
    /// <param name="document">The document being modified.</param>
    /// <param name="layer">The layer to move; it must currently be in the document.</param>
    /// <param name="newParent">The group to move it into.</param>
    /// <param name="newIndex">The position within that group, where 0 is the bottom.</param>
    /// <exception cref="ArgumentException">The layer is not part of the document.</exception>
    public MoveLayerCommand(
        string name,
        PixelDocument document,
        LayerNode layer,
        GroupLayer newParent,
        int newIndex)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(newParent);

        _oldParent = layer.Parent
            ?? throw new ArgumentException("The layer is not part of a document.", nameof(layer));

        Name = name;
        _document = document;
        _layer = layer;
        _oldIndex = _oldParent.IndexOf(layer);
        _newParent = newParent;
        _newIndex = newIndex;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public void Redo() => MoveTo(_newParent, _newIndex);

    /// <inheritdoc />
    public void Undo() => MoveTo(_oldParent, _oldIndex);

    private void MoveTo(GroupLayer parent, int index)
    {
        GroupLayer? currentParent = _layer.Parent;
        if (currentParent is null)
        {
            return;
        }

        // Reordering inside the same group is a single move, which keeps the layer object
        // in place and lets the layer panel animate the change rather than rebuilding.
        if (ReferenceEquals(currentParent, parent))
        {
            int currentIndex = parent.IndexOf(_layer);
            if (currentIndex != index)
            {
                parent.Move(currentIndex, Math.Clamp(index, 0, parent.Children.Count - 1));
            }
        }
        else
        {
            currentParent.Remove(_layer);
            parent.Insert(Math.Clamp(index, 0, parent.Children.Count), _layer);
        }

        _document.NotifyStructureChanged();
    }
}
