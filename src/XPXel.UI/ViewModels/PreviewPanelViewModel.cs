using System.ComponentModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.UI.Rendering;
using XPXel.UI.Services;

namespace XPXel.UI.ViewModels;

/// <summary>
/// The preview panel: the animation playing at its real timing, at the size it will be seen
/// at in a game.
/// </summary>
/// <remarks>
/// <para>
/// <b>It shows the same frame the canvas does.</b> Playback moves the document's active
/// frame, so the preview follows automatically and cannot drift out of step with the canvas
/// or the timeline. What it adds is the thing the canvas cannot give: the artwork at 1× or 2×,
/// without the pixel grid, the onion skin ghosts or the selection outline on top of it — which
/// is the only way to judge whether a walk cycle actually reads.
/// </para>
/// <para>
/// <b>Why it renders its own bitmap rather than sharing the canvas's.</b> The canvas composites
/// with the onion skin ghosts blended in. Showing that here would defeat the point of the
/// panel, so it keeps its own clean composite of the frame.
/// </para>
/// </remarks>
public sealed partial class PreviewPanelViewModel : ViewModelBase, IDisposable
{
    /// <summary>How long to wait after a pixel change before re-rendering.</summary>
    /// <remarks>
    /// Short enough to feel live while drawing, long enough that a fast stroke does not
    /// composite the document once per pointer sample. Frame changes are not throttled at all
    /// — during playback they must land immediately or the preview stutters.
    /// </remarks>
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(60);

    private readonly DispatcherTimer _refreshTimer;

    private DocumentWorkspace? _workspace;

    [ObservableProperty]
    private Bitmap? _image;

    /// <summary>The magnification the preview is drawn at.</summary>
    [ObservableProperty]
    private int _zoom = 2;

    /// <summary>Whether the transparency checkerboard is drawn behind the artwork.</summary>
    /// <remarks>
    /// On by default, because a sprite's transparent regions are part of what has to be
    /// judged. Turning it off is how an artist checks the silhouette against a flat ground.
    /// </remarks>
    [ObservableProperty]
    private bool _showCheckerboard = true;

    /// <summary>Initialises the panel.</summary>
    /// <param name="playback">The shared transport, whose buttons this panel repeats.</param>
    public PreviewPanelViewModel(AnimationPlaybackService playback)
    {
        ArgumentNullException.ThrowIfNull(playback);

        Playback = playback;
        _refreshTimer = new DispatcherTimer(RefreshDelay, DispatcherPriority.Background, OnRefreshTick);
    }

    /// <summary>The shared transport controls.</summary>
    public AnimationPlaybackService Playback { get; }

    /// <summary>The zoom levels offered.</summary>
    public static IReadOnlyList<int> ZoomLevels { get; } = [1, 2, 3, 4, 6, 8];

    /// <summary>The size the preview occupies on screen, at the current zoom.</summary>
    public double DisplayWidth => (_workspace?.Document.Size.Width ?? 0) * Zoom;

    /// <summary>The height the preview occupies on screen, at the current zoom.</summary>
    public double DisplayHeight => (_workspace?.Document.Size.Height ?? 0) * Zoom;

    /// <summary>A short description of what is playing, for the panel's caption.</summary>
    public string CaptionText
    {
        get
        {
            if (_workspace is not { } workspace)
            {
                return "No document";
            }

            string animation = workspace.ActiveTag?.Name ?? "Whole timeline";
            return $"{animation} — frame {workspace.ActiveFrameIndex + 1} of {workspace.Document.Frames.Count}";
        }
    }

    /// <summary>Points the panel at a different document, or at none.</summary>
    /// <param name="workspace">The document to preview.</param>
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

        Refresh();

        OnPropertyChanged(nameof(DisplayWidth));
        OnPropertyChanged(nameof(DisplayHeight));
        OnPropertyChanged(nameof(CaptionText));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _refreshTimer.Stop();
        Attach(null);

        Image?.Dispose();
        Image = null;
    }

    /// <summary>Sets the preview magnification, used by the zoom buttons.</summary>
    /// <param name="zoom">The magnification to apply.</param>
    [RelayCommand]
    private void SetZoom(int zoom) => Zoom = Math.Clamp(zoom, 1, 16);

    partial void OnZoomChanged(int value)
    {
        OnPropertyChanged(nameof(DisplayWidth));
        OnPropertyChanged(nameof(DisplayHeight));
    }

    private void Refresh()
    {
        if (_workspace is not { } workspace)
        {
            Image?.Dispose();
            Image = null;
            return;
        }

        Bitmap? previous = Image;

        // The document root is a group, so rendering it flattens every visible layer for this
        // frame — the same call the timeline's thumbnails use, at full document size.
        Image = FramePreviewRenderer.Render(workspace.Document, workspace.ActiveFrame);

        previous?.Dispose();
        OnPropertyChanged(nameof(CaptionText));
    }

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        Refresh();
    }

    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DocumentWorkspace.ActiveFrame):
                // Never throttled: during playback this is the animation, and delaying it
                // would show the wrong frame for a tenth of a second at a time.
                Refresh();
                break;

            case nameof(DocumentWorkspace.ActiveTag):
                OnPropertyChanged(nameof(CaptionText));
                break;

            default:
                break;
        }
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (e.Kind == DocumentChangeKind.Size)
        {
            OnPropertyChanged(nameof(DisplayWidth));
            OnPropertyChanged(nameof(DisplayHeight));
        }

        _refreshTimer.Start();
    }
}
