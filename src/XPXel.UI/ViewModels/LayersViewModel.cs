using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.History;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Rendering;

namespace XPXel.UI.ViewModels;

/// <summary>
/// The layers panel: the stack of layers, which one is selected, and the operations that
/// change the stack.
/// </summary>
/// <remarks>
/// <para>
/// <b>Display order is the reverse of storage order.</b> The document stores layers
/// bottom-most first so the compositor can walk them forwards; every layers panel ever built
/// shows the top-most layer at the top of the list. The flip happens here, in exactly one
/// place, and nothing else in the codebase needs to think about it.
/// </para>
/// <para>
/// The list is rebuilt whenever the document reports a structural change, rather than being
/// patched incrementally. A pixel art document has tens of layers, not thousands, so the
/// rebuild is imperceptible and it removes an entire category of "the panel and the model
/// disagree" bugs.
/// </para>
/// </remarks>
public sealed partial class LayersViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// How long the panel waits after the last pixel change before re-rendering previews.
    /// </summary>
    /// <remarks>
    /// Rendering a preview costs time proportional to the canvas, so doing it straight from
    /// the document's change event would rebuild every row on every pointer move of a
    /// stroke. A short delay coalesces a whole stroke into one rebuild while still feeling
    /// immediate — the preview updates before the artist can look away from the canvas.
    /// </remarks>
    private static readonly TimeSpan ThumbnailRefreshDelay = TimeSpan.FromMilliseconds(150);

    private readonly DispatcherTimer _thumbnailTimer;

    private DocumentWorkspace? _workspace;
    private PixelDocument? _observedDocument;
    private bool _isRebuilding;

    /// <summary>
    /// Guards the round trip between the panel's selection and the workspace's.
    /// </summary>
    /// <remarks>
    /// The panel pushes its selection to the workspace and now also follows the workspace's,
    /// because the timeline, an undo and a placed transform all change the active layer without
    /// going anywhere near this panel. Both directions are real, so the pair is guarded rather
    /// than one of them being argued away.
    /// </remarks>
    private bool _isFollowingWorkspace;

    /// <summary>The other half of that guard: the panel is mid-way through telling the workspace.</summary>
    /// <remarks>
    /// The workspace raises its change events synchronously, so a push from this panel comes
    /// straight back through <see cref="OnWorkspacePropertyChanged"/> before the push has
    /// finished — at which point the workspace's active layer and its marked set disagree with
    /// each other, and following that half-finished state undid the change being made. Marking
    /// a second layer put the highlight back on the first, and a stroke then landed on both.
    /// </remarks>
    private bool _isPushingToWorkspace;

    /// <summary>
    /// The layers last copied, waiting to be pasted.
    /// </summary>
    /// <remarks>
    /// Kept on the panel rather than on the system clipboard: a layer is an XP-xel idea that no
    /// other program has a format for, and the panel is a single instance shared by every open
    /// document, so a layer copied in one tab pastes into another.
    /// </remarks>
    private IReadOnlyList<LayerClip>? _layerClipboard;

    /// <summary>
    /// Every highlighted row, the selected one included.
    /// </summary>
    /// <remarks>
    /// Kept here rather than read from the list box on demand, because every operation that
    /// consults it - a drop, a delete, a visibility tick - happens while the list box is busy
    /// with a gesture of its own, and asking it mid-gesture gets an answer that is halfway
    /// through changing.
    /// </remarks>
    private readonly List<LayerItemViewModel> _markedRows = [];

    /// <summary>Initialises the panel.</summary>
    /// <param name="settings">
    /// The shared layer options, which the panel's own settings button edits.
    /// </param>
    public LayersViewModel(LayerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Settings = settings;
        _thumbnailTimer = new DispatcherTimer { Interval = ThumbnailRefreshDelay };
        _thumbnailTimer.Tick += OnThumbnailTimerTick;
    }

    [ObservableProperty]
    private LayerItemViewModel? _selectedLayer;

    /// <summary>
    /// How layers behave when artwork is placed on them, edited from the panel's own settings
    /// button as well as from Settings ▸ Layers.
    /// </summary>
    /// <remarks>
    /// Both, rather than one or the other. These change what happens when a transform or a
    /// paste lands, which is a thing an artist wants to reach without leaving the panel they
    /// are already looking at - and it is also a preference, which belongs where preferences
    /// are kept. There is one object behind both, so they can never disagree.
    /// </remarks>
    public LayerSettings Settings { get; }

    /// <summary>
    /// Opens the full settings, supplied by the shell so the panel needs no knowledge of it.
    /// </summary>
    /// <remarks>
    /// A view model in a docked panel cannot reach the shell - the shell owns it, not the
    /// other way round - so the command is handed in rather than taken. It is null until the
    /// shell is built, and the button that uses it is simply disabled until then.
    /// </remarks>
    [ObservableProperty]
    private System.Windows.Input.ICommand? _openSettingsCommand;

    /// <summary>Every highlighted row: what a structural operation acts on.</summary>
    public IReadOnlyList<LayerItemViewModel> MarkedRows => _markedRows;

    /// <summary>Gets a value indicating whether more than one row is highlighted.</summary>
    public bool HasMultipleMarkedRows => _markedRows.Count > 1;

    /// <summary>
    /// Replaces the highlighted set, which the list box reports whenever it changes.
    /// </summary>
    /// <param name="rows">The rows now highlighted.</param>
    /// <remarks>
    /// The workspace is told as well, because copying, cutting and transforming all read the
    /// marked layers from there and the panel is the only thing that knows what they are.
    /// </remarks>
    public void SetMarkedRows(IEnumerable<LayerItemViewModel>? rows)
    {
        foreach (LayerItemViewModel row in _markedRows)
        {
            row.IsMarked = false;
        }

        _markedRows.Clear();

        foreach (LayerItemViewModel row in rows ?? [])
        {
            if (!_markedRows.Contains(row))
            {
                _markedRows.Add(row);
                row.IsMarked = true;
            }
        }

        if (!_isFollowingWorkspace)
        {
            PushToWorkspace(workspace => workspace.SetSelectedLayers(_markedRows.Select(row => row.Layer)));
        }

        OnPropertyChanged(nameof(MarkedRows));
        OnPropertyChanged(nameof(HasMultipleMarkedRows));
        NotifyCommandsCanExecuteChanged();
    }

    /// <summary>
    /// The rows an operation should act on, given the row it was aimed at.
    /// </summary>
    /// <param name="row">
    /// The row the gesture named - the one dragged, or the one right-clicked - or
    /// <see langword="null"/> when it came from a button and means the selection.
    /// </param>
    /// <returns>The rows to act on, in the panel order, never empty unless nothing is selected.</returns>
    /// <remarks>
    /// The rule every file manager uses: dragging one of several highlighted items brings all of
    /// them, and dragging an item that is not highlighted brings only itself. Anything else
    /// makes a careless drag destroy work the artist could not see they had selected.
    /// </remarks>
    public IReadOnlyList<LayerItemViewModel> GetTargets(LayerItemViewModel? row)
    {
        if (row is null)
        {
            return _markedRows.Count > 0 ? [.. _markedRows] : SelectedLayer is { } only ? [only] : [];
        }

        return _markedRows.Contains(row) ? [.. _markedRows] : [row];
    }

    /// <summary>Puts a set of rows into document order, bottom-most first.</summary>
    /// <remarks>
    /// The panel shows the stack upside down, so its own order is the reverse of the one every
    /// document operation needs. Sorting by the row index and reversing is exact, and cheaper
    /// than walking the layer tree for each comparison.
    /// </remarks>
    private List<LayerItemViewModel> InDocumentOrder(IEnumerable<LayerItemViewModel> rows) =>
        [.. rows.OrderByDescending(Layers.IndexOf)];

    /// <summary>The layers, top-most first.</summary>
    public ObservableCollection<LayerItemViewModel> Layers { get; } = [];

    /// <summary>The blend modes offered in each row's dropdown.</summary>
    public static IReadOnlyList<BlendMode> BlendModes { get; } = Enum.GetValues<BlendMode>();

    /// <summary>Points the panel at a different document, or at none.</summary>
    /// <param name="workspace">The document to present.</param>
    public void Attach(DocumentWorkspace? workspace)
    {
        if (ReferenceEquals(_workspace, workspace))
        {
            return;
        }

        if (_workspace is not null)
        {
            _workspace.PropertyChanged -= OnWorkspacePropertyChanged;
        }

        if (_observedDocument is not null)
        {
            _observedDocument.Changed -= OnDocumentChanged;
        }

        _workspace = workspace;
        _observedDocument = workspace?.Document;

        if (_workspace is not null)
        {
            // The document says when its layers change; the workspace says which frame and
            // which layer are being worked on. The panel needs both, and used to hear only the
            // first - so stepping to another frame left every preview showing the frame before
            // it, and a layer made active from the timeline left the highlight behind.
            _workspace.PropertyChanged += OnWorkspacePropertyChanged;
        }

        if (_observedDocument is not null)
        {
            _observedDocument.Changed += OnDocumentChanged;
        }

        Rebuild();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _thumbnailTimer.Stop();
        _thumbnailTimer.Tick -= OnThumbnailTimerTick;

        if (_workspace is not null)
        {
            _workspace.PropertyChanged -= OnWorkspacePropertyChanged;
            _workspace = null;
        }

        if (_observedDocument is not null)
        {
            _observedDocument.Changed -= OnDocumentChanged;
            _observedDocument = null;
        }

        ClearRows();
    }

    /// <summary>Adds an empty layer above the selected one and selects it.</summary>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void AddLayer()
    {
        if (_workspace is not { } workspace)
        {
            return;
        }

        PixelDocument document = workspace.Document;
        GroupLayer parent = SelectedLayer?.Layer.Parent ?? document.Root;
        int index = SelectedLayer is null ? parent.Children.Count : parent.IndexOf(SelectedLayer.Layer) + 1;

        RasterLayer layer = new(Guid.NewGuid(), NextLayerName(document));
        workspace.History.Execute(new AddLayerCommand("Add Layer", document, layer, parent, index));

        SelectLayer(layer);
    }

    /// <summary>Copies a layer, artwork and all, and selects the copy.</summary>
    /// <param name="row">
    /// The layer to copy, or <see langword="null"/> for the selected one. Dropping a row
    /// onto the "new layer" button passes it explicitly, because the dragged layer is not
    /// necessarily the selected one.
    /// </param>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DuplicateLayer(LayerItemViewModel? row)
    {
        if (_workspace is not { } workspace || GetTargets(row) is not { Count: > 0 } targets)
        {
            return;
        }

        PixelDocument document = workspace.Document;
        List<IUndoableCommand> copies = [];
        LayerNode? last = null;

        foreach (LayerItemViewModel target in InDocumentOrder(targets))
        {
            LayerNode source = target.Layer;

            if (source.Parent is not { } parent)
            {
                continue;
            }

            // DuplicateLayer both copies the cels and inserts the layer, so the copy is removed
            // again and handed to the command, which owns putting it back on redo.
            LayerNode copy = document.DuplicateLayer(source, $"{source.Name} copy");
            IReadOnlyList<Cel> cels = document.RemoveLayer(copy);

            AddLayerCommand command = new(
                "Duplicate Layer", document, copy, parent, parent.IndexOf(source) + 1, cels);

            command.Redo();
            copies.Add(command);
            last = copy;
        }

        if (copies.Count == 0)
        {
            return;
        }

        // The copies are already in the document, so the entry is recorded rather than run.
        workspace.History.Push(copies.Count == 1
            ? copies[0]
            : new CompositeCommand("Duplicate Layers", copies));

        if (last is not null)
        {
            SelectLayer(last);
        }
    }

    /// <summary>Deletes a layer. The last remaining top-level layer cannot be deleted.</summary>
    /// <param name="row">
    /// The layer to delete, or <see langword="null"/> for the selected one. Dropping a row
    /// onto the delete button passes it explicitly: the layer in the artist's hand is not
    /// necessarily the one that happens to be selected, and deleting the wrong layer is about
    /// the worst thing this panel could do.
    /// </param>
    [RelayCommand(CanExecute = nameof(CanDeleteLayer))]
    private void DeleteLayer(LayerItemViewModel? row)
    {
        if (_workspace is not { } workspace || GetTargets(row) is not { Count: > 0 } targets)
        {
            return;
        }

        List<IUndoableCommand> removals = [];

        // Top-most first, so that a folder is removed before anything inside it is reached and
        // the child is quietly skipped rather than removed twice.
        foreach (LayerItemViewModel target in InDocumentOrder(targets).AsEnumerable().Reverse())
        {
            // The guard is repeated here rather than left to CanExecute, because a drop invokes
            // the command directly and CanExecute only ever considers the selected layer. It is
            // asked again per layer, since deleting the first of them can make the second the
            // last top-level layer there is.
            if (target.Layer.Parent is null || !CanDelete(workspace, target))
            {
                continue;
            }

            RemoveLayerCommand command = new("Delete Layer", workspace.Document, target.Layer);
            command.Redo();
            removals.Add(command);
        }

        if (removals.Count == 0)
        {
            return;
        }

        workspace.History.Push(removals.Count == 1
            ? removals[0]
            : new CompositeCommand("Delete Layers", removals));

        SelectedLayer = Layers.FirstOrDefault();
    }

    /// <summary>
    /// Bakes every marked layer into one, flattening them exactly as the canvas draws them.
    /// </summary>
    /// <param name="row">
    /// The row the gesture began on, or <see langword="null"/> for the marked set. A row
    /// outside the marked set merges nothing, since one layer merged with itself is a no-op.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Every frame, not only the one on screen.</b> Merging a walk cycle's layers on frame
    /// four and leaving frames one to three unmerged would produce a document whose layer stack
    /// changes as it plays, which is not a thing a document can be. So the flatten runs once per
    /// frame and the result is one layer holding the whole animation.
    /// </para>
    /// <para>
    /// <b>The result is a new layer rather than the bottom one overwritten.</b> A merge set can
    /// include a folder or a text layer, neither of which can simply be handed a buffer of
    /// pixels — and going through <see cref="AddLayerCommand"/> means the whole thing is undone
    /// by putting the originals back rather than by trying to reconstruct them.
    /// </para>
    /// <para>
    /// It flattens through <see cref="LayerCompositor.ComposeLayers"/>, the same code the canvas
    /// draws with, so what lands is what the artist was looking at when they asked.
    /// </para>
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanMergeLayers))]
    private void MergeLayers(LayerItemViewModel? row)
    {
        if (_workspace is not { } workspace)
        {
            return;
        }

        List<LayerItemViewModel> targets = InDocumentOrder(GetTargets(row));

        if (targets.Count < 2)
        {
            return;
        }

        PixelDocument document = workspace.Document;
        List<LayerNode> layers = [.. targets.Select(t => t.Layer)];

        // The bottom-most is where the result goes and whose name it takes, which is what
        // every editor with a merge command does.
        LayerNode bottom = layers[0];

        if (bottom.Parent is not { } parent)
        {
            return;
        }

        int index = parent.IndexOf(bottom);
        RasterLayer merged = new(Guid.NewGuid(), bottom.Name)
        {
            IsVisible = true,
        };

        List<Cel> cels = [];
        LayerCompositor compositor = new();

        foreach (Frame frame in document.Frames)
        {
            PixelBuffer flattened = new(document.Size);
            compositor.ComposeLayers(document, frame, layers, flattened);

            // A frame on which none of the merged layers painted anything gets no cel at all,
            // which is how an empty cel is represented everywhere else in the document.
            if (!flattened.GetOccupiedBounds().IsEmpty)
            {
                cels.Add(new Cel(merged.Id, frame.Id, flattened));
            }
        }

        // The removals are built before anything moves, so each captures the position it will
        // be put back into. They run first and the new layer lands in the gap they leave, which
        // is why its index is the bottom-most layer's own: nothing merged sat below it.
        List<IUndoableCommand> parts =
            [.. targets.AsEnumerable().Reverse().Select(
                t => new RemoveLayerCommand("Merge Layers", document, t.Layer))];

        parts.Add(new AddLayerCommand("Merge Layers", document, merged, parent, index, cels));

        workspace.History.Execute(new CompositeCommand("Merge Layers", parts));
        SelectLayer(merged);
    }

    /// <summary>
    /// Gets a value indicating whether there are at least two marked layers to merge.
    /// </summary>
    private bool CanMergeLayers() => _workspace is not null && _markedRows.Count > 1;

    /// <summary>
    /// Adds an opaque white layer at the bottom of the stack, on every frame.
    /// </summary>
    /// <remarks>
    /// The thing an artist means by "give me a background". It is one drawing shared by the
    /// whole timeline — see <see cref="LayerNode.IsContinuous"/> — so it is behind every frame
    /// including the ones added afterwards, and painting on it from any frame changes all of
    /// them. Doing this by hand meant a white layer per frame and no way to keep them in step.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void AddBackgroundLayer()
    {
        if (_workspace is not { } workspace)
        {
            return;
        }

        PixelDocument document = workspace.Document;

        RasterLayer background = new(Guid.NewGuid(), NextBackgroundName(document));
        PixelBuffer white = new(document.Size);
        white.Fill(PixelColor.White);

        Cel cel = new(background.Id, document.Frames[0].Id, white);

        // Added first with an ordinary per-frame cel, then switched to being shared: two
        // existing commands rather than a third one that would have to know both halves.
        workspace.History.Execute(new CompositeCommand(
            "Add Background",
            [
                new AddLayerCommand("Add Background", document, background, document.Root, 0, [cel]),
                new SetLayerContinuousCommand(
                    "Add Background", document, background, continuous: true, document.Frames[0]),
            ]));

        SelectLayer(background);
    }

    /// <summary>
    /// Switches the marked layers between one drawing for the whole timeline and a drawing
    /// per frame.
    /// </summary>
    /// <param name="row">The row the gesture began on, or <see langword="null"/> for the marked set.</param>
    /// <remarks>
    /// Turning it on has to choose which frame's drawing survives, and it keeps the one being
    /// looked at. That is the only defensible answer and it is why the command is undoable in
    /// one step: getting it wrong should cost one Ctrl+Z, not an evening.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ToggleContinuous(LayerItemViewModel? row)
    {
        if (_workspace is not { } workspace || GetTargets(row) is not { Count: > 0 } targets)
        {
            return;
        }

        // The whole marked set follows whichever row was asked, so ticking it on one of five
        // marked layers does not leave the other four disagreeing with the tick.
        bool continuous = !(row ?? targets[0]).Layer.IsContinuous;
        List<IUndoableCommand> parts = [];

        foreach (LayerItemViewModel target in InDocumentOrder(targets))
        {
            SetLayerContinuousCommand command = new(
                continuous ? "Same on Every Frame" : "One Drawing per Frame",
                workspace.Document,
                target.Layer,
                continuous,
                workspace.ActiveFrame);

            if (command.HasChanges)
            {
                parts.Add(command);
            }
        }

        if (parts.Count == 0)
        {
            return;
        }

        workspace.History.Execute(parts.Count == 1
            ? parts[0]
            : new CompositeCommand(parts[0].Name, parts));
    }

    /// <summary>Copies this frame's drawing onto every other frame of the marked layers.</summary>
    /// <param name="row">The row the gesture began on, or <see langword="null"/> for the marked set.</param>
    /// <remarks>
    /// Independent copies, unlike <see cref="ToggleContinuousCommand"/>: this is for spreading
    /// a starting point that each frame will then be changed away from.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CopyToAllFrames(LayerItemViewModel? row)
    {
        if (_workspace is not { } workspace || GetTargets(row) is not { Count: > 0 } targets)
        {
            return;
        }

        List<IUndoableCommand> parts = [];

        foreach (LayerItemViewModel target in InDocumentOrder(targets))
        {
            CopyCelToAllFramesCommand command = new(
                "Copy to All Frames", workspace.Document, target.Layer, workspace.ActiveFrame);

            if (command.HasChanges)
            {
                parts.Add(command);
            }
        }

        if (parts.Count == 0)
        {
            return;
        }

        workspace.History.Execute(parts.Count == 1
            ? parts[0]
            : new CompositeCommand("Copy to All Frames", parts));
    }

    /// <summary>
    /// Takes a copy of the marked layers, artwork on every frame included, for pasting later.
    /// </summary>
    /// <param name="row">The row the gesture began on, or <see langword="null"/> for the marked set.</param>
    /// <remarks>
    /// <b>Separate from Ctrl+C on purpose.</b> Copying pixels and copying a layer are different
    /// things: one takes what is marked on the frame in front of you, the other takes the layer
    /// itself — its name, its opacity, its blend mode and every frame's drawing. Ctrl+C has
    /// always meant the first, and quietly making it sometimes mean the second would be worse
    /// than having two commands.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CopyLayer(LayerItemViewModel? row)
    {
        if (_workspace is not { } workspace || GetTargets(row) is not { Count: > 0 } targets)
        {
            return;
        }

        List<LayerClip> clips = [];

        foreach (LayerItemViewModel target in InDocumentOrder(targets))
        {
            if (LayerClip.From(workspace.Document, target.Layer) is { } clip)
            {
                clips.Add(clip);
            }
        }

        if (clips.Count > 0)
        {
            _layerClipboard = clips;
            PasteLayerCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Puts the copied layers back into the document, above the selected one.</summary>
    /// <remarks>
    /// Into <em>this</em> document, which may be a different one from the copy: the clip holds
    /// artwork per frame position rather than per frame identity, so pasting a four-frame layer
    /// into a two-frame document brings the first two and drops the rest instead of failing.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanPasteLayer))]
    private void PasteLayer()
    {
        if (_workspace is not { } workspace || _layerClipboard is not { Count: > 0 } clips)
        {
            return;
        }

        PixelDocument document = workspace.Document;
        GroupLayer parent = SelectedLayer?.Layer.Parent ?? document.Root;
        int index = SelectedLayer is null ? parent.Children.Count : parent.IndexOf(SelectedLayer.Layer) + 1;

        List<IUndoableCommand> parts = [];
        LayerNode? last = null;

        foreach (LayerClip clip in clips)
        {
            (RasterLayer layer, List<Cel> cels, bool continuous) = clip.Rebuild(document);

            parts.Add(new AddLayerCommand("Paste Layer", document, layer, parent, index, cels));

            if (continuous)
            {
                parts.Add(new SetLayerContinuousCommand(
                    "Paste Layer", document, layer, continuous: true, document.Frames[0]));
            }

            last = layer;
            index++;
        }

        if (parts.Count == 0)
        {
            return;
        }

        workspace.History.Execute(new CompositeCommand("Paste Layer", parts));

        if (last is not null)
        {
            SelectLayer(last);
        }
    }

    /// <summary>Gets a value indicating whether a layer has been copied.</summary>
    private bool CanPasteLayer() => _workspace is not null && _layerClipboard is { Count: > 0 };

    /// <summary>Produces "Background 2" style names, counting only the backgrounds.</summary>
    private static string NextBackgroundName(PixelDocument document)
    {
        int count = document.EnumerateLayers().Count(l => l.Name.StartsWith("Background", StringComparison.Ordinal));
        return count == 0 ? "Background" : $"Background {count + 1}";
    }

    /// <summary>Adds an empty folder above the selected layer and selects it.</summary>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void AddFolder()
    {
        if (_workspace is not { } workspace)
        {
            return;
        }

        PixelDocument document = workspace.Document;
        GroupLayer parent = SelectedLayer?.Layer.Parent ?? document.Root;
        int index = SelectedLayer is null ? parent.Children.Count : parent.IndexOf(SelectedLayer.Layer) + 1;

        GroupLayer folder = new(Guid.NewGuid(), NextFolderName(document));
        workspace.History.Execute(new AddLayerCommand("Add Folder", document, folder, parent, index));

        SelectLayer(folder);
    }

    /// <summary>
    /// Creates a folder in a layer's own place and moves that layer inside it, as one
    /// undoable step. This is what dropping a row onto the "new folder" button does.
    /// </summary>
    /// <param name="row">The layer to wrap, or <see langword="null"/> for the selected one.</param>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void GroupIntoFolder(LayerItemViewModel? row)
    {
        if (_workspace is not { } workspace || GetTargets(row) is not { Count: > 0 } targets)
        {
            return;
        }

        List<LayerNode> layers = [.. InDocumentOrder(targets).Select(item => item.Layer).Where(layer => layer.Parent is not null)];

        if (layers.Count == 0)
        {
            return;
        }

        GroupLayer folder = new(Guid.NewGuid(), NextFolderName(workspace.Document));

        workspace.History.Execute(new GroupLayersCommand(
            layers.Count == 1 ? "Group Layer" : "Group Layers",
            workspace.Document,
            folder,
            layers));

        SelectLayer(folder);
    }

    /// <summary>Moves a layer into a folder, on top of whatever the folder already holds.</summary>
    /// <param name="source">The row being dragged.</param>
    /// <param name="folder">The folder row it was dropped on.</param>
    public void MoveIntoFolder(LayerItemViewModel source, LayerItemViewModel folder)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(folder);

        if (_workspace is not { } workspace || folder.Layer is not GroupLayer group)
        {
            return;
        }

        List<IUndoableCommand> moves = [];
        LayerNode? last = null;

        foreach (LayerItemViewModel target in InDocumentOrder(GetTargets(source)))
        {
            if (ReferenceEquals(target, folder))
            {
                continue;
            }

            // Dropping a folder inside itself, or inside one of its own descendants, would
            // detach that whole subtree from the document. GroupLayer refuses it as well, but
            // stopping here avoids pushing a command that would throw when redone.
            if (target.Layer is GroupLayer dragged && IsDescendantOf(group, dragged))
            {
                continue;
            }

            // The top of a folder as displayed is its highest index, because the panel shows
            // the stack in reverse. Dropping onto a folder therefore lands the layers on top of
            // its contents, which is where an artist expects newly added layers to go.
            MoveLayerCommand command = new(
                "Move Into Folder", workspace.Document, target.Layer, group, group.Children.Count);

            command.Redo();
            moves.Add(command);
            last = target.Layer;
        }

        if (moves.Count == 0)
        {
            return;
        }

        workspace.History.Push(moves.Count == 1
            ? moves[0]
            : new CompositeCommand("Move Into Folder", moves));

        if (last is not null)
        {
            SelectLayer(last);
        }
    }

    /// <summary>Moves the selected layer one position up the stack.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveLayerUp() => MoveSelected(1);

    /// <summary>Moves the selected layer one position down the stack.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveLayerDown() => MoveSelected(-1);

    private bool HasDocument() => _workspace is not null;

    private bool HasSelection() => _workspace is not null && SelectedLayer is not null;

    private bool CanDeleteLayer() => HasSelection() && CanDelete(_workspace!, SelectedLayer!);

    /// <summary>
    /// A document must always keep at least one top-level layer, otherwise there is nowhere
    /// to draw and the artist is left with a canvas that silently ignores every stroke. A
    /// layer inside a folder is always removable, because the folder stays behind.
    /// </summary>
    private static bool CanDelete(DocumentWorkspace workspace, LayerItemViewModel row) =>
        workspace.Document.Root.Children.Count > 1
        || !ReferenceEquals(row.Layer.Parent, workspace.Document.Root);

    private void MoveSelected(int delta)
    {
        if (_workspace is not { } workspace || SelectedLayer?.Layer is not { Parent: { } parent } layer)
        {
            return;
        }

        int current = parent.IndexOf(layer);
        int target = current + delta;
        if (target < 0 || target >= parent.Children.Count)
        {
            return;
        }

        workspace.History.Execute(new MoveLayerCommand("Reorder Layer", workspace.Document, layer, parent, target));
        SelectLayer(layer);
    }

    /// <summary>
    /// Follows the workspace: which frame is being drawn on, and which layers are marked.
    /// </summary>
    private void OnWorkspacePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isPushingToWorkspace)
        {
            return;
        }

        switch (e.PropertyName)
        {
            // A preview shows one layer on one frame, so stepping along the timeline changes
            // every one of them. Rendered at once rather than through the throttle: the delay
            // exists to coalesce a burst of pixel changes during a stroke, and a frame change
            // is a single event that the artist is looking straight at.
            case nameof(DocumentWorkspace.ActiveFrame):
                RefreshThumbnails();
                break;

            case nameof(DocumentWorkspace.ActiveLayer):
            case nameof(DocumentWorkspace.SelectedLayers):
                FollowWorkspaceSelection();
                RefreshActiveRow();
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Moves the panel's highlight onto whatever the workspace now says is selected.
    /// </summary>
    /// <remarks>
    /// The active layer is changed from outside this panel more often than in it: clicking a
    /// cell in the timeline, undoing the addition of a layer, and placing a transform that made
    /// a layer of its own all do it. Without this the panel went on highlighting the previous
    /// row while every tool wrote somewhere else.
    /// </remarks>
    private void FollowWorkspaceSelection()
    {
        if (_isFollowingWorkspace || _isRebuilding || _workspace is not { } workspace)
        {
            return;
        }

        List<LayerItemViewModel> marked =
            [.. Layers.Where(row => workspace.SelectedLayers.Contains(row.Layer))];

        LayerItemViewModel? active = Layers.FirstOrDefault(row => row.Layer == workspace.ActiveLayer);

        if (active is null && marked.Count == 0)
        {
            return;
        }

        bool sameMarks = marked.Count == _markedRows.Count && marked.All(_markedRows.Contains);

        if (sameMarks && ReferenceEquals(active, SelectedLayer))
        {
            return;
        }

        _isFollowingWorkspace = true;

        try
        {
            if (active is not null)
            {
                SelectedLayer = active;
            }

            if (!sameMarks && marked.Count > 0)
            {
                SetMarkedRows(marked);
            }
        }
        finally
        {
            _isFollowingWorkspace = false;
        }
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (e.Kind == DocumentChangeKind.Structure)
        {
            Rebuild();
            return;
        }

        // Pixels for the obvious reason, and properties because a preview of a folder is a
        // composite of what is inside it: ticking a child hidden changes the folder's picture
        // without a single pixel being written.
        if (e.Kind is DocumentChangeKind.Pixels or DocumentChangeKind.Properties)
        {
            ScheduleThumbnailRefresh();
        }
    }

    /// <summary>
    /// Requests a preview rebuild, restarting the delay so a burst of changes costs one
    /// rebuild rather than one per change.
    /// </summary>
    private void ScheduleThumbnailRefresh()
    {
        _thumbnailTimer.Stop();
        _thumbnailTimer.Start();
    }

    private void OnThumbnailTimerTick(object? sender, EventArgs e)
    {
        _thumbnailTimer.Stop();
        RefreshThumbnails();
    }

    /// <summary>
    /// Re-renders every row's preview.
    /// </summary>
    /// <remarks>
    /// Every row is refreshed rather than only the one being drawn on, because an undo, a
    /// redo or a future scripted edit can change any layer and the change event does not say
    /// which. A stale preview is a visible bug, so correctness wins here. If documents ever
    /// grow large enough for this to be felt, the fix is to carry the affected layer on
    /// <see cref="DocumentChangedEventArgs"/> rather than to guess here.
    /// </remarks>
    private void RefreshThumbnails()
    {
        if (_workspace is not { } workspace)
        {
            return;
        }

        foreach (LayerItemViewModel row in Layers)
        {
            row.RefreshThumbnail(workspace.Document, workspace.ActiveFrame);
        }
    }

    /// <summary>
    /// Moves a dragged layer next to another row, which is how the panel reorders the stack.
    /// </summary>
    /// <param name="source">The row being dragged.</param>
    /// <param name="target">The row it was dropped on.</param>
    /// <param name="insertAbove">
    /// <see langword="true"/> to place it above the target <i>as displayed</i>, which means
    /// a higher index in the document's bottom-first storage order.
    /// </param>
    public void ReorderLayer(LayerItemViewModel source, LayerItemViewModel target, bool insertAbove)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        if (_workspace is not { } workspace
            || ReferenceEquals(source, target)
            || target.Layer.Parent is not { } parent)
        {
            return;
        }

        // Dragging a group into its own descendant would detach that subtree from the
        // document. GroupLayer guards against this too, but failing silently here avoids
        // pushing a no-op onto the undo stack.
        if (source.Layer is GroupLayer group && IsDescendantOf(target.Layer, group))
        {
            return;
        }

        int targetIndex = parent.IndexOf(target.Layer);
        int destination = insertAbove ? targetIndex + 1 : targetIndex;

        // Within one group the move is remove-then-insert, so every position above the
        // layer's old slot shifts down by one and the destination must follow.
        int sourceIndex = parent.IndexOf(source.Layer);
        if (sourceIndex >= 0 && sourceIndex < destination)
        {
            destination--;
        }

        destination = Math.Clamp(destination, 0, Math.Max(0, parent.Children.Count - 1));
        if (sourceIndex == destination && ReferenceEquals(source.Layer.Parent, parent))
        {
            return;
        }

        workspace.History.Execute(new MoveLayerCommand(
            "Reorder Layer", workspace.Document, source.Layer, parent, destination));

        SelectLayer(source.Layer);
    }

    /// <summary>Walks up the parent chain to test containment.</summary>
    private static bool IsDescendantOf(LayerNode node, GroupLayer ancestor)
    {
        for (LayerNode? current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private void Rebuild()
    {
        Guid? previousSelection = SelectedLayer?.Layer.Id;
        HashSet<Guid> previousMarks = [.. _markedRows.Select(row => row.Layer.Id)];

        _isRebuilding = true;
        try
        {
            ClearRows();

            if (_workspace is { } workspace)
            {
                AppendRows(workspace.Document.Root, workspace.History, depth: 0);
            }

            SelectedLayer = Layers.FirstOrDefault(row => row.Layer.Id == previousSelection)
                ?? Layers.FirstOrDefault(row => row.Layer == _workspace?.ActiveLayer)
                ?? Layers.FirstOrDefault();
        }
        finally
        {
            _isRebuilding = false;
        }

        // The rows are new objects, so the marked set is re-found by layer identity. Layers that
        // no longer exist simply drop out, which is what a marked layer that was just deleted
        // should do.
        SetMarkedRows(Layers.Where(row => previousMarks.Contains(row.Layer.Id)));

        if (_markedRows.Count == 0 && SelectedLayer is { } fallback)
        {
            SetMarkedRows([fallback]);
        }

        // Selection was assigned while suppressed, so push it to the workspace now.
        ApplySelectionToWorkspace();
        NotifyCommandsCanExecuteChanged();

        RefreshActiveRow();

        // New rows start with no preview, so render immediately rather than waiting for the
        // throttle: an empty box beside a layer reads as "this layer is empty".
        RefreshThumbnails();
    }

    /// <summary>Walks the tree in reverse so the top-most layer ends up first in the list.</summary>
    private void AppendRows(GroupLayer group, DocumentHistory history, int depth)
    {
        for (int i = group.Children.Count - 1; i >= 0; i--)
        {
            LayerNode child = group.Children[i];
            Layers.Add(new LayerItemViewModel(child, history, depth)
            {
                Peers = GetPeerLayers,
                DeleteCommand = DeleteLayerCommand,
                MergeCommand = MergeLayersCommand,
                ContinuousCommand = ToggleContinuousCommand,
                CopyToAllFramesCommand = CopyToAllFramesCommand,
                CopyLayerCommand = CopyLayerCommand,
                PasteLayerCommand = PasteLayerCommand,
            });

            if (child is GroupLayer nested)
            {
                AppendRows(nested, history, depth + 1);
            }
        }
    }

    private void ClearRows()
    {
        foreach (LayerItemViewModel row in Layers)
        {
            row.Dispose();
        }

        Layers.Clear();
    }

    /// <summary>
    /// Marks every pixel one layer paints, which is what Ctrl-clicking its preview does.
    /// </summary>
    /// <param name="row">The row whose layer should be marked.</param>
    /// <remarks>
    /// The layer is made active first. Marking pixels the artist then cannot move — because the
    /// selection came from one layer and the active layer is another — would be a trap, and the
    /// click plainly says which layer is meant.
    /// </remarks>
    public void SelectLayerPixels(LayerItemViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (_workspace is not { } workspace)
        {
            return;
        }

        SelectedLayer = row;
        workspace.SelectLayerPixels(row.Layer);
    }

    private void SelectLayer(LayerNode layer) =>
        SelectedLayer = Layers.FirstOrDefault(row => row.Layer == layer) ?? SelectedLayer;

    private void ApplySelectionToWorkspace()
    {
        PushToWorkspace(workspace => workspace.ActiveLayer = SelectedLayer?.Layer);
        RefreshActiveRow();
    }

    /// <summary>Moves the "tools are pointed here" bar onto the active layer's row.</summary>
    private void RefreshActiveRow()
    {
        LayerNode? active = _workspace?.ActiveLayer;

        foreach (LayerItemViewModel row in Layers)
        {
            row.IsActive = ReferenceEquals(row.Layer, active);
        }
    }

    /// <summary>
    /// Tells the workspace something, without hearing it come back around.
    /// </summary>
    /// <remarks>
    /// See <see cref="_isPushingToWorkspace"/>: the workspace's events are synchronous, so
    /// every push would otherwise be answered mid-flight by a state that is only half updated.
    /// </remarks>
    private void PushToWorkspace(Action<DocumentWorkspace> push)
    {
        if (_workspace is not { } workspace)
        {
            return;
        }

        bool wasPushing = _isPushingToWorkspace;
        _isPushingToWorkspace = true;

        try
        {
            push(workspace);
        }
        finally
        {
            _isPushingToWorkspace = wasPushing;
        }
    }

    partial void OnSelectedLayerChanged(LayerItemViewModel? value)
    {
        if (_isRebuilding)
        {
            return;
        }

        if (_isFollowingWorkspace)
        {
            // The workspace is the one talking. Highlighting the row is the whole job; pushing
            // it straight back would be an echo.
            NotifyCommandsCanExecuteChanged();
            return;
        }

        // A row selected by anything other than the list box - adding a layer, dropping one,
        // an undo - has no marked set behind it, so it becomes the marked set. The list box
        // reports its own changes through SetMarkedRows and this leaves those alone.
        if (value is not null && !_markedRows.Contains(value))
        {
            SetMarkedRows([value]);
        }

        ApplySelectionToWorkspace();
        NotifyCommandsCanExecuteChanged();
    }

    /// <summary>
    /// Answers a row asking which other layers a change to it should reach.
    /// </summary>
    /// <remarks>
    /// The whole marked set when the row is part of it, and nothing when it is not. See
    /// <see cref="LayerItemViewModel.Peers"/>.
    /// </remarks>
    private IReadOnlyList<LayerNode> GetPeerLayers(LayerItemViewModel row) =>
        _markedRows.Contains(row) ? [.. _markedRows.Select(marked => marked.Layer)] : [];

    private void NotifyCommandsCanExecuteChanged()
    {
        AddLayerCommand.NotifyCanExecuteChanged();
        AddFolderCommand.NotifyCanExecuteChanged();
        AddBackgroundLayerCommand.NotifyCanExecuteChanged();
        MergeLayersCommand.NotifyCanExecuteChanged();
        ToggleContinuousCommand.NotifyCanExecuteChanged();
        CopyToAllFramesCommand.NotifyCanExecuteChanged();
        CopyLayerCommand.NotifyCanExecuteChanged();
        PasteLayerCommand.NotifyCanExecuteChanged();
        GroupIntoFolderCommand.NotifyCanExecuteChanged();
        DuplicateLayerCommand.NotifyCanExecuteChanged();
        DeleteLayerCommand.NotifyCanExecuteChanged();
        MoveLayerUpCommand.NotifyCanExecuteChanged();
        MoveLayerDownCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Produces "Layer 3" style names that do not collide with existing ones.</summary>
    private static string NextLayerName(PixelDocument document)
    {
        int count = document.EnumerateLayers().Count();
        return $"Layer {count + 1}";
    }

    /// <summary>Produces "Folder 2" style names, counting only the folders.</summary>
    private static string NextFolderName(PixelDocument document)
    {
        int count = document.EnumerateLayers().OfType<GroupLayer>().Count();
        return $"Folder {count + 1}";
    }
}
