using XPXel.Core.Documents;

namespace XPXel.Core.Animation;

/// <summary>
/// A run of frames flattened into the exact order they are played in, with each one's hold
/// time — everything needed to answer "what is on screen at time t".
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the loop direction is expanded rather than interpreted.</b> A ping-pong tag over
/// frames 1–4 plays 1, 2, 3, 4, 3, 2 and then repeats. Working that out inside a playback
/// loop means a direction flag, an edge case at each end, and the same logic written again
/// in the GIF encoder, the sprite sheet exporter and the preview panel — four chances to
/// disagree about whether the end frames repeat. Expanding it once, here, means every
/// consumer walks a plain list.
/// </para>
/// <para>
/// <b>This is deliberately not a player.</b> It holds no clock and starts nothing; it maps a
/// time offset to a frame. The UI owns the timer and asks. That keeps the timing rules
/// testable without a display and without waiting for real time to pass.
/// </para>
/// </remarks>
public sealed class AnimationSequence
{
    private readonly Frame[] _frames;
    private readonly int[] _sourceIndices;

    /// <summary>The accumulated end time of each step, in milliseconds.</summary>
    /// <remarks>
    /// Stored as running totals rather than durations so that finding the frame at a time is
    /// a scan of cumulative values, and so that <see cref="TotalDurationMilliseconds"/> is
    /// simply the last of them.
    /// </remarks>
    private readonly int[] _endTimes;

    private AnimationSequence(Frame[] frames, int[] sourceIndices, int[] endTimes, string name)
    {
        _frames = frames;
        _sourceIndices = sourceIndices;
        _endTimes = endTimes;
        Name = name;
    }

    /// <summary>The animation's name, from its tag, or the document's name for an untagged one.</summary>
    public string Name { get; }

    /// <summary>How many steps the sequence plays before repeating.</summary>
    /// <remarks>
    /// For a ping-pong tag this is larger than the number of distinct frames, because the
    /// return journey is expanded into real steps.
    /// </remarks>
    public int Count => _frames.Length;

    /// <summary>Gets a value indicating whether the sequence has nothing to play.</summary>
    public bool IsEmpty => _frames.Length == 0;

    /// <summary>How long one full cycle lasts, in milliseconds.</summary>
    public int TotalDurationMilliseconds => _endTimes.Length == 0 ? 0 : _endTimes[^1];

    /// <summary>The frame played at a step.</summary>
    /// <param name="step">The position in the playback order.</param>
    /// <returns>The frame to show.</returns>
    public Frame FrameAt(int step) => _frames[Math.Clamp(step, 0, _frames.Length - 1)];

    /// <summary>
    /// The timeline index of the frame played at a step, which a ping-pong sequence visits
    /// more than once.
    /// </summary>
    /// <param name="step">The position in the playback order.</param>
    /// <returns>The frame's index in the document's timeline.</returns>
    public int SourceIndexAt(int step) => _sourceIndices[Math.Clamp(step, 0, _sourceIndices.Length - 1)];

    /// <summary>How long the step at a position is held, in milliseconds.</summary>
    /// <param name="step">The position in the playback order.</param>
    /// <returns>The hold time.</returns>
    public int DurationAt(int step)
    {
        int index = Math.Clamp(step, 0, _endTimes.Length - 1);
        return index == 0 ? _endTimes[0] : _endTimes[index] - _endTimes[index - 1];
    }

    /// <summary>Finds the step playing at a time offset within one cycle.</summary>
    /// <param name="timeMilliseconds">
    /// Milliseconds since the cycle began. Values past the end wrap round, so a caller can
    /// simply accumulate elapsed time and never think about looping.
    /// </param>
    /// <returns>The step index, or 0 for an empty sequence.</returns>
    public int StepAtTime(double timeMilliseconds)
    {
        int total = TotalDurationMilliseconds;
        if (_frames.Length == 0 || total <= 0)
        {
            return 0;
        }

        // Modulo on a double, then floored, so that a negative offset — which a caller
        // stepping backwards can produce — wraps to the end rather than clamping to the start.
        double wrapped = timeMilliseconds % total;
        if (wrapped < 0)
        {
            wrapped += total;
        }

        for (int i = 0; i < _endTimes.Length; i++)
        {
            if (wrapped < _endTimes[i])
            {
                return i;
            }
        }

        return _frames.Length - 1;
    }

    /// <summary>Enumerates the frames in playback order.</summary>
    /// <returns>The frames, with repeats where the sequence revisits one.</returns>
    public IEnumerable<Frame> EnumerateFrames() => _frames;

    /// <summary>
    /// Builds the sequence for a tag, or for the whole timeline when no tag is given.
    /// </summary>
    /// <param name="document">The document to read frames from.</param>
    /// <param name="tag">The animation to play, or <see langword="null"/> for every frame.</param>
    /// <param name="expandLoop">
    /// Whether to expand a ping-pong or reverse loop into its real playback order. Playback
    /// and animated-image export want that; a sprite sheet does not, because a sheet stores
    /// each drawing once and lets the engine decide the order.
    /// </param>
    /// <returns>The sequence, which may be empty if the range falls outside the timeline.</returns>
    public static AnimationSequence Build(PixelDocument document, AnimationTag? tag, bool expandLoop = true)
    {
        ArgumentNullException.ThrowIfNull(document);

        int lastFrame = document.Frames.Count - 1;
        int from = tag is null ? 0 : Math.Clamp(tag.From, 0, Math.Max(0, lastFrame));
        int to = tag is null ? lastFrame : Math.Clamp(tag.To, 0, Math.Max(0, lastFrame));

        if (lastFrame < 0 || to < from)
        {
            return new AnimationSequence([], [], [], tag?.Name ?? "Animation");
        }

        LoopDirection direction = expandLoop ? tag?.Direction ?? LoopDirection.Forward : LoopDirection.Forward;
        List<int> order = BuildOrder(from, to, direction);

        Frame[] frames = new Frame[order.Count];
        int[] sources = new int[order.Count];
        int[] endTimes = new int[order.Count];

        int elapsed = 0;

        for (int i = 0; i < order.Count; i++)
        {
            int sourceIndex = order[i];
            Frame frame = document.Frames[sourceIndex];

            frames[i] = frame;
            sources[i] = sourceIndex;
            elapsed += frame.DurationMilliseconds;
            endTimes[i] = elapsed;
        }

        return new AnimationSequence(frames, sources, endTimes, tag?.Name ?? "Animation");
    }

    private static List<int> BuildOrder(int from, int to, LoopDirection direction)
    {
        List<int> order = [];

        switch (direction)
        {
            case LoopDirection.Reverse:
                for (int i = to; i >= from; i--)
                {
                    order.Add(i);
                }

                break;

            case LoopDirection.PingPong:
                for (int i = from; i <= to; i++)
                {
                    order.Add(i);
                }

                // The two end frames are already in the list, and playing them again on the
                // way back makes the turn visibly stutter — so the return leg stops short of
                // both.
                for (int i = to - 1; i > from; i--)
                {
                    order.Add(i);
                }

                break;

            default:
                for (int i = from; i <= to; i++)
                {
                    order.Add(i);
                }

                break;
        }

        return order;
    }
}
