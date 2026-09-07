using XPXel.Core.Documents;

namespace XPXel.Core.History;

/// <summary>
/// Creates a folder and moves one or more layers into it, as one step.
/// </summary>
/// <remarks>
/// <para>
/// Changes the artist experiences as a single gesture — dragging layers onto the "new folder"
/// button — and therefore one history entry however many layers went in.
/// </para>
/// <para>
/// <b>The folder takes the top-most layer's place in the stack.</b> What was the third row down
/// is now a folder in that slot with the layers inside it, so nothing appears to jump and the
/// composited image is unchanged: a group composites its children and blends the result with its
/// own opacity and blend mode, and a fresh folder has the neutral values for both.
/// </para>
/// <para>
/// <b>The layers keep their order relative to one another.</b> They go into the folder
/// bottom-most first, which is the order they were stacked in, so grouping a character's four
/// body parts does not shuffle which of them is in front.
/// </para>
/// </remarks>
public sealed class GroupLayersCommand : IUndoableCommand
{
    private readonly PixelDocument _document;
    private readonly GroupLayer _folder;
    private readonly LayerNode[] _layers;
    private readonly GroupLayer[] _originalParents;
    private readonly int[] _originalIndexes;
    private readonly GroupLayer _folderParent;
    private readonly int _folderIndex;

    /// <summary>Initialises the command.</summary>
    /// <param name="name">The action name shown in the Edit menu.</param>
    /// <param name="document">The document to change.</param>
    /// <param name="folder">The folder to create. It must not already be in the document.</param>
    /// <param name="layers">
    /// The layers to move into it, in document order. A layer inside another one of them is
    /// dropped, because it is already going along with its ancestor and moving it separately
    /// would tear it out of the group it belongs to.
    /// </param>
    /// <exception cref="ArgumentException">No layer given, or none of them is in the document.</exception>
    public GroupLayersCommand(
        string name,
        PixelDocument document,
        GroupLayer folder,
        IEnumerable<LayerNode> layers)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(layers);

        List<LayerNode> subjects = [];

        foreach (LayerNode layer in layers)
        {
            if (layer.Parent is not null && !subjects.Contains(layer))
            {
                subjects.Add(layer);
            }
        }

        subjects.RemoveAll(layer => subjects.Exists(other => other != layer && IsInside(layer, other)));

        if (subjects.Count == 0)
        {
            throw new ArgumentException("No layer given belongs to a document.", nameof(layers));
        }

        // Document order, so the folder ends up holding them stacked as they were.
        subjects.Sort((a, b) => CompareDepthFirst(document, a, b));

        Name = name;
        _document = document;
        _folder = folder;
        _layers = [.. subjects];
        _originalParents = [.. subjects.Select(layer => layer.Parent!)];
        _originalIndexes = [.. subjects.Select(layer => layer.Parent!.IndexOf(layer))];

        // The folder goes where the top-most of them was, which is the row the artist was
        // looking at when they asked for it.
        LayerNode anchor = _layers[^1];
        _folderParent = anchor.Parent!;
        _folderIndex = _folderParent.IndexOf(anchor);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>The folder this command creates.</summary>
    public GroupLayer Folder => _folder;

    /// <inheritdoc />
    public void Redo()
    {
        // The folder first, into the slot the top-most layer occupies. That pushes the layers up
        // one place, and moving them inside then takes them out again — leaving the folder
        // exactly where they were.
        _document.InsertLayer(_folder, _folderParent, _folderIndex);

        foreach (LayerNode layer in _layers)
        {
            layer.Parent?.Remove(layer);
            _folder.Add(layer);
        }

        _document.NotifyStructureChanged();
    }

    /// <inheritdoc />
    public void Undo()
    {
        // Backwards, so that each layer finds the index it left free still free: putting the
        // bottom-most one back first would shift every index above it by one.
        for (int i = _layers.Length - 1; i >= 0; i--)
        {
            _layers[i].Parent?.Remove(_layers[i]);
            _originalParents[i].Insert(
                Math.Clamp(_originalIndexes[i], 0, _originalParents[i].Children.Count),
                _layers[i]);
        }

        // The folder is empty by now, so removing it takes no cels with it — a group never owns
        // any of its own, and its children have already been moved out.
        _document.RemoveLayer(_folder);

        _document.NotifyStructureChanged();
    }

    /// <summary>Whether a layer lies anywhere inside another.</summary>
    private static bool IsInside(LayerNode layer, LayerNode possibleAncestor)
    {
        for (LayerNode? walk = layer.Parent; walk is not null; walk = walk.Parent)
        {
            if (walk == possibleAncestor)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Orders two layers the way the document stores them, bottom-most first.</summary>
    private static int CompareDepthFirst(PixelDocument document, LayerNode a, LayerNode b)
    {
        int index = 0;
        int first = -1;
        int second = -1;

        foreach (LayerNode node in document.EnumerateLayers())
        {
            if (node == a)
            {
                first = index;
            }
            else if (node == b)
            {
                second = index;
            }

            index++;
        }

        return first.CompareTo(second);
    }
}
