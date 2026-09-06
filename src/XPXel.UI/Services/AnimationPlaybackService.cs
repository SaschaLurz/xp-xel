using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XPXel.Core.Animation;
using XPXel.Core.Documents;
using XPXel.Core.Editing;

namespace XPXel.UI.Services;

/// <summary>
/// Plays the active document's animation by walking its active frame forward in real time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Playing moves the editor's own current frame.</b> There is no second, separate
/// animation running somewhere — the canvas, the timeline and the preview panel all show the
/// active frame, so advancing it animates every one of them at once and they cannot disagree
/// about what is playing. It also means the artist can stop on any frame and immediately draw
/// on it.
/// </para>
/// <para>
/// <b>One service, shared by the panels.</b> The timeline and the preview both need transport
/// controls, and duplicating the clock in each would give two timers drifting apart. They bind
/// to this instead.
/// </para>
/// <para>
/// <b>Elapsed time comes from a stopwatch, not from counting ticks.</b> A dispatcher timer
/// fires late whenever the UI thread is busy — which, in a paint program, is whenever the
/// artist is drawing — and counting its ticks would make the animation slow down exactly when
/// it is being watched. Measuring real elapsed time keeps playback at the speed it was
/// authored at and simply drops frames under load, which is the right trade.
/// </para>
/// </remarks>
public sealed partial class AnimationPlaybackService : ObservableObject, IDisposable
{
    /// <summary>How often the player checks whether the frame should change.</summary>
    /// <remarks>
    /// About 60 times a second. The frame rate of the animation is unrelated to this: a
    /// 12 fps animation still ticks at 60 Hz and simply changes frame every fifth tick, which
    /// is what keeps a 30 ms hold from being rounded up to 60.
    /// </remarks>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(16);

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();

    private DocumentWorkspace? _workspace;
    private AnimationSequence? _sequence;
    private double _startOffsetMilliseconds;

    [ObservableProperty]
    private bool _isPlaying;

    /// <summary>
    /// Whether playback runs the whole timeline rather than only the animation being worked
    /// on.
    /// </summary>
    [ObservableProperty]
    private bool _playsWholeTimeline;

    /// <summary>Initialises the service.</summary>
    public AnimationPlaybackService()
    {
        _timer = new DispatcherTimer(TickInterval, DispatcherPriority.Render, OnTick);
    }

    /// <summary>The document being played, or <see langword="null"/> when none is open.</summary>
    public DocumentWorkspace? Workspace => _workspace;

    /// <summary>Gets a value indicating whether there is more than one frame to play.</summary>
    public bool CanPlay => _workspace is { } workspace && workspace.Document.Frames.Count > 1;

    /// <summary>Points the player at a different document, stopping anything already playing.</summary>
    /// <param name="workspace">The document to play.</param>
    public void Attach(DocumentWorkspace? workspace)
    {
        if (ReferenceEquals(_workspace, workspace))
        {
            return;
        }

        Stop();

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

        _sequence = null;
        OnPropertyChanged(nameof(CanPlay));
    }

    /// <summary>Starts playing from the current frame.</summary>
    [RelayCommand]
    public void Play()
    {
        if (!CanPlay || IsPlaying)
        {
            return;
        }

        _sequence = BuildSequence();
        if (_sequence.IsEmpty)
        {
            return;
        }

        // Starting from where the artist left the playhead rather than from the beginning: an
        // artist who has just scrubbed to the middle of a run cycle wants to see it from
        // there.
        _startOffsetMilliseconds = TimeOfCurrentFrame(_sequence);

        _clock.Restart();
        _timer.Start();
        IsPlaying = true;
    }

    /// <summary>Stops playing, leaving the playhead where it is.</summary>
    [RelayCommand]
    public void Stop()
    {
        if (!IsPlaying)
        {
            return;
        }

        _timer.Stop();
        _clock.Stop();
        IsPlaying = false;
    }

    /// <summary>Starts or stops playing.</summary>
    [RelayCommand]
    public void Toggle()
    {
        if (IsPlaying)
        {
            Stop();
        }
        else
        {
            Play();
        }
    }

    /// <summary>Moves one frame along the animation, wrapping at its ends.</summary>
    /// <param name="delta">How many frames to move; negative goes back.</param>
    public void Step(int delta)
    {
        if (_workspace is not { } workspace)
        {
            return;
        }

        Stop();

        AnimationSequence sequence = BuildSequence();
        if (sequence.IsEmpty)
        {
            return;
        }

        int current = CurrentStep(sequence);
        int next = ((current + delta) % sequence.Count + sequence.Count) % sequence.Count;

        workspace.GoToFrame(sequence.SourceIndexAt(next));
    }

    /// <summary>Moves back one frame, wrapping at the start.</summary>
    [RelayCommand]
    public void StepBack() => Step(-1);

    /// <summary>Moves on one frame, wrapping at the end.</summary>
    [RelayCommand]
    public void StepForward() => Step(1);

    /// <summary>Moves to the first frame of the animation.</summary>
    [RelayCommand]
    public void GoToStart()
    {
        Stop();

        AnimationSequence sequence = BuildSequence();
        if (!sequence.IsEmpty)
        {
            _workspace?.GoToFrame(sequence.SourceIndexAt(0));
        }
    }

    /// <summary>Moves to the last frame of the animation.</summary>
    [RelayCommand]
    public void GoToEnd()
    {
        Stop();

        AnimationSequence sequence = BuildSequence();
        if (!sequence.IsEmpty)
        {
            _workspace?.GoToFrame(sequence.SourceIndexAt(sequence.Count - 1));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        Attach(null);
    }

    partial void OnPlaysWholeTimelineChanged(bool value)
    {
        // The range changed underneath the player, so an in-flight sequence is stale.
        _sequence = null;

        if (IsPlaying)
        {
            Stop();
            Play();
        }
    }

    private AnimationSequence BuildSequence()
    {
        if (_workspace is not { } workspace)
        {
            return AnimationSequence.Build(PixelDocument.CreateNew(new Core.Primitives.ImageSize(1, 1)), null);
        }

        AnimationTag? tag = PlaysWholeTimeline ? null : workspace.ActiveTag;
        return AnimationSequence.Build(workspace.Document, tag);
    }

    /// <summary>The playback step showing the frame the artist is currently on.</summary>
    private int CurrentStep(AnimationSequence sequence)
    {
        int frameIndex = _workspace?.ActiveFrameIndex ?? 0;

        for (int i = 0; i < sequence.Count; i++)
        {
            if (sequence.SourceIndexAt(i) == frameIndex)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>Where in the cycle the current frame begins, so playback resumes from it.</summary>
    private double TimeOfCurrentFrame(AnimationSequence sequence)
    {
        int step = CurrentStep(sequence);
        double time = 0;

        for (int i = 0; i < step; i++)
        {
            time += sequence.DurationAt(i);
        }

        return time;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_workspace is not { } workspace || _sequence is not { } sequence || sequence.IsEmpty)
        {
            Stop();
            return;
        }

        double elapsed = _startOffsetMilliseconds + _clock.Elapsed.TotalMilliseconds;
        int step = sequence.StepAtTime(elapsed);
        int frameIndex = sequence.SourceIndexAt(step);

        if (frameIndex != workspace.ActiveFrameIndex)
        {
            workspace.GoToFrame(frameIndex);
        }
    }

    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentWorkspace.ActiveTag))
        {
            _sequence = null;

            if (IsPlaying)
            {
                Stop();
                Play();
            }
        }
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (e.Kind != DocumentChangeKind.Structure)
        {
            return;
        }

        // Frames added or removed mid-playback invalidate the cached order, and a sequence
        // holding a frame the document no longer has would keep showing it.
        _sequence = IsPlaying ? BuildSequence() : null;
        OnPropertyChanged(nameof(CanPlay));
    }
}
