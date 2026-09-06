using XPXel.Core.Documents;

namespace XPXel.Core.History;

/// <summary>
/// Creates a folder and moves a layer into it, as one step.
/// </summary>
/// <remarks>
/// <para>
/// Two changes the artist experiences as a single gesture — dragging a layer onto the "new
/// folder" button — and therefore one history entry rather than two.
/// </para>
/// <para>
/// <b>The folder takes the layer's place in the stack.</b> What was the third row down is now
/// a folder in that slot with the layer inside it, so nothing appears to jump and the
/// composited image is unchanged: a group composites its children and blends the result with
/// its own opacity and blend mode, and a fresh folder has the neutral values for both.
/// </para>
/// </remarks>
public sealed class GroupLayersCommand : IUndoableCommand
{
    private readonly PixelDocument _document;
    private readonly GroupLayer _folder;
    private readonly LayerNode _layer;
    private readonly GroupLayer _originalParent;
    private readonly int _originalIndex;

    /// <summary>Initialises the command.</summary>
    /// <param name="name">The action name shown in the Edit menu.</param>
    /// <param name="document">The document to change.</param>
    /// <param name="folder">The folder to create. It must not already be in the document.</param>
    /// <param name="layer">The layer to move into it.</param>
    /// <exception cref="ArgumentException">The layer is not part of the document.</exception>
    public GroupLayersCommand(string name, PixelDocument document, GroupLayer folder, LayerNode layer)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(layer);

        _originalParent = layer.Parent
            ?? throw new ArgumentException("The layer is not part of a document.", nameof(layer));

        Name = name;
        _document = document;
        _folder = folder;
        _layer = layer;
        _originalIndex = _originalParent.IndexOf(layer);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>The folder this command creates.</summary>
    public GroupLayer Folder => _folder;

    /// <inheritdoc />
    public void Redo()
    {
        // The folder first, into the slot the layer occupies. That pushes the layer up one
        // place, and moving it inside then takes it out again — leaving the folder exactly
        // where the layer was.
        _document.InsertLayer(_folder, _originalParent, _originalIndex);

        _layer.Parent?.Remove(_layer);
        _folder.Add(_layer);

        _document.NotifyStructureChanged();
    }

    /// <inheritdoc />
    public void Undo()
    {
        _layer.Parent?.Remove(_layer);
        _originalParent.Insert(Math.Clamp(_originalIndex, 0, _originalParent.Children.Count), _layer);

        // The folder is empty by now, so removing it takes no cels with it — a group never
        // owns any of its own, and its child has already been moved out.
        _document.RemoveLayer(_folder);

        _document.NotifyStructureChanged();
    }
}
