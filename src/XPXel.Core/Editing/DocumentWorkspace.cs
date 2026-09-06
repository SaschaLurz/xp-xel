using XPXel.Core.ComponentModel;
using XPXel.Core.Documents;
using XPXel.Core.History;
using XPXel.Core.Primitives;
using XPXel.Core.Selection;

namespace XPXel.Core.Editing;

/// <summary>
/// One open document together with everything the editor tracks about it: its undo history,
/// which layer and frame are active, the current selection, and where it lives on disk.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PixelDocument"/> deliberately knows nothing about "the layer the artist is
/// working on" — that is a property of the editing session, not of the artwork. Separating
/// them means a single document could be open in two views, or processed head-lessly by an
/// exporter, without carrying UI state around.
/// </para>
/// <para>
/// <b>Growth path.</b> One workspace equals one document tab. Multi-document support is
/// therefore a collection of these plus a notion of which is current; nothing about this
/// type assumes it is the only one.
/// </para>
/// </remarks>
public sealed class DocumentWorkspace : ObservableModel
{
    private LayerNode? _activeLayer;
    private Frame _activeFrame;
    private AnimationTag? _activeTag;
    private SelectionMask? _selection;
    private TextEditSession? _activeTextEdit;
    private string? _filePath;

    /// <summary>Opens a workspace over a document.</summary>
    /// <param name="document">The document being edited.</param>
    /// <param name="filePath">Where the document was loaded from, if anywhere.</param>
    public DocumentWorkspace(PixelDocument document, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        Document = document;
        History = new DocumentHistory();
        _filePath = filePath;
        _activeFrame = document.Frames[0];

        // Select the top-most layer, which is what an artist expects to be drawing on when
        // a document opens.
        _activeLayer = document.Root.Children.Count > 0
            ? document.Root.Children[^1]
            : null;

        History.Changed += OnHistoryChanged;
    }

    private int _previousFrameIndex;

    /// <summary>The document being edited.</summary>
    public PixelDocument Document { get; }

    /// <summary>The undo history for this document.</summary>
    public DocumentHistory History { get; }

    /// <summary>The layer that drawing tools operate on, or <see langword="null"/> if none.</summary>
    public LayerNode? ActiveLayer
    {
        get => _activeLayer;
        set
        {
            if (SetProperty(ref _activeLayer, value))
            {
                OnPropertyChanged(nameof(ActiveRasterLayer));
                OnPropertyChanged(nameof(ActiveTextLayer));
                OnPropertyChanged(nameof(CanEditActiveLayer));
            }
        }
    }

    /// <summary>
    /// The active layer if it is a raster layer that can hold pixels, otherwise
    /// <see langword="null"/>. Drawing tools use this rather than <see cref="ActiveLayer"/>.
    /// </summary>
    public RasterLayer? ActiveRasterLayer => _activeLayer as RasterLayer;

    /// <summary>
    /// The active layer if it is a text layer, otherwise <see langword="null"/>. The text
    /// panel binds to this to know whose typography it is editing.
    /// </summary>
    public TextLayer? ActiveTextLayer => _activeLayer as TextLayer;

    /// <summary>
    /// The text layer currently being typed into, or <see langword="null"/> when no text is
    /// being edited.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This lives on the workspace rather than on the text tool for two reasons. Tools are
    /// singletons shared by every open document, so per-document state cannot live on one;
    /// and keystrokes arrive at the canvas control, which needs somewhere to route them that
    /// does not require it to understand how text is stored.
    /// </para>
    /// <para>
    /// Assign through <see cref="BeginTextEdit"/> and <see cref="CommitTextEdit"/> rather
    /// than directly, so that starting a second edit cannot silently discard the first.
    /// </para>
    /// </remarks>
    public TextEditSession? ActiveTextEdit
    {
        get => _activeTextEdit;
        private set => SetProperty(ref _activeTextEdit, value);
    }

    /// <summary>
    /// Starts typing into a text layer, first finishing any edit already in progress.
    /// </summary>
    /// <param name="session">The session to start.</param>
    public void BeginTextEdit(TextEditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        CommitTextEdit();
        ActiveTextEdit = session;
    }

    /// <summary>
    /// Finishes any text edit in progress, recording it as one entry in the history.
    /// </summary>
    /// <returns><see langword="true"/> if an edit was in progress and recorded a change.</returns>
    /// <remarks>
    /// Safe to call when nothing is being edited, which matters because it is called
    /// defensively from several places: switching tool, switching document tab, saving, and
    /// clicking somewhere else on the canvas.
    /// </remarks>
    public bool CommitTextEdit()
    {
        if (ActiveTextEdit is not { } session)
        {
            return false;
        }

        // Cleared first, so that anything reacting to the history push sees no edit in
        // progress rather than one that is halfway through ending.
        ActiveTextEdit = null;
        return session.Commit();
    }

    /// <summary>Abandons any text edit in progress, restoring what was there before it.</summary>
    public void CancelTextEdit()
    {
        if (ActiveTextEdit is not { } session)
        {
            return;
        }

        ActiveTextEdit = null;
        session.Cancel();
    }

    /// <summary>
    /// The selected pixels currently lifted off the layer and being dragged, or
    /// <see langword="null"/> when nothing is being moved.
    /// </summary>
    /// <remarks>
    /// Lives here for the same two reasons the text edit does: tools are singletons shared by
    /// every open document and so cannot hold per-document state, and the canvas needs somewhere
    /// to route a drag that does not require it to understand how pixels are stored.
    /// </remarks>
    public SelectionTransform? ActiveTransform
    {
        get => _activeTransform;
        private set => SetProperty(ref _activeTransform, value);
    }

    /// <summary>Lifts the selected pixels so they can be dragged around.</summary>
    /// <returns>
    /// <see langword="true"/> if something is now being moved — including when a move was
    /// already in progress, which is left alone rather than restarted.
    /// </returns>
    public bool BeginTransform()
    {
        if (ActiveTransform is not null)
        {
            return true;
        }

        if (SelectionTransform.Begin(this) is not { } transform)
        {
            return false;
        }

        ActiveTransform = transform;
        return true;
    }

    /// <summary>
    /// Puts down whatever is being moved, recording the whole drag as one history entry.
    /// </summary>
    /// <returns><see langword="true"/> if a move was in progress and actually moved something.</returns>
    /// <remarks>
    /// Safe to call when nothing is being moved, because it is called defensively from
    /// everywhere a move must not survive: switching tool, switching tab, and saving.
    /// </remarks>
    public bool CommitTransform()
    {
        if (ActiveTransform is not { } transform)
        {
            return false;
        }

        // Cleared first, so anything reacting to the history push sees no move in progress
        // rather than one that is halfway through ending.
        ActiveTransform = null;

        if (transform.Commit() is not { } command)
        {
            return false;
        }

        History.Push(command);
        return true;
    }

    /// <summary>Abandons a move, putting the pixels back where they came from.</summary>
    public void CancelTransform()
    {
        if (ActiveTransform is not { } transform)
        {
            return;
        }

        ActiveTransform = null;
        transform.Cancel();
    }

    /// <summary>
    /// Gets a value indicating whether drawing is currently possible: there is an active
    /// raster layer and it is neither locked nor hidden.
    /// </summary>
    /// <remarks>
    /// Hidden layers are excluded because drawing on something invisible looks to the artist
    /// like the tool is broken.
    /// </remarks>
    public bool CanEditActiveLayer =>
        ActiveRasterLayer is { IsLocked: false } layer && layer.IsEffectivelyVisible;

    /// <summary>The frame being edited.</summary>
    public Frame ActiveFrame
    {
        get => _activeFrame;
        set
        {
            if (SetProperty(ref _activeFrame, value ?? throw new ArgumentNullException(nameof(value))))
            {
                OnPropertyChanged(nameof(ActiveFrameIndex));
            }
        }
    }

    /// <summary>The active frame's position in the timeline, or 0 if it has somehow left it.</summary>
    public int ActiveFrameIndex => Math.Max(0, Document.IndexOfFrame(_activeFrame));

    /// <summary>
    /// The animation currently being worked on, or <see langword="null"/> when the whole
    /// timeline is treated as one.
    /// </summary>
    /// <remarks>
    /// This is what the preview plays and what onion skinning is allowed to reach across. It
    /// is editing state rather than artwork, which is why it lives here and not on the
    /// document alongside the tag list itself.
    /// </remarks>
    public AnimationTag? ActiveTag
    {
        get => _activeTag;
        set => SetProperty(ref _activeTag, value);
    }

    /// <summary>Moves to a frame by its position, clamped to the timeline.</summary>
    /// <param name="index">The position to move to.</param>
    /// <returns>The frame that is now active.</returns>
    public Frame GoToFrame(int index)
    {
        int clamped = Math.Clamp(index, 0, Document.Frames.Count - 1);
        ActiveFrame = Document.Frames[clamped];
        return ActiveFrame;
    }

    /// <summary>
    /// Puts the active frame and tag back onto something that still exists, after the
    /// timeline has been changed underneath them.
    /// </summary>
    /// <remarks>
    /// Deleting the frame being edited, or undoing the addition of one, leaves the workspace
    /// pointing at a frame the document has never heard of — and every cel lookup after that
    /// silently returns nothing, so the canvas goes blank with no error. Callers that change
    /// the timeline call this afterwards.
    /// </remarks>
    public void EnsureValidFrame()
    {
        if (!Document.Frames.Contains(_activeFrame))
        {
            ActiveFrame = Document.Frames[Math.Clamp(_previousFrameIndex, 0, Document.Frames.Count - 1)];
        }

        _previousFrameIndex = ActiveFrameIndex;

        if (_activeTag is { } tag && !Document.Tags.Contains(tag))
        {
            ActiveTag = null;
        }
    }

    /// <summary>
    /// The active selection, or <see langword="null"/> when everything is editable. No tool
    /// in v0.1 creates one; see <see cref="SelectionMask"/> for why it is plumbed through
    /// anyway.
    /// </summary>
    public SelectionMask? Selection
    {
        get => _selection;
        set => SetProperty(ref _selection, value);
    }

    /// <summary>
    /// Announces that the current selection's contents have changed, without replacing it.
    /// </summary>
    /// <remarks>
    /// A tool that builds a mask pixel by pixel mutates one mask rather than allocating a new
    /// one per pointer sample — on a large canvas that would be megabytes per drag. Assigning
    /// the same instance back raises nothing, because the property compares references, so this
    /// is how such a tool asks the canvas to repaint the marching ants.
    /// </remarks>
    public void NotifySelectionChanged() => OnPropertyChanged(nameof(Selection));

    /// <summary>
    /// The full path the document was last loaded from or saved to, or
    /// <see langword="null"/> for a document that has never been saved.
    /// </summary>
    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (SetProperty(ref _filePath, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    /// <summary>The document's name for the window title and tab, without any path or extension.</summary>
    public string DisplayName => string.IsNullOrEmpty(_filePath)
        ? "Untitled"
        : Path.GetFileNameWithoutExtension(_filePath);

    /// <summary>
    /// Gets a value indicating whether the document differs from what was last saved.
    /// </summary>
    public bool HasUnsavedChanges => !History.IsAtSavedState;

    private SelectionTransform? _activeTransform;

    /// <summary>
    /// Returns the cel the active layer occupies on the active frame, creating it on first
    /// use, or <see langword="null"/> if the active layer cannot be drawn on.
    /// </summary>
    /// <returns>The cel to draw into, or <see langword="null"/>.</returns>
    public Cel? GetOrCreateActiveCel()
    {
        if (!CanEditActiveLayer || ActiveRasterLayer is not RasterLayer layer)
        {
            return null;
        }

        return Document.GetOrCreateCel(layer, ActiveFrame);
    }

    /// <summary>
    /// Returns the cel the active layer already occupies on the active frame, without
    /// creating one.
    /// </summary>
    /// <returns>The cel, or <see langword="null"/> if the layer has nothing on this frame.</returns>
    /// <remarks>
    /// Text layers keep their glyphs in a cel exactly as raster layers keep their artwork, so
    /// both are returned. Callers that intend to <em>write</em> want
    /// <see cref="GetOrCreateActiveCel"/> instead; this is for the ones that only want to look,
    /// and which must not leave an empty cel behind on a layer the artist merely clicked.
    /// </remarks>
    public Cel? FindActiveCel() => ActiveLayer is RasterLayer or TextLayer
        ? Document.Cels.Find(ActiveLayer.Id, ActiveFrame.Id)
        : null;

    /// <summary>
    /// Marks every pixel the given layer actually paints on the active frame.
    /// </summary>
    /// <param name="layer">The layer to mark, or a group, whose visible children are all marked.</param>
    /// <returns><see langword="true"/> if anything was marked.</returns>
    /// <remarks>
    /// <para>
    /// This is what Ctrl-clicking a layer's preview does, and it is the fastest way to get a
    /// selection shaped like a drawing rather than like a box: the artist has already drawn the
    /// shape once, and asking them to trace it again with a wand is asking them to do the same
    /// work twice.
    /// </para>
    /// <para>
    /// <b>Only pixels that are actually painted.</b> A fully transparent pixel is not part of the
    /// artwork however much of the canvas it covers, and marking it would make the following move
    /// drag a rectangle of nothing along with the drawing.
    /// </para>
    /// </remarks>
    public bool SelectLayerPixels(LayerNode layer)
    {
        ArgumentNullException.ThrowIfNull(layer);

        SelectionMask mask = new(Document.Size);
        MarkLayerPixels(layer, mask, isRoot: true);
        mask.RecalculateBounds();

        // An empty mask and no mask mean the same thing to every tool, but only the second says
        // so: an empty one clips every drawing tool to nothing.
        Selection = mask.IsEmpty ? null : mask;
        return !mask.IsEmpty;
    }

    /// <summary>Adds one layer subtree's painted pixels to a mask.</summary>
    /// <remarks>
    /// The layer that was asked for is marked whether or not it is hidden — an artist who
    /// Ctrl-clicks a hidden layer's preview plainly means that layer. Children inside a group
    /// honour their own visibility, because there the question is what the group shows.
    /// </remarks>
    private void MarkLayerPixels(LayerNode layer, SelectionMask mask, bool isRoot)
    {
        if (!isRoot && (!layer.IsVisible || layer.Opacity <= 0.0))
        {
            return;
        }

        if (layer is GroupLayer group)
        {
            foreach (LayerNode child in group.Children)
            {
                MarkLayerPixels(child, mask, isRoot: false);
            }

            return;
        }

        if (Document.Cels.Find(layer.Id, ActiveFrame.Id) is not { } cel)
        {
            return;
        }

        // The cel may be larger than the canvas and sit at an offset, so only the part of it
        // that is on the canvas can be marked; a mask has no room for the rest.
        ImageRect visible = cel.Bounds.Intersect(Document.Bounds);

        for (int y = visible.Top; y < visible.Bottom; y++)
        {
            for (int x = visible.Left; x < visible.Right; x++)
            {
                if (cel.Pixels.GetPixelOrTransparent(x - cel.Offset.X, y - cel.Offset.Y).A > 0)
                {
                    mask.SetCoverage(x, y, byte.MaxValue);
                }
            }
        }
    }

    /// <summary>Records that the document has just been saved to <paramref name="filePath"/>.</summary>
    /// <param name="filePath">Where it was saved.</param>
    public void MarkSaved(string filePath)
    {
        FilePath = filePath;
        History.MarkSaved();
    }

    private void OnHistoryChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(HasUnsavedChanges));
}
