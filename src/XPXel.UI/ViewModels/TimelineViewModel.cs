using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XPXel.Core.Animation;
using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.History;
using XPXel.UI.Rendering;
using XPXel.UI.Services;

namespace XPXel.UI.ViewModels;

/// <summary>
/// One cel in the timeline grid: what one layer holds on one frame.
/// </summary>
public sealed partial class TimelineCellViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _hasContent;

    [ObservableProperty]
    private bool _isCurrentFrame;

    [ObservableProperty]
    private bool _isCurrentLayer;

    /// <summary>Initialises the cell.</summary>
    /// <param name="layer">The layer this cell belongs to.</param>
    /// <param name="frameIndex">The frame's position in the timeline.</param>
    /// <param name="select">Makes this cell the one being edited.</param>
    public TimelineCellViewModel(LayerNode layer, int frameIndex, Action<TimelineCellViewModel> select)
    {
        Layer = layer;
        FrameIndex = frameIndex;
        SelectCommand = new RelayCommand(() => select(this));
    }

    /// <summary>The layer this cell belongs to.</summary>
    public LayerNode Layer { get; }

    /// <summary>The frame's position in the timeline.</summary>
    public int FrameIndex { get; }

    /// <summary>Makes this cell the one being edited.</summary>
    public IRelayCommand SelectCommand { get; }

    /// <summary>Whether this is the cell the artist is drawing on.</summary>
    public bool IsSelected => IsCurrentFrame && IsCurrentLayer;

    partial void OnIsCurrentFrameChanged(bool value) => OnPropertyChanged(nameof(IsSelected));

    partial void OnIsCurrentLayerChanged(bool value) => OnPropertyChanged(nameof(IsSelected));
}

/// <summary>One layer's row of cells across the timeline.</summary>
public sealed partial class TimelineRowViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isCurrentLayer;

    /// <summary>Initialises the row.</summary>
    /// <param name="layer">The layer this row shows.</param>
    /// <param name="cells">Its cells, one per frame.</param>
    public TimelineRowViewModel(LayerNode layer, IEnumerable<TimelineCellViewModel> cells)
    {
        Layer = layer;
        Cells = [.. cells];
    }

    /// <summary>The layer this row shows.</summary>
    public LayerNode Layer { get; }

    /// <summary>The layer's name, shown at the head of the row.</summary>
    public string Name => Layer.Name;

    /// <summary>The row's cells, one per frame, left to right.</summary>
    public ObservableCollection<TimelineCellViewModel> Cells { get; }
}

/// <summary>One frame's column header: its number, its hold time and a preview of it.</summary>
public sealed partial class TimelineFrameViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isCurrent;

    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private int _durationMilliseconds;

    /// <summary>Initialises the header.</summary>
    /// <param name="frame">The frame it describes.</param>
    /// <param name="index">The frame's position in the timeline.</param>
    /// <param name="select">Moves the playhead to this frame.</param>
    public TimelineFrameViewModel(Frame frame, int index, Action<int> select)
    {
        Frame = frame;
        Index = index;
        _durationMilliseconds = frame.DurationMilliseconds;
        SelectCommand = new RelayCommand(() => select(index));
    }

    /// <summary>The frame this header describes.</summary>
    public Frame Frame { get; }

    /// <summary>The frame's position in the timeline.</summary>
    public int Index { get; }

    /// <summary>The number shown to the artist, counting from one.</summary>
    public int Number => Index + 1;

    /// <summary>Moves the playhead to this frame.</summary>
    public IRelayCommand SelectCommand { get; }
}

/// <summary>One named animation's bar across the timeline.</summary>
public sealed partial class TimelineTagViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isActive;

    /// <summary>Initialises the bar.</summary>
    /// <param name="tag">The animation it represents.</param>
    /// <param name="select">Makes this the animation being worked on.</param>
    public TimelineTagViewModel(AnimationTag tag, Action<TimelineTagViewModel> select)
    {
        Tag = tag;
        SelectCommand = new RelayCommand(() => select(this));
    }

    /// <summary>The animation this bar represents.</summary>
    public AnimationTag Tag { get; }

    /// <summary>The animation's name.</summary>
    public string Name => Tag.Name;

    /// <summary>The range shown beside the name.</summary>
    public string RangeText => $"{Tag.From + 1}–{Tag.To + 1}";

    /// <summary>Makes this the animation being worked on.</summary>
    public IRelayCommand SelectCommand { get; }

    /// <summary>The bar's colour, falling back to the accent when the tag has none.</summary>
    public IBrush BarBrush => Tag.Color is { } color ? color.ToBrush() : Brushes.SlateGray;
}

/// <summary>
/// The timeline: frames across, layers down, and one cell for every drawing in between.
/// </summary>
/// <remarks>
/// <para>
/// <b>The grid is the document model made visible.</b> A cel is the intersection of one layer
/// and one frame, and this panel is that grid drawn on screen — which is why it is the shape
/// the timeline takes rather than a simple row of frames. It is what lets a background stay
/// still while only the legs move, and it is the reason the document was built with two axes
/// from the first version.
/// </para>
/// <para>
/// <b>Rebuilt wholesale on structural change.</b> Adding a frame shifts every column after it,
/// and reconciling that in place would be a great deal of code to save rebuilding a few dozen
/// small objects. Pixel changes do not rebuild anything — they refresh one thumbnail.
/// </para>
/// </remarks>
public sealed partial class TimelineViewModel : ViewModelBase, IDisposable
{
    /// <summary>How long to wait after a pixel change before redrawing a thumbnail.</summary>
    /// <remarks>
    /// A stroke reports a change per pointer move, and compositing the frame each time would
    /// make drawing stutter. Coalescing to a few refreshes a second keeps the timeline
    /// visibly live without competing with the canvas for the frame budget.
    /// </remarks>
    private static readonly TimeSpan ThumbnailDelay = TimeSpan.FromMilliseconds(200);

    private readonly Avalonia.Threading.DispatcherTimer _thumbnailTimer;

    private DocumentWorkspace? _workspace;
    private bool _isRebuilding;

    [ObservableProperty]
    private int _frameRate = 12;

    /// <summary>Initialises the panel.</summary>
    /// <param name="playback">The shared transport, which the panel's buttons drive.</param>
    /// <param name="onionSkin">The onion skin settings the canvas reads.</param>
    public TimelineViewModel(AnimationPlaybackService playback, OnionSkinSettings onionSkin)
    {
        ArgumentNullException.ThrowIfNull(playback);
        ArgumentNullException.ThrowIfNull(onionSkin);

        Playback = playback;
        OnionSkin = onionSkin;

        Rows = [];
        Frames = [];
        Tags = [];

        _thumbnailTimer = new Avalonia.Threading.DispatcherTimer(
            ThumbnailDelay,
            Avalonia.Threading.DispatcherPriority.Background,
            OnThumbnailTimerTick);
    }

    /// <summary>The shared transport controls.</summary>
    public AnimationPlaybackService Playback { get; }

    /// <summary>The onion skin settings, edited from this panel and read by the canvas.</summary>
    public OnionSkinSettings OnionSkin { get; }

    /// <summary>One row per layer, top-most first.</summary>
    public ObservableCollection<TimelineRowViewModel> Rows { get; }

    /// <summary>One header per frame, left to right.</summary>
    public ObservableCollection<TimelineFrameViewModel> Frames { get; }

    /// <summary>One bar per named animation.</summary>
    public ObservableCollection<TimelineTagViewModel> Tags { get; }

    /// <summary>The loop directions offered when editing an animation.</summary>
    public static IReadOnlyList<LoopDirection> LoopDirections { get; } = Enum.GetValues<LoopDirection>();

    /// <summary>Gets a value indicating whether a document is open.</summary>
    public bool HasDocument => _workspace is not null;

    /// <summary>The frame the artist is on, counting from one, for the status readout.</summary>
    public string PositionText => _workspace is { } workspace
        ? $"Frame {workspace.ActiveFrameIndex + 1} of {workspace.Document.Frames.Count}"
        : "No document";

    /// <summary>The animation currently being worked on, or null for the whole timeline.</summary>
    public AnimationTag? ActiveTag => _workspace?.ActiveTag;

    /// <summary>
    /// The index of the last frame, which is as far as an animation's range may reach.
    /// </summary>
    /// <remarks>
    /// Bound as the maximum of the range editors. A tag is positional and independently
    /// editable, so without this an artist could name frames 0 to 9 in a five-frame document
    /// — and everything that reads a range then has to defend itself against numbers that
    /// mean nothing. Onion skinning did not, and threw.
    /// </remarks>
    public int LastFrameIndex => Math.Max(0, (_workspace?.Document.Frames.Count ?? 1) - 1);

    /// <summary>The first frame of the animation being edited.</summary>
    /// <remarks>
    /// The range editors go through here rather than binding straight at the tag, and that is
    /// not ceremony. A tag is positional and independently editable, so a range set by hand is
    /// the one place a document can be given an animation that runs from frame 3 to frame 40
    /// of five — and everything downstream then has to defend itself against numbers that mean
    /// nothing. Onion skinning did not, and threw. Going through
    /// <see cref="SetTagRange(AnimationTag, int, int)"/> both keeps the range inside the
    /// timeline and puts the change on the undo stack, which a direct binding did neither of.
    /// </remarks>
    public int ActiveTagFrom
    {
        get => ActiveTag?.From ?? 0;
        set => SetActiveTagRange(value, ActiveTagTo);
    }

    /// <summary>The last frame of the animation being edited, inclusive.</summary>
    public int ActiveTagTo
    {
        get => ActiveTag?.To ?? 0;
        set => SetActiveTagRange(ActiveTagFrom, value);
    }

    /// <summary>Points the panel at a different document, or at none.</summary>
    /// <param name="workspace">The document to show.</param>
    public void Attach(DocumentWorkspace? workspace)
    {
        if (ReferenceEquals(_workspace, workspace))
        {
            return;
        }

        if (_workspace is not null)
        {
            _workspace.PropertyChanged -= OnWorkspaceChanged;
            _workspace.Document.Changed -= OnDocumentChanged;
        }

        _workspace = workspace;

        if (_workspace is not null)
        {
            _workspace.PropertyChanged += OnWorkspaceChanged;
            _workspace.Document.Changed += OnDocumentChanged;
        }

        Playback.Attach(workspace);
        Rebuild();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _thumbnailTimer.Stop();
        Attach(null);
    }

    // ---------------------------------------------------------------------------------
    // Frame commands
    // ---------------------------------------------------------------------------------

    /// <summary>Adds an empty frame after the current one.</summary>
    [RelayCommand]
    private void AddFrame()
    {
        if (_workspace is not { } workspace)
        {
            return;
        }

        int index = workspace.ActiveFrameIndex + 1;
        Frame frame = new(Guid.NewGuid()) { DurationMilliseconds = workspace.ActiveFrame.DurationMilliseconds };

        workspace.History.Execute(new AddFrameCommand("Add Frame", workspace.Document, frame, index));
        workspace.GoToFrame(index);
    }

    /// <summary>Copies the current frame and moves to the copy.</summary>
    /// <remarks>
    /// The most-used command on this panel by a wide margin: duplicating the previous drawing
    /// and changing part of it is how hand-drawn animation is actually made.
    /// </remarks>
    [RelayCommand]
    private void DuplicateFrame()
    {
        if (_workspace is not { } workspace)
        {
            return;
        }

        int index = workspace.ActiveFrameIndex;
        Frame source = workspace.ActiveFrame;

        // The document does the copying, and the history is then told about a change that
        // has already happened — the same pattern the layer commands use. The command holds
        // no cels because they are still in the document; its undo takes them back out.
        Frame copy = workspace.Document.DuplicateFrame(source, index + 1);

        workspace.History.Push(new AddFrameCommand("Duplicate Frame", workspace.Document, copy, index + 1));
        workspace.GoToFrame(index + 1);
    }

    /// <summary>Deletes the current frame.</summary>
    [RelayCommand]
    private void DeleteFrame()
    {
        if (_workspace is not { } workspace || workspace.Document.Frames.Count <= 1)
        {
            return;
        }

        int index = workspace.ActiveFrameIndex;

        workspace.History.Execute(new RemoveFrameCommand("Delete Frame", workspace.Document, workspace.ActiveFrame));
        workspace.EnsureValidFrame();
        workspace.GoToFrame(Math.Min(index, workspace.Document.Frames.Count - 1));
    }

    /// <summary>Moves the current frame one place earlier.</summary>
    [RelayCommand]
    private void MoveFrameLeft() => MoveFrame(-1);

    /// <summary>Moves the current frame one place later.</summary>
    [RelayCommand]
    private void MoveFrameRight() => MoveFrame(1);

    /// <summary>Moves a frame from one position to another, for drag and drop.</summary>
    /// <param name="from">The frame's current position.</param>
    /// <param name="to">Where it should end up.</param>
    public void MoveFrameTo(int from, int to)
    {
        if (_workspace is not { } workspace || from == to)
        {
            return;
        }

        int target = Math.Clamp(to, 0, workspace.Document.Frames.Count - 1);
        workspace.History.Execute(new MoveFrameCommand("Reorder Frame", workspace.Document, from, target));
        workspace.GoToFrame(target);
    }

    /// <summary>Applies the frame rate to every frame of the current animation.</summary>
    [RelayCommand]
    private void ApplyFrameRate()
    {
        if (_workspace is not { } workspace)
        {
            return;
        }

        int duration = Math.Max(1, (int)Math.Round(1000.0 / Math.Clamp(FrameRate, 1, 120)));

        // The animation being worked on, or the whole timeline when none is chosen — the same
        // rule playback follows, so the two never disagree about what "the animation" means.
        IEnumerable<Frame> frames = workspace.ActiveTag is { } tag
            ? workspace.Document.Frames.Skip(tag.From).Take(tag.FrameCount)
            : workspace.Document.Frames;

        SetFrameDurationsCommand command = new(
            "Set Frame Rate",
            frames.Select(f => (f, f.DurationMilliseconds, duration)));

        if (command.HasChanges)
        {
            workspace.History.Execute(command);
        }
    }

    /// <summary>Changes how long one frame is held.</summary>
    /// <param name="frame">The header whose frame is being retimed.</param>
    /// <param name="milliseconds">The new hold time.</param>
    public void SetFrameDuration(TimelineFrameViewModel frame, int milliseconds)
    {
        if (_workspace is not { } workspace || frame.Frame.DurationMilliseconds == milliseconds)
        {
            return;
        }

        workspace.History.Execute(new SetFrameDurationsCommand(
            "Set Frame Duration",
            [(frame.Frame, frame.Frame.DurationMilliseconds, milliseconds)]));
    }

    // ---------------------------------------------------------------------------------
    // Tag commands
    // ---------------------------------------------------------------------------------

    /// <summary>Names an animation covering the whole timeline.</summary>
    /// <remarks>
    /// The whole timeline rather than "from wherever the playhead is", because the playhead is
    /// usually sitting on the last frame at the moment an artist finishes a motion and goes to
    /// name it — and a tag covering that one frame is never what they meant. Narrowing the
    /// range afterwards is two numbers in the strip below.
    /// </remarks>
    [RelayCommand]
    private void AddTag()
    {
        if (_workspace is not { } workspace)
        {
            return;
        }

        int to = workspace.Document.Frames.Count - 1;

        AnimationTag tag = new(Guid.NewGuid(), $"Animation {workspace.Document.Tags.Count + 1}", 0, to);
        workspace.History.Execute(new AddTagCommand("Add Animation", workspace.Document, tag));
        workspace.ActiveTag = tag;
    }

    /// <summary>Removes the animation being worked on.</summary>
    [RelayCommand]
    private void DeleteTag()
    {
        if (_workspace is not { ActiveTag: { } tag } workspace)
        {
            return;
        }

        workspace.History.Execute(new RemoveTagCommand("Delete Animation", workspace.Document, tag));
        workspace.ActiveTag = null;
    }

    /// <summary>Stops working on a named animation and treats the whole timeline as one.</summary>
    [RelayCommand]
    private void ClearActiveTag()
    {
        if (_workspace is { } workspace)
        {
            workspace.ActiveTag = null;
        }
    }

    /// <summary>Sets an animation's range to the frames currently in view.</summary>
    /// <param name="tag">The animation to change.</param>
    /// <param name="from">The first frame, inclusive.</param>
    /// <param name="to">The last frame, inclusive.</param>
    public void SetTagRange(AnimationTag tag, int from, int to)
    {
        if (_workspace is not { } workspace)
        {
            return;
        }

        AnimationTag before = tag.CloneWith(tag.Id);
        AnimationTag after = tag.CloneWith(tag.Id);
        after.From = Math.Clamp(from, 0, workspace.Document.Frames.Count - 1);
        after.To = Math.Clamp(to, after.From, workspace.Document.Frames.Count - 1);

        ChangeTagCommand command = new("Change Animation", tag, before, after);
        if (command.HasChanges)
        {
            workspace.History.Execute(command);
        }
    }

    /// <summary>
    /// Applies a range typed into the editors, and reports back what it was allowed to be.
    /// </summary>
    /// <remarks>
    /// The properties are raised again unconditionally, because a rejected value has to be
    /// pushed back into the editor it was typed into. Without that, a field left showing 40
    /// would disagree with the tag it is bound to.
    /// </remarks>
    private void SetActiveTagRange(int from, int to)
    {
        if (ActiveTag is not { } tag)
        {
            return;
        }

        SetTagRange(tag, from, to);

        OnPropertyChanged(nameof(ActiveTagFrom));
        OnPropertyChanged(nameof(ActiveTagTo));
    }

    /// <summary>Records a change made directly to an animation, so it can be undone.</summary>
    /// <param name="tag">The animation that changed.</param>
    /// <param name="before">A copy of it as it was.</param>
    public void CommitTagChange(AnimationTag tag, AnimationTag before)
    {
        if (_workspace is not { } workspace)
        {
            return;
        }

        ChangeTagCommand command = new("Change Animation", tag, before, tag);
        if (command.HasChanges)
        {
            workspace.History.Push(command);
        }
    }

    // ---------------------------------------------------------------------------------
    // Rebuilding
    // ---------------------------------------------------------------------------------

    private void MoveFrame(int delta)
    {
        if (_workspace is not { } workspace)
        {
            return;
        }

        int from = workspace.ActiveFrameIndex;
        MoveFrameTo(from, from + delta);
    }

    private void Rebuild()
    {
        _isRebuilding = true;

        try
        {
            Rows.Clear();
            Frames.Clear();
            Tags.Clear();

            if (_workspace is not { } workspace)
            {
                return;
            }

            PixelDocument document = workspace.Document;

            for (int i = 0; i < document.Frames.Count; i++)
            {
                Frames.Add(new TimelineFrameViewModel(document.Frames[i], i, GoToFrame));
            }

            // Top-most layer first, matching the layers panel. The model stores them
            // bottom-first, and reversing here is the same single reversal that panel makes.
            foreach (LayerNode layer in document.Root.Children.Reverse())
            {
                List<TimelineCellViewModel> cells = [];

                for (int i = 0; i < document.Frames.Count; i++)
                {
                    TimelineCellViewModel cell = new(layer, i, SelectCell)
                    {
                        HasContent = document.Cels.Find(layer.Id, document.Frames[i].Id) is not null,
                    };

                    cells.Add(cell);
                }

                Rows.Add(new TimelineRowViewModel(layer, cells));
            }

            foreach (AnimationTag tag in document.Tags)
            {
                Tags.Add(new TimelineTagViewModel(tag, SelectTag));
            }
        }
        finally
        {
            _isRebuilding = false;
        }

        RefreshSelection();
        QueueThumbnails(all: true);

        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(LastFrameIndex));
        OnPropertyChanged(nameof(ActiveTag));
        OnPropertyChanged(nameof(ActiveTagFrom));
        OnPropertyChanged(nameof(ActiveTagTo));
    }

    private void RefreshSelection()
    {
        if (_workspace is not { } workspace)
        {
            return;
        }

        int frameIndex = workspace.ActiveFrameIndex;
        LayerNode? activeLayer = workspace.ActiveLayer;

        foreach (TimelineFrameViewModel frame in Frames)
        {
            frame.IsCurrent = frame.Index == frameIndex;
            frame.DurationMilliseconds = frame.Frame.DurationMilliseconds;
        }

        foreach (TimelineRowViewModel row in Rows)
        {
            bool isCurrentLayer = ReferenceEquals(row.Layer, activeLayer);
            row.IsCurrentLayer = isCurrentLayer;

            foreach (TimelineCellViewModel cell in row.Cells)
            {
                cell.IsCurrentFrame = cell.FrameIndex == frameIndex;
                cell.IsCurrentLayer = isCurrentLayer;
            }
        }

        foreach (TimelineTagViewModel tag in Tags)
        {
            tag.IsActive = ReferenceEquals(tag.Tag, workspace.ActiveTag);
        }

        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(LastFrameIndex));
    }

    private void SelectCell(TimelineCellViewModel cell)
    {
        if (_workspace is not { } workspace)
        {
            return;
        }

        Playback.Stop();
        workspace.ActiveLayer = cell.Layer;
        workspace.GoToFrame(cell.FrameIndex);
    }

    private void GoToFrame(int index)
    {
        Playback.Stop();
        _workspace?.GoToFrame(index);
    }

    private void SelectTag(TimelineTagViewModel tag)
    {
        if (_workspace is not { } workspace)
        {
            return;
        }

        // Clicking the active animation again turns it off, which is the quickest way back to
        // seeing the whole timeline.
        workspace.ActiveTag = ReferenceEquals(workspace.ActiveTag, tag.Tag) ? null : tag.Tag;
        workspace.GoToFrame(tag.Tag.From);
    }

    // ---------------------------------------------------------------------------------
    // Thumbnails
    // ---------------------------------------------------------------------------------

    private bool _thumbnailsAllDirty;
    private readonly HashSet<int> _dirtyThumbnails = [];

    private void QueueThumbnails(bool all)
    {
        if (all)
        {
            _thumbnailsAllDirty = true;
        }
        else if (_workspace is { } workspace)
        {
            _dirtyThumbnails.Add(workspace.ActiveFrameIndex);
        }

        _thumbnailTimer.Start();
    }

    private void OnThumbnailTimerTick(object? sender, EventArgs e)
    {
        _thumbnailTimer.Stop();

        if (_workspace is not { } workspace)
        {
            return;
        }

        PixelDocument document = workspace.Document;

        foreach (TimelineFrameViewModel frame in Frames)
        {
            if (!_thumbnailsAllDirty && !_dirtyThumbnails.Contains(frame.Index))
            {
                continue;
            }

            if (frame.Index >= document.Frames.Count)
            {
                continue;
            }

            Bitmap? previous = frame.Thumbnail;

            // The document root is a group, so rendering it flattens every visible layer for
            // that frame — which is exactly the preview a timeline wants.
            frame.Thumbnail = LayerThumbnailRenderer.Render(document, document.Root, document.Frames[frame.Index]);

            previous?.Dispose();
        }

        _thumbnailsAllDirty = false;
        _dirtyThumbnails.Clear();
    }

    // ---------------------------------------------------------------------------------
    // Change plumbing
    // ---------------------------------------------------------------------------------

    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DocumentWorkspace.ActiveFrame):
            case nameof(DocumentWorkspace.ActiveLayer):
                RefreshSelection();
                break;

            case nameof(DocumentWorkspace.ActiveTag):
                RefreshSelection();
                OnPropertyChanged(nameof(ActiveTag));
                OnPropertyChanged(nameof(ActiveTagFrom));
                OnPropertyChanged(nameof(ActiveTagTo));
                break;

            default:
                break;
        }
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (_isRebuilding)
        {
            return;
        }

        switch (e.Kind)
        {
            case DocumentChangeKind.Structure:
                Rebuild();
                break;

            case DocumentChangeKind.Pixels:
                // Only the frame being drawn on can have changed, and only its thumbnail
                // needs redrawing. Marking the cell filled matters too: a cel springs into
                // existence on the first stroke.
                MarkActiveCellFilled();
                QueueThumbnails(all: false);
                break;

            default:
                QueueThumbnails(all: true);
                break;
        }
    }

    private void MarkActiveCellFilled()
    {
        if (_workspace is not { ActiveLayer: { } layer } workspace)
        {
            return;
        }

        int index = workspace.ActiveFrameIndex;

        foreach (TimelineRowViewModel row in Rows)
        {
            if (!ReferenceEquals(row.Layer, layer) || index >= row.Cells.Count)
            {
                continue;
            }

            row.Cells[index].HasContent =
                workspace.Document.Cels.Find(layer.Id, workspace.Document.Frames[index].Id) is not null;

            return;
        }
    }
}
