using System.Collections.ObjectModel;
using System.ComponentModel;
using XPXel.Core.ComponentModel;
using XPXel.Core.Primitives;

namespace XPXel.Core.Documents;

/// <summary>
/// A pixel art document: a fixed-size canvas, a tree of layers, a list of frames, the cels
/// where the two meet, and a palette.
/// </summary>
/// <remarks>
/// <para>
/// <b>The two axes.</b> A document is best pictured as a grid. Layers run down one axis and
/// frames along the other; each intersection may hold a <see cref="Cel"/>. Read
/// <see cref="Frame"/>'s remarks for why the model is shaped this way even though v0.1
/// exposes only a single frame — it is the decision that keeps animation from becoming a
/// rewrite.
/// </para>
/// <para>
/// <b>Mutation and undo.</b> The methods here mutate the document directly and do not touch
/// the undo stack. That is deliberate: undoable operations are expressed as commands in
/// <c>XPXel.Core.History</c> which call these methods in both directions. Application code
/// should go through commands; only the commands and the deserializer should call these
/// methods straight.
/// </para>
/// <para>
/// <b>Change notification.</b> The document raises a single <see cref="Changed"/> event
/// carrying a dirty rectangle. It also forwards property changes from every layer and cel
/// it contains, so the UI needs exactly one subscription to know when to redraw.
/// </para>
/// </remarks>
public sealed class PixelDocument : ObservableModel
{
    private readonly ObservableCollection<Frame> _frames = [];

    /// <summary>Creates an empty document with one frame and no layers.</summary>
    /// <param name="size">The canvas dimensions.</param>
    /// <exception cref="ArgumentException">The size has a zero dimension.</exception>
    public PixelDocument(ImageSize size)
    {
        if (size.IsEmpty)
        {
            throw new ArgumentException("A document must have a non-zero width and height.", nameof(size));
        }

        Size = size;
        Frames = new ReadOnlyObservableCollection<Frame>(_frames);
        Root = new GroupLayer(Guid.NewGuid(), "Root");
        Cels = new CelStore();
        Palette = Palette.CreateDefault();
        Metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        _frames.Add(new Frame(Guid.NewGuid()));
    }

    /// <summary>Raised whenever anything in the document changes.</summary>
    public event EventHandler<DocumentChangedEventArgs>? Changed;

    /// <summary>The canvas dimensions. Every cel buffer is currently this size.</summary>
    public ImageSize Size { get; }

    /// <summary>The rectangle covering the whole canvas.</summary>
    public ImageRect Bounds => Size.ToRect();

    /// <summary>
    /// The invisible group containing all top-level layers. It is never shown in the layer
    /// panel and its own opacity and blend mode are ignored; it exists so that the top level
    /// and a nested folder are the same kind of thing to the compositor.
    /// </summary>
    public GroupLayer Root { get; }

    /// <summary>The timeline, in playback order. Always contains at least one frame.</summary>
    public ReadOnlyObservableCollection<Frame> Frames { get; }

    /// <summary>The pixel content, addressed by layer and frame.</summary>
    public CelStore Cels { get; }

    /// <summary>The document's colour palette.</summary>
    public Palette Palette { get; }

    /// <summary>
    /// Free-form key/value data preserved across save and load. Use it for information that
    /// does not deserve a first-class field yet — for example the game-engine export hints
    /// planned for a later version. Keys should be namespaced, such as
    /// <c>"export.unity.pixelsPerUnit"</c>.
    /// </summary>
    public IDictionary<string, string> Metadata { get; }

    /// <summary>
    /// Creates a document with one frame and one empty raster layer, which is what "File →
    /// New" produces.
    /// </summary>
    /// <param name="size">The canvas dimensions.</param>
    /// <param name="layerName">The name for the initial layer.</param>
    /// <returns>The new document.</returns>
    public static PixelDocument CreateNew(ImageSize size, string layerName = "Layer 1")
    {
        PixelDocument document = new(size);
        document.CreateRasterLayer(layerName);
        return document;
    }

    /// <summary>
    /// Reports that pixels changed inside <paramref name="region"/>. Tools and commands call
    /// this once per operation, not once per pixel.
    /// </summary>
    /// <param name="region">The affected region in document coordinates.</param>
    public void NotifyPixelsChanged(ImageRect region)
    {
        ImageRect clipped = region.Intersect(Bounds);
        if (!clipped.IsEmpty)
        {
            Changed?.Invoke(this, new DocumentChangedEventArgs(DocumentChangeKind.Pixels, clipped));
        }
    }

    /// <summary>Reports that layers or frames were added, removed or reordered.</summary>
    public void NotifyStructureChanged() =>
        Changed?.Invoke(this, new DocumentChangedEventArgs(DocumentChangeKind.Structure, Bounds));

    /// <summary>Reports that a layer or cel presentation property changed.</summary>
    public void NotifyPropertiesChanged() =>
        Changed?.Invoke(this, new DocumentChangedEventArgs(DocumentChangeKind.Properties, Bounds));

    /// <summary>Enumerates every layer in the tree, depth-first and bottom-most first.</summary>
    /// <returns>A lazy sequence of all layers, excluding <see cref="Root"/>.</returns>
    public IEnumerable<LayerNode> EnumerateLayers() => Root.EnumerateDescendants();

    /// <summary>Finds a layer anywhere in the tree by its identity.</summary>
    /// <param name="layerId">The layer's identity.</param>
    /// <returns>The layer, or <see langword="null"/> if the document does not contain it.</returns>
    public LayerNode? FindLayer(Guid layerId)
    {
        foreach (LayerNode layer in EnumerateLayers())
        {
            if (layer.Id == layerId)
            {
                return layer;
            }
        }

        return null;
    }

    /// <summary>Finds a frame by its identity.</summary>
    /// <param name="frameId">The frame's identity.</param>
    /// <returns>The frame, or <see langword="null"/> if the document does not contain it.</returns>
    public Frame? FindFrame(Guid frameId)
    {
        foreach (Frame frame in _frames)
        {
            if (frame.Id == frameId)
            {
                return frame;
            }
        }

        return null;
    }

    /// <summary>
    /// Creates an empty raster layer and inserts it into the tree.
    /// </summary>
    /// <param name="name">The display name.</param>
    /// <param name="parent">The containing group; defaults to <see cref="Root"/>.</param>
    /// <param name="index">The position within the parent, or -1 to place it on top.</param>
    /// <returns>The newly created layer.</returns>
    public RasterLayer CreateRasterLayer(string name, GroupLayer? parent = null, int index = -1)
    {
        RasterLayer layer = new(Guid.NewGuid(), name);
        InsertLayer(layer, parent ?? Root, index);
        return layer;
    }

    /// <summary>
    /// Inserts an existing layer into the tree and starts forwarding its change
    /// notifications. Used both by "add layer" and by the undo of "delete layer".
    /// </summary>
    /// <param name="layer">The layer to insert.</param>
    /// <param name="parent">The containing group.</param>
    /// <param name="index">The position within the parent, or -1 to place it on top.</param>
    public void InsertLayer(LayerNode layer, GroupLayer parent, int index = -1)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(parent);

        parent.Insert(index < 0 ? parent.Children.Count : index, layer);
        AttachLayer(layer);
        NotifyStructureChanged();
    }

    /// <summary>
    /// Removes a layer and every cel belonging to it or to its descendants.
    /// </summary>
    /// <param name="layer">The layer to remove.</param>
    /// <returns>
    /// The removed cels, which the caller must retain if the removal is to be undoable.
    /// </returns>
    public IReadOnlyList<Cel> RemoveLayer(LayerNode layer)
    {
        ArgumentNullException.ThrowIfNull(layer);

        GroupLayer? parent = layer.Parent;
        if (parent is null)
        {
            return [];
        }

        List<Cel> removed = [];
        removed.AddRange(Cels.RemoveAllForLayer(layer.Id));

        if (layer is GroupLayer group)
        {
            foreach (LayerNode descendant in group.EnumerateDescendants())
            {
                removed.AddRange(Cels.RemoveAllForLayer(descendant.Id));
            }
        }

        DetachLayer(layer);
        parent.Remove(layer);
        NotifyStructureChanged();

        return removed;
    }

    /// <summary>
    /// Creates a full copy of a layer — structure, settings and pixel content on every
    /// frame — and inserts it directly above the original.
    /// </summary>
    /// <param name="source">The layer to duplicate.</param>
    /// <param name="newName">The display name for the copy.</param>
    /// <returns>The newly created layer.</returns>
    /// <exception cref="ArgumentException">The layer does not belong to this document.</exception>
    public LayerNode DuplicateLayer(LayerNode source, string newName)
    {
        ArgumentNullException.ThrowIfNull(source);

        GroupLayer parent = source.Parent
            ?? throw new ArgumentException("The layer is not part of a document.", nameof(source));

        LayerNode copy = source.CreateStructuralCopy(Guid.NewGuid(), newName);

        foreach (Frame frame in _frames)
        {
            Cel? cel = Cels.Find(source.Id, frame.Id);
            if (cel is not null)
            {
                Cels.Set(cel.CopyTo(copy.Id, frame.Id));
            }
        }

        InsertLayer(copy, parent, parent.IndexOf(source) + 1);
        return copy;
    }

    /// <summary>
    /// Returns the cel for a layer and frame, creating an empty canvas-sized one on first
    /// use. This is what drawing tools call before their first edit.
    /// </summary>
    /// <param name="layer">The layer being drawn on.</param>
    /// <param name="frame">The frame being drawn on.</param>
    /// <returns>The cel to draw into.</returns>
    public Cel GetOrCreateCel(LayerNode layer, Frame frame)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(frame);

        return Cels.GetOrCreate(layer.Id, frame.Id, Size);
    }

    /// <summary>
    /// Replaces the entire timeline, preserving the given frames' identities.
    /// </summary>
    /// <param name="frames">The frames to install, in playback order.</param>
    /// <exception cref="ArgumentException">The sequence is empty.</exception>
    /// <remarks>
    /// This exists for deserialization. A loaded document's frames must keep the identities
    /// recorded in the file, because those identities are half of every cel's key — a
    /// freshly generated one would orphan all the artwork. Ordinary editing should use
    /// <see cref="CreateFrame"/> and <see cref="RemoveFrame"/>.
    /// </remarks>
    public void ReplaceFrames(IEnumerable<Frame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);

        List<Frame> replacement = [.. frames];
        if (replacement.Count == 0)
        {
            throw new ArgumentException("A document must have at least one frame.", nameof(frames));
        }

        foreach (Frame existing in _frames)
        {
            existing.PropertyChanged -= OnContentPropertyChanged;
        }

        _frames.Clear();

        foreach (Frame frame in replacement)
        {
            frame.PropertyChanged += OnContentPropertyChanged;
            _frames.Add(frame);
        }

        NotifyStructureChanged();
    }

    /// <summary>Appends a new empty frame to the timeline.</summary>
    /// <param name="index">The position to insert at, or -1 to append.</param>
    /// <returns>The newly created frame.</returns>
    public Frame CreateFrame(int index = -1)
    {
        Frame frame = new(Guid.NewGuid());
        _frames.Insert(index < 0 ? _frames.Count : index, frame);
        frame.PropertyChanged += OnContentPropertyChanged;
        NotifyStructureChanged();
        return frame;
    }

    /// <summary>
    /// Removes a frame and its cels. The last remaining frame cannot be removed, because a
    /// document with no frames has nowhere to store pixels.
    /// </summary>
    /// <param name="frame">The frame to remove.</param>
    /// <returns>
    /// The removed cels, which the caller must retain if the removal is to be undoable, or
    /// <see langword="null"/> if the frame was the last one and was kept.
    /// </returns>
    public IReadOnlyList<Cel>? RemoveFrame(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (_frames.Count <= 1 || !_frames.Contains(frame))
        {
            return null;
        }

        IReadOnlyList<Cel> removed = Cels.RemoveAllForFrame(frame.Id);
        frame.PropertyChanged -= OnContentPropertyChanged;
        _frames.Remove(frame);
        NotifyStructureChanged();

        return removed;
    }

    private void AttachLayer(LayerNode layer)
    {
        layer.PropertyChanged += OnContentPropertyChanged;

        if (layer is GroupLayer group)
        {
            foreach (LayerNode descendant in group.EnumerateDescendants())
            {
                descendant.PropertyChanged += OnContentPropertyChanged;
            }
        }
    }

    private void DetachLayer(LayerNode layer)
    {
        layer.PropertyChanged -= OnContentPropertyChanged;

        if (layer is GroupLayer group)
        {
            foreach (LayerNode descendant in group.EnumerateDescendants())
            {
                descendant.PropertyChanged -= OnContentPropertyChanged;
            }
        }
    }

    private void OnContentPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        NotifyPropertiesChanged();
}
