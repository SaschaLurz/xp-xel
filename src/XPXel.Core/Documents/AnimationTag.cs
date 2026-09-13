using XPXel.Core.ComponentModel;
using XPXel.Core.Primitives;

namespace XPXel.Core.Documents;

/// <summary>The order a tagged range of frames is played back in.</summary>
public enum LoopDirection
{
    /// <summary>First frame to last, then round again.</summary>
    Forward = 0,

    /// <summary>Last frame to first, then round again.</summary>
    Reverse = 1,

    /// <summary>
    /// Forward to the end, then back to the start, without repeating the two end frames.
    /// </summary>
    /// <remarks>
    /// The end frames are played once per cycle rather than twice, which is what makes a
    /// ping-pong loop read as a smooth back-and-forth instead of stuttering at each turn.
    /// </remarks>
    PingPong = 2,
}

/// <summary>
/// A named run of frames within a document's timeline — "idle", "walk", "attack" — together
/// with how it loops.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why one document holds many animations.</b> A game character is authored as one set of
/// artwork with several motions cut out of it. Splitting those into separate files means the
/// palette, the canvas size and the layer structure can drift apart between them, and it is
/// the layer structure especially that must not: a walk and an idle built on differently
/// named layers cannot share a colour change. Tags keep one file as the unit of a character
/// and let export produce one sheet per motion.
/// </para>
/// <para>
/// <b>Ranges are positional, unlike everything else in the model.</b> Cels are keyed by frame
/// <em>identity</em> precisely so reordering the timeline does not disturb them. A tag is the
/// opposite case: it means "this contiguous stretch of the timeline", so it is stored as a
/// pair of indices and maintained by <see cref="PixelDocument"/> as frames are inserted and
/// removed. Inserting a frame inside a tag extends it, which is what an artist adding an
/// in-between expects; inserting before it shifts it along.
/// </para>
/// </remarks>
public sealed class AnimationTag : ObservableModel
{
    private string _name;
    private int _from;
    private int _to;
    private LoopDirection _direction = LoopDirection.Forward;
    private PixelColor? _color;
    private int _repeatCount;
    private bool _coversWholeTimeline;

    /// <summary>Initialises a tag.</summary>
    /// <param name="id">The tag's stable identity, persisted in project files.</param>
    /// <param name="name">The animation's name, as an engine will see it.</param>
    /// <param name="from">The first frame index, inclusive.</param>
    /// <param name="to">The last frame index, inclusive.</param>
    public AnimationTag(Guid id, string name, int from, int to)
    {
        Id = id;
        _name = string.IsNullOrWhiteSpace(name) ? "Animation" : name;
        _from = Math.Max(0, Math.Min(from, to));
        _to = Math.Max(_from, to);
    }

    /// <summary>The tag's stable identity.</summary>
    public Guid Id { get; }

    /// <summary>
    /// The animation's name. This becomes a file name on export, so it is what the artist
    /// will type into an engine.
    /// </summary>
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, string.IsNullOrWhiteSpace(value) ? "Animation" : value.Trim());
    }

    /// <summary>The first frame of the range, inclusive.</summary>
    public int From
    {
        get => _from;
        set
        {
            if (SetProperty(ref _from, Math.Max(0, value)) && _to < _from)
            {
                To = _from;
            }

            OnPropertyChanged(nameof(FrameCount));
        }
    }

    /// <summary>The last frame of the range, inclusive.</summary>
    public int To
    {
        get => _to;
        set
        {
            if (SetProperty(ref _to, Math.Max(0, value)) && _to < _from)
            {
                From = _to;
            }

            OnPropertyChanged(nameof(FrameCount));
        }
    }

    /// <summary>
    /// Whether the animation is the whole timeline, however long the timeline becomes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>On for every animation the editor makes, and the reason is a trap.</b> A range is a
    /// pair of numbers fixed at the moment it was set, so an artist who names an animation over
    /// four frames and then draws six more finds that pressing play still shows them the first
    /// four — with nothing on screen to say why, because the range they set is exactly the range
    /// they are getting. Following the timeline is what almost everyone means almost all of the
    /// time, so it is the default, and picking a range by hand is what turning it off is for.
    /// </para>
    /// <para>
    /// <see cref="From"/> and <see cref="To"/> stay true while this is set: the document keeps
    /// them level with the timeline whenever a frame is added or removed. Everything that reads
    /// a range — playback, onion skinning, every exporter — therefore needs to know nothing
    /// about this flag.
    /// </para>
    /// </remarks>
    public bool CoversWholeTimeline
    {
        get => _coversWholeTimeline;
        set => SetProperty(ref _coversWholeTimeline, value);
    }

    /// <summary>How the range loops.</summary>
    public LoopDirection Direction
    {
        get => _direction;
        set => SetProperty(ref _direction, value);
    }

    /// <summary>
    /// How many times the animation plays before stopping, or 0 for an endless loop.
    /// </summary>
    /// <remarks>
    /// Carried because engines and sheet metadata often want it — a hit reaction plays once,
    /// an idle plays forever — even though the editor's own preview always loops.
    /// </remarks>
    public int RepeatCount
    {
        get => _repeatCount;
        set => SetProperty(ref _repeatCount, Math.Max(0, value));
    }

    /// <summary>
    /// An optional colour used to draw the tag's bar on the timeline, or
    /// <see langword="null"/> for the default.
    /// </summary>
    /// <remarks>
    /// Purely organisational, like <see cref="LayerNode.TagColor"/>: it lets an artist pick
    /// out one motion in a timeline holding six. Nothing about playback or export reads it.
    /// </remarks>
    public PixelColor? Color
    {
        get => _color;
        set => SetProperty(ref _color, value);
    }

    /// <summary>How many frames the range covers.</summary>
    public int FrameCount => (_to - _from) + 1;

    /// <summary>Determines whether a frame index falls inside this tag.</summary>
    /// <param name="frameIndex">The index to test.</param>
    /// <returns><see langword="true"/> if the frame is part of this animation.</returns>
    public bool Contains(int frameIndex) => frameIndex >= _from && frameIndex <= _to;

    /// <summary>Creates an independent copy under a new identity.</summary>
    /// <param name="newId">The identity for the copy.</param>
    /// <returns>A new tag with the same values.</returns>
    public AnimationTag CloneWith(Guid newId) => new(newId, Name, From, To)
    {
        Direction = Direction,
        RepeatCount = RepeatCount,
        Color = Color,
        CoversWholeTimeline = CoversWholeTimeline,
    };

    /// <inheritdoc />
    public override string ToString() => $"{Name} [{From}..{To}] {Direction}";
}
