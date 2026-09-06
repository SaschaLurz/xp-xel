using System.Collections.ObjectModel;
using System.ComponentModel;
using XPXel.Core.ComponentModel;
using XPXel.Core.Imaging;
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
    private readonly ObservableCollection<AnimationTag> _tags = [];

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
        Tags = new ReadOnlyObservableCollection<AnimationTag>(_tags);
        Root = new GroupLayer(Guid.NewGuid(), "Root");
        Cels = new CelStore();
        Palette = Palette.CreateDefault();
        Metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        _frames.Add(new Frame(Guid.NewGuid()));
    }

    /// <summary>Raised whenever anything in the document changes.</summary>
    public event EventHandler<DocumentChangedEventArgs>? Changed;

    /// <summary>The canvas dimensions. Every cel buffer is this size.</summary>
    /// <remarks>
    /// Settable only from inside the document, and only through <see cref="Resize"/> or
    /// <see cref="RestoreCanvas"/> — both of which bring every cel with them. A size that
    /// disagreed with the buffers would make every bounds check in the compositor wrong at
    /// once.
    /// </remarks>
    public ImageSize Size { get; private set; }

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

    /// <summary>
    /// The named animations cut out of the timeline, in the order they are listed.
    /// </summary>
    /// <remarks>
    /// Empty for a document that is a single still image, which is the majority. A document
    /// with frames but no tags is treated as one unnamed animation covering all of them, so
    /// nothing has to declare a tag before it can be played or exported.
    /// </remarks>
    public ReadOnlyObservableCollection<AnimationTag> Tags { get; }

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
    /// Creates an empty text layer and inserts it into the tree.
    /// </summary>
    /// <param name="name">The display name.</param>
    /// <param name="style">The typography to start from; copied, not referenced.</param>
    /// <param name="parent">The containing group; defaults to <see cref="Root"/>.</param>
    /// <param name="index">The position within the parent, or -1 to place it on top.</param>
    /// <returns>The newly created layer.</returns>
    /// <remarks>
    /// The layer arrives with no cel. Its pixels appear the first time
    /// <see cref="Text.TextLayerRenderer"/> runs over it, which is what every path that
    /// changes the text goes through.
    /// </remarks>
    public TextLayer CreateTextLayer(
        string name,
        Text.TextStyle? style = null,
        GroupLayer? parent = null,
        int index = -1)
    {
        TextLayer layer = new(Guid.NewGuid(), name, style);
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
        int at = index < 0 ? _frames.Count : Math.Clamp(index, 0, _frames.Count);

        _frames.Insert(at, frame);
        frame.PropertyChanged += OnContentPropertyChanged;
        ShiftTagsForInsert(at);
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

        int index = _frames.IndexOf(frame);

        IReadOnlyList<Cel> removed = Cels.RemoveAllForFrame(frame.Id);
        frame.PropertyChanged -= OnContentPropertyChanged;
        _frames.RemoveAt(index);
        ShiftTagsForRemove(index);
        NotifyStructureChanged();

        return removed;
    }

    /// <summary>
    /// Creates an empty folder and inserts it into the tree.
    /// </summary>
    /// <param name="name">The display name.</param>
    /// <param name="parent">The containing group; defaults to <see cref="Root"/>.</param>
    /// <param name="index">The position within the parent, or -1 to place it on top.</param>
    /// <returns>The newly created folder.</returns>
    /// <remarks>
    /// The compositor has handled groups since the first version — it composites one into a
    /// scratch buffer and blends the result with the group's own opacity and blend mode — so
    /// this method is the whole of what "layer folders" needed on the model side.
    /// </remarks>
    public GroupLayer CreateGroupLayer(string name, GroupLayer? parent = null, int index = -1)
    {
        GroupLayer group = new(Guid.NewGuid(), name);
        InsertLayer(group, parent ?? Root, index);
        return group;
    }

    // ---------------------------------------------------------------------------------
    // Canvas size
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Changes the canvas dimensions, moving the existing artwork by an offset.
    /// </summary>
    /// <param name="newSize">The dimensions to adopt.</param>
    /// <param name="offset">
    /// Where the old canvas's top-left corner lands in the new one. A positive offset grows
    /// the canvas around the artwork; a negative one crops into it.
    /// </param>
    /// <returns>
    /// The cels as they were, which the caller must retain if the change is to be undoable.
    /// </returns>
    /// <exception cref="ArgumentException">The new size has a zero dimension.</exception>
    /// <remarks>
    /// <para>
    /// <b>One operation covers cropping and expanding.</b> Trimming a 32×32 sprite to 16×32
    /// and growing it to 64×64 differ only in the sign of the numbers, so treating them as
    /// two features would mean two code paths that have to agree about anchoring and about
    /// what happens to the pixels that fall outside.
    /// </para>
    /// <para>
    /// <b>Cels are replaced rather than resized in place.</b> A cel's buffer reference is
    /// documented as never changing for the cel's lifetime, because tools hold on to it for
    /// the duration of a stroke. Swapping the whole cel keeps that promise: a stroke in
    /// progress writes into a buffer that is simply no longer in the document, which the
    /// session then rolls back harmlessly.
    /// </para>
    /// <para>
    /// <b>Text layers move with the artwork.</b> Their glyphs are in a cel and shift like any
    /// other pixels, but their <see cref="TextLayer.Origin"/> is in document coordinates and
    /// would otherwise stay put — so the text would jump back the next time it was
    /// re-rendered, long after the resize, which is the kind of delayed surprise that is very
    /// hard to connect to its cause.
    /// </para>
    /// </remarks>
    public IReadOnlyList<Cel> Resize(ImageSize newSize, ImagePoint offset)
    {
        if (newSize.IsEmpty)
        {
            throw new ArgumentException("A document must have a non-zero width and height.", nameof(newSize));
        }

        List<Cel> previous = [.. Cels.All];

        foreach (Cel cel in previous)
        {
            PixelBuffer resized = new(newSize);

            // Clipped by CopyRegionTo, so pixels pushed outside the new canvas are simply
            // dropped — which is what cropping means.
            cel.Pixels.CopyRegionTo(cel.Pixels.Bounds, resized, new ImagePoint(offset.X, offset.Y));

            Cels.Set(new Cel(cel.LayerId, cel.FrameId, resized)
            {
                Offset = ImagePoint.Origin,
                Opacity = cel.Opacity,
            });
        }

        foreach (LayerNode node in EnumerateLayers())
        {
            if (node is TextLayer text)
            {
                text.Origin = new ImagePoint(text.Origin.X + offset.X, text.Origin.Y + offset.Y);
            }
        }

        Size = newSize;
        Changed?.Invoke(this, new DocumentChangedEventArgs(DocumentChangeKind.Size, Bounds));

        return previous;
    }

    /// <summary>
    /// Puts the canvas back to a previous size and content, for the undo of a resize.
    /// </summary>
    /// <param name="size">The dimensions to restore.</param>
    /// <param name="cels">The cels as they were.</param>
    /// <remarks>
    /// The old cels are restored wholesale rather than the resize being run backwards,
    /// because a crop throws pixels away and no inverse offset can bring them back.
    /// </remarks>
    public void RestoreCanvas(ImageSize size, IEnumerable<Cel> cels)
    {
        ArgumentNullException.ThrowIfNull(cels);

        if (size.IsEmpty)
        {
            throw new ArgumentException("A document must have a non-zero width and height.", nameof(size));
        }

        Size = size;

        foreach (Cel cel in cels)
        {
            Cels.Set(cel);
        }

        Changed?.Invoke(this, new DocumentChangedEventArgs(DocumentChangeKind.Size, Bounds));
    }

    /// <summary>
    /// Works out where the old canvas's corner lands when a resize is anchored to one of the
    /// nine positions.
    /// </summary>
    /// <param name="oldSize">The current canvas dimensions.</param>
    /// <param name="newSize">The dimensions being adopted.</param>
    /// <param name="anchor">Which part of the artwork stays put.</param>
    /// <returns>The offset to pass to <see cref="Resize"/>.</returns>
    /// <remarks>
    /// Kept here rather than in the dialog so that the arithmetic is testable without a
    /// display, and so that a future "crop to content" command anchors identically.
    /// </remarks>
    public static ImagePoint GetAnchorOffset(ImageSize oldSize, ImageSize newSize, CanvasAnchor anchor)
    {
        int dx = newSize.Width - oldSize.Width;
        int dy = newSize.Height - oldSize.Height;

        int x = anchor switch
        {
            CanvasAnchor.TopLeft or CanvasAnchor.Left or CanvasAnchor.BottomLeft => 0,
            CanvasAnchor.TopRight or CanvasAnchor.Right or CanvasAnchor.BottomRight => dx,
            _ => dx / 2,
        };

        int y = anchor switch
        {
            CanvasAnchor.TopLeft or CanvasAnchor.Top or CanvasAnchor.TopRight => 0,
            CanvasAnchor.BottomLeft or CanvasAnchor.Bottom or CanvasAnchor.BottomRight => dy,
            _ => dy / 2,
        };

        return new ImagePoint(x, y);
    }

    /// <summary>
    /// Inserts an existing frame, keeping its identity so that cels restored alongside it
    /// stay attached. This is what the undo of "delete frame" calls.
    /// </summary>
    /// <param name="frame">The frame to insert.</param>
    /// <param name="index">The position to insert at, or -1 to append.</param>
    public void InsertFrame(Frame frame, int index = -1)
    {
        ArgumentNullException.ThrowIfNull(frame);

        int at = index < 0 ? _frames.Count : Math.Clamp(index, 0, _frames.Count);

        _frames.Insert(at, frame);
        frame.PropertyChanged += OnContentPropertyChanged;
        ShiftTagsForInsert(at);
        NotifyStructureChanged();
    }

    /// <summary>
    /// Creates a frame whose content is a copy of an existing one, on every layer.
    /// </summary>
    /// <param name="source">The frame to copy.</param>
    /// <param name="index">The position for the copy, or -1 to place it after the source.</param>
    /// <returns>The new frame.</returns>
    /// <remarks>
    /// Duplicating the previous frame and changing part of it is how hand-drawn animation is
    /// actually made, so this is the timeline's most-used command rather than a convenience.
    /// </remarks>
    public Frame DuplicateFrame(Frame source, int index = -1)
    {
        ArgumentNullException.ThrowIfNull(source);

        int sourceIndex = _frames.IndexOf(source);
        if (sourceIndex < 0)
        {
            throw new ArgumentException("The frame is not part of this document.", nameof(source));
        }

        Frame copy = new(Guid.NewGuid()) { DurationMilliseconds = source.DurationMilliseconds };

        foreach (LayerNode layer in EnumerateLayers())
        {
            if (Cels.Find(layer.Id, source.Id) is { } cel)
            {
                Cels.Set(cel.CopyTo(layer.Id, copy.Id));
            }
        }

        InsertFrame(copy, index < 0 ? sourceIndex + 1 : index);
        return copy;
    }

    /// <summary>Moves a frame to a different position in the timeline.</summary>
    /// <param name="oldIndex">The frame's current position.</param>
    /// <param name="newIndex">Where it should end up.</param>
    /// <remarks>
    /// The cels move with the frame for free, because they are keyed by the frame's identity
    /// rather than its position — which is the whole reason frames carry a
    /// <see cref="Guid"/>.
    /// <para>
    /// <b>Tag ranges deliberately do not follow.</b> A tag names a stretch of the timeline,
    /// so dragging a frame from the middle of "walk" into "idle" moves that drawing into the
    /// idle animation; it does not drag the tag boundary along with it. That is the behaviour
    /// artists expect from every editor that has both.
    /// </para>
    /// </remarks>
    public void MoveFrame(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= _frames.Count)
        {
            return;
        }

        int target = Math.Clamp(newIndex, 0, _frames.Count - 1);
        if (target == oldIndex)
        {
            return;
        }

        _frames.Move(oldIndex, target);
        NotifyStructureChanged();
    }

    /// <summary>Returns a frame's position in the timeline, or -1 if it is not part of it.</summary>
    /// <param name="frame">The frame to locate.</param>
    /// <returns>The zero-based index.</returns>
    public int IndexOfFrame(Frame frame) => frame is null ? -1 : _frames.IndexOf(frame);

    // ---------------------------------------------------------------------------------
    // Animation tags
    // ---------------------------------------------------------------------------------

    /// <summary>Creates a tag over a range of frames and adds it to the document.</summary>
    /// <param name="name">The animation's name.</param>
    /// <param name="from">The first frame index, inclusive.</param>
    /// <param name="to">The last frame index, inclusive.</param>
    /// <returns>The new tag.</returns>
    public AnimationTag CreateTag(string name, int from, int to)
    {
        AnimationTag tag = new(Guid.NewGuid(), name, from, to);
        InsertTag(tag);
        return tag;
    }

    /// <summary>Adds an existing tag, keeping its identity.</summary>
    /// <param name="tag">The tag to add.</param>
    /// <param name="index">The position in the tag list, or -1 to append.</param>
    public void InsertTag(AnimationTag tag, int index = -1)
    {
        ArgumentNullException.ThrowIfNull(tag);

        _tags.Insert(index < 0 ? _tags.Count : Math.Clamp(index, 0, _tags.Count), tag);
        tag.PropertyChanged += OnContentPropertyChanged;
        NotifyStructureChanged();
    }

    /// <summary>Removes a tag.</summary>
    /// <param name="tag">The tag to remove.</param>
    /// <returns>The position it occupied, or -1 if it was not present.</returns>
    public int RemoveTag(AnimationTag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        int index = _tags.IndexOf(tag);
        if (index < 0)
        {
            return -1;
        }

        tag.PropertyChanged -= OnContentPropertyChanged;
        _tags.RemoveAt(index);
        NotifyStructureChanged();

        return index;
    }

    /// <summary>Replaces every tag, for deserialization.</summary>
    /// <param name="tags">The tags to install.</param>
    public void ReplaceTags(IEnumerable<AnimationTag> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        foreach (AnimationTag existing in _tags)
        {
            existing.PropertyChanged -= OnContentPropertyChanged;
        }

        _tags.Clear();

        foreach (AnimationTag tag in tags)
        {
            tag.PropertyChanged += OnContentPropertyChanged;
            _tags.Add(tag);
        }

        NotifyStructureChanged();
    }

    /// <summary>
    /// Moves every tag range to account for a frame being inserted at a position.
    /// </summary>
    /// <remarks>
    /// A frame inserted before a tag pushes the whole tag along; one inserted inside it
    /// extends it, which is what adding an in-between to a walk cycle should do. A frame
    /// inserted after a tag leaves it alone.
    /// </remarks>
    private void ShiftTagsForInsert(int index)
    {
        foreach (AnimationTag tag in _tags)
        {
            if (index <= tag.From)
            {
                tag.To++;
                tag.From++;
            }
            else if (index <= tag.To)
            {
                tag.To++;
            }
        }
    }

    /// <summary>
    /// Moves every tag range to account for a frame being removed, dropping any tag left
    /// with nothing in it.
    /// </summary>
    private void ShiftTagsForRemove(int index)
    {
        for (int i = _tags.Count - 1; i >= 0; i--)
        {
            AnimationTag tag = _tags[i];

            if (index < tag.From)
            {
                tag.From--;
                tag.To--;
            }
            else if (index <= tag.To)
            {
                // Removing the tag's only frame leaves a range covering nothing. Keeping it
                // would give the artist an animation that silently exports as an empty sheet.
                if (tag.FrameCount <= 1)
                {
                    tag.PropertyChanged -= OnContentPropertyChanged;
                    _tags.RemoveAt(i);
                    continue;
                }

                tag.To--;
            }
        }
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
