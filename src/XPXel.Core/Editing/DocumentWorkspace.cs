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

    /// <summary>Every layer marked in the layers panel; see <see cref="SelectedLayers"/>.</summary>
    private LayerNode[] _selectedLayers = [];

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
    /// Every layer marked in the layers panel, in the panel's own order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Distinct from <see cref="ActiveLayer"/>, and larger than it.</b> One layer is always
    /// the active one - the layer a stroke lands on, the layer the text panel styles - because
    /// drawing has to have exactly one answer to "where does this go?". Marking several is a
    /// second, weaker idea: these are the layers the next <em>structural</em> operation acts
    /// on. Grouping, deleting, duplicating, transforming and copying all read this; every
    /// drawing tool reads <see cref="ActiveLayer"/> and ignores it.
    /// </para>
    /// <para>
    /// It is never empty while a layer is active: a panel with one row highlighted marks that
    /// row, so every caller can read this and needs no separate "or else the active one" path.
    /// </para>
    /// </remarks>
    public IReadOnlyList<LayerNode> SelectedLayers => _selectedLayers;

    /// <summary>Gets a value indicating whether more than one layer is marked.</summary>
    public bool HasMultipleSelectedLayers => _selectedLayers.Length > 1;

    /// <summary>Replaces the marked set.</summary>
    /// <param name="layers">
    /// The layers to mark. Anything that is not part of this document is dropped, and an empty
    /// or <see langword="null"/> sequence falls back to the active layer.
    /// </param>
    public void SetSelectedLayers(IEnumerable<LayerNode>? layers)
    {
        List<LayerNode> marked = [];

        foreach (LayerNode layer in layers ?? [])
        {
            if (Document.FindLayer(layer.Id) == layer && !marked.Contains(layer))
            {
                marked.Add(layer);
            }
        }

        if (marked.Count == 0 && ActiveLayer is { } active)
        {
            marked.Add(active);
        }

        if (marked.Count == _selectedLayers.Length && marked.SequenceEqual(_selectedLayers))
        {
            return;
        }

        _selectedLayers = [.. marked];
        OnPropertyChanged(nameof(SelectedLayers));
        OnPropertyChanged(nameof(HasMultipleSelectedLayers));
    }

    /// <summary>
    /// The marked layers' drawable leaves - raster and text layers - in document order,
    /// bottom-most first.
    /// </summary>
    /// <returns>The leaves, which may be empty.</returns>
    /// <remarks>
    /// <para>
    /// Marking a folder means marking what is inside it, which is what dragging a folder does
    /// in every application that has folders, so groups are expanded rather than skipped.
    /// Hidden and locked children are expanded too: what a folder contains does not change
    /// with a visibility tick, and each caller decides for itself whether it may write to a
    /// leaf it has been handed.
    /// </para>
    /// <para>
    /// <b>Document order, not panel order.</b> The result is used to composite and to stamp,
    /// and both are only correct bottom-most first - which is the order the document stores
    /// layers in, and the reverse of the order the panel shows them in.
    /// </para>
    /// </remarks>
    public IReadOnlyList<LayerNode> GetSelectedLeaves()
    {
        if (_selectedLayers.Length == 0)
        {
            return ActiveLayer is RasterLayer or TextLayer ? [ActiveLayer] : [];
        }

        List<LayerNode> leaves = [];

        foreach (LayerNode node in Document.EnumerateLayers())
        {
            if (node is RasterLayer or TextLayer && IsUnderAMarkedLayer(node))
            {
                leaves.Add(node);
            }
        }

        return leaves;
    }

    /// <summary>Whether a layer is itself marked, or lies inside a marked folder.</summary>
    private bool IsUnderAMarkedLayer(LayerNode node)
    {
        for (LayerNode? walk = node; walk is not null; walk = walk.Parent)
        {
            if (Array.IndexOf(_selectedLayers, walk) >= 0)
            {
                return true;
            }
        }

        return false;
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

    /// <summary>Floats a clip over the active layer, ready for the artist to place it.</summary>
    /// <param name="clip">The pixels to paste.</param>
    /// <returns><see langword="true"/> if the clip is now in the artist's hand.</returns>
    /// <remarks>
    /// <para>
    /// Pasting puts anything already floating down first, rather than discarding it. Two things
    /// cannot be in one hand, and of the two possible answers — lose the first or keep it — only
    /// one of them can be undone.
    /// </para>
    /// <para>
    /// <b>It lands floating rather than stamped.</b> Where a paste should go is a question only
    /// the artist can answer, so it arrives already picked up: drag it, nudge it with the arrow
    /// keys, place it with Enter, or press Escape and it was never there. Where it starts is
    /// <see cref="PixelClip.GetPasteOrigin"/>'s decision — back where it was cut from when that
    /// still fits, and the middle of the canvas otherwise.
    /// </para>
    /// </remarks>
    public bool BeginPaste(PixelClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);

        CommitTransform();

        if (SelectionTransform.BeginPaste(this, clip, clip.GetPasteOrigin(Document.Bounds)) is not { } transform)
        {
            return false;
        }

        ActiveTransform = transform;
        return true;
    }

    /// <summary>
    /// The free transform in progress, or <see langword="null"/> when nothing is being scaled,
    /// flipped or turned.
    /// </summary>
    /// <remarks>
    /// Deliberately a separate property from <see cref="ActiveTransform"/> rather than a mode of
    /// it. A move is artwork in the hand and the whole canvas is somewhere to put it down; a
    /// free transform is a box with eight handles, and the two want opposite things from every
    /// press the canvas receives. Only one of them can be in progress at a time, which
    /// <see cref="BeginFreeTransform"/> enforces by putting the other one down first.
    /// </remarks>
    public FreeTransform? ActiveFreeTransform
    {
        get => _activeFreeTransform;
        private set => SetProperty(ref _activeFreeTransform, value);
    }

    /// <summary>Puts a transform box around the marked pixels, or the marked layers.</summary>
    /// <param name="scope">Whether to transform the artwork or only the marching ants.</param>
    /// <returns>
    /// <see langword="true"/> if a box is now up, including when one already was and is left
    /// alone rather than restarted.
    /// </returns>
    public bool BeginFreeTransform(FreeTransformScope scope = FreeTransformScope.Pixels)
    {
        if (ActiveFreeTransform is { } existing)
        {
            return existing.Scope == scope;
        }

        // Anything already floating goes down first. Two things cannot be in one hand, and of
        // the two ways to resolve that, only putting the first down can be undone.
        CommitTransform();

        if (FreeTransform.Begin(this, scope) is not { } transform)
        {
            return false;
        }

        ActiveFreeTransform = transform;
        return true;
    }

    /// <summary>
    /// Applies the transform box, recording the whole episode as one history entry.
    /// </summary>
    /// <returns><see langword="true"/> if a box was up and it actually changed something.</returns>
    public bool CommitFreeTransform()
    {
        if (ActiveFreeTransform is not { } transform)
        {
            return false;
        }

        // Cleared first, so anything reacting to the history push sees no transform in progress
        // rather than one halfway through ending.
        ActiveFreeTransform = null;

        if (transform.Commit() is not { } command)
        {
            return false;
        }

        History.Push(command);
        return true;
    }

    /// <summary>Abandons a transform, putting everything back as it was.</summary>
    public void CancelFreeTransform()
    {
        if (ActiveFreeTransform is not { } transform)
        {
            return;
        }

        ActiveFreeTransform = null;
        transform.Cancel();
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
    private FreeTransform? _activeFreeTransform;

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

    /// <summary>
    /// Clears every marked pixel, on every marked layer, as one undoable step.
    /// </summary>
    /// <param name="actionName">
    /// What the history entry is called. Cutting erases exactly as deleting does, and the only
    /// thing the artist needs told apart in the Edit menu is which of the two they asked for.
    /// </param>
    /// <returns><see langword="true"/> if anything was actually erased.</returns>
    /// <remarks>
    /// <para>
    /// This is what Delete and Backspace do, and it is the other half of making a selection
    /// useful: having marked something, the two things an artist wants next are to move it and
    /// to get rid of it. Every other editor of this kind spends the same two keys on it.
    /// </para>
    /// <para>
    /// <b>Erased, not cut.</b> The pixels become transparent rather than being filled with a
    /// colour — a hole in a sprite is what "delete" means here, and the layer beneath is what
    /// should show through it.
    /// </para>
    /// <para>
    /// <b>The marks stay behind.</b> Deleting does not deselect, because the usual next action
    /// is to draw something else in the space just cleared, and clearing the marks would throw
    /// away the shape the artist would need to make again.
    /// </para>
    /// </remarks>
    public bool EraseSelection(string actionName = "Erase")
    {
        ArgumentException.ThrowIfNullOrEmpty(actionName);

        if (Selection is not { IsEmpty: false } selection)
        {
            return false;
        }

        ImageRect region = selection.Bounds.Intersect(Document.Bounds);

        if (region.Width <= 0 || region.Height <= 0)
        {
            return false;
        }

        List<IUndoableCommand> erasures = [];

        foreach (LayerNode leaf in GetSelectedLeaves())
        {
            // A locked or hidden layer is skipped rather than refused. With several layers
            // marked the artist means "clear this out of my drawing", and stopping the whole
            // operation because one of them happens to be locked would be an answer to a
            // question nobody asked. Text layers are skipped for a harder reason: their pixels
            // are regenerated from the words, so a hole punched in one would heal itself the
            // next time anything re-rendered it.
            if (leaf is not RasterLayer { IsLocked: false } layer || !layer.IsEffectivelyVisible)
            {
                continue;
            }

            Cel cel = Document.GetOrCreateCel(layer, ActiveFrame);

            // The session is handed the selection, so it clips the writes for us and a mark
            // that extends past the canvas edge simply has no effect there.
            RasterEditSession session = RasterEditSession.Begin(Document, cel, actionName, selection);

            for (int y = region.Top; y < region.Bottom; y++)
            {
                for (int x = region.Left; x < region.Right; x++)
                {
                    session.SetPixel(x, y, PixelColor.Transparent);
                }
            }

            if (session.Commit() is { } command)
            {
                erasures.Add(command);
            }
        }

        if (erasures.Count == 0)
        {
            return false;
        }

        // The pixels are already cleared by the time the commands exist, so they are recorded
        // rather than executed - and as one entry, because one keystroke should cost one undo
        // however many layers it reached.
        History.Push(erasures.Count == 1 ? erasures[0] : new CompositeCommand(actionName, erasures));
        return true;
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
