using System.Collections.ObjectModel;

namespace XPXel.Core.Documents;

/// <summary>
/// A layer that contains other layers — a folder in the layer panel, and also the type of
/// a document's invisible root node.
/// </summary>
/// <remarks>
/// <para>
/// A group composites its children into a temporary buffer and then blends that result into
/// the parent using the group's own opacity and blend mode. That "isolated group" behaviour
/// is what artists expect from Photoshop: fading a folder fades the whole folder as one
/// image rather than fading each child independently.
/// </para>
/// <para>
/// v0.1 does not expose a command to create groups; only the document root is one. The type
/// exists and the compositor handles it so that adding a "New Group" menu item later is a
/// UI change with no model or renderer work.
/// </para>
/// <para>
/// Remember the ordering convention described on <see cref="LayerNode"/>: index 0 of
/// <see cref="Children"/> is the bottom-most layer.
/// </para>
/// </remarks>
public sealed class GroupLayer : LayerNode
{
    private readonly ObservableCollection<LayerNode> _children = [];

    /// <summary>Initialises a group layer.</summary>
    /// <param name="id">The layer's stable identity.</param>
    /// <param name="name">The display name shown in the layer panel.</param>
    public GroupLayer(Guid id, string name)
        : base(id, name)
    {
        Children = new ReadOnlyObservableCollection<LayerNode>(_children);
    }

    /// <summary>
    /// The contained layers, bottom-most first. The collection raises change notifications
    /// so the layer panel can bind to it directly, but it can only be modified through the
    /// methods on this class, which keep <see cref="LayerNode.Parent"/> consistent.
    /// </summary>
    public ReadOnlyObservableCollection<LayerNode> Children { get; }

    /// <summary>Appends a layer on top of the existing children.</summary>
    /// <param name="child">The layer to add.</param>
    public void Add(LayerNode child) => Insert(_children.Count, child);

    /// <summary>Inserts a layer at the given position.</summary>
    /// <param name="index">The position, where 0 is the bottom of the stack.</param>
    /// <param name="child">The layer to insert.</param>
    /// <exception cref="ArgumentException">The layer already belongs to a group.</exception>
    /// <exception cref="InvalidOperationException">The insertion would create a cycle.</exception>
    public void Insert(int index, LayerNode child)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (child.Parent is not null)
        {
            throw new ArgumentException("The layer is already contained in a group; remove it first.", nameof(child));
        }

        // A group cannot contain itself or one of its own ancestors. Without this check a
        // careless drag-and-drop in the layer panel would produce an infinite render loop.
        for (LayerNode? ancestor = this; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, child))
            {
                throw new InvalidOperationException("A group cannot be inserted into itself or one of its descendants.");
            }
        }

        _children.Insert(index, child);
        child.Parent = this;
    }

    /// <summary>Removes a layer from this group.</summary>
    /// <param name="child">The layer to remove.</param>
    /// <returns><see langword="true"/> if the layer was a direct child and was removed.</returns>
    public bool Remove(LayerNode child)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (!_children.Remove(child))
        {
            return false;
        }

        child.Parent = null;
        return true;
    }

    /// <summary>Moves a direct child to a different position within this group.</summary>
    /// <param name="oldIndex">The current position.</param>
    /// <param name="newIndex">The desired position.</param>
    public void Move(int oldIndex, int newIndex) => _children.Move(oldIndex, newIndex);

    /// <summary>Returns the position of a direct child, or -1 if it is not one.</summary>
    /// <param name="child">The layer to locate.</param>
    /// <returns>The zero-based index, or -1.</returns>
    public int IndexOf(LayerNode child) => _children.IndexOf(child);

    /// <summary>
    /// Enumerates every descendant depth-first, bottom-most first, excluding this group.
    /// </summary>
    /// <returns>A lazy sequence of all nested layers.</returns>
    public IEnumerable<LayerNode> EnumerateDescendants()
    {
        foreach (LayerNode child in _children)
        {
            yield return child;

            if (child is GroupLayer group)
            {
                foreach (LayerNode nested in group.EnumerateDescendants())
                {
                    yield return nested;
                }
            }
        }
    }

    /// <inheritdoc />
    public override LayerNode CreateStructuralCopy(Guid newId, string newName)
    {
        GroupLayer copy = new(newId, newName);
        CopySettingsTo(copy);
        return copy;
    }
}
