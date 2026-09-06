using XPXel.Core.Animation;
using XPXel.Core.Documents;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Rendering;

namespace XPXel.Formats.Animation;

/// <summary>
/// One animation, already flattened into finished images at export size.
/// </summary>
/// <remarks>
/// <para>
/// Every exporter needs the same first step — work out which frames belong to the animation,
/// composite each one, and scale it — and every exporter would otherwise do it slightly
/// differently. Doing it once here means a sheet, a GIF and a frame sequence exported from
/// the same document contain pixel-identical images, which is exactly the property that makes
/// them interchangeable in an engine.
/// </para>
/// <para>
/// <b>Frames are composited eagerly.</b> A clip holds finished buffers rather than a lazy
/// sequence, because the sprite sheet exporter needs the whole set before it can decide how
/// large the sheet is, and compositing twice would double the cost of the slowest step.
/// </para>
/// </remarks>
public sealed class AnimationClip
{
    private AnimationClip(string name, IReadOnlyList<PixelBuffer> images, IReadOnlyList<int> durations, AnimationTag? tag)
    {
        Name = name;
        Images = images;
        Durations = durations;
        Tag = tag;
    }

    /// <summary>The animation's name, used to build file names.</summary>
    public string Name { get; }

    /// <summary>The flattened frames, at export scale, in playback order.</summary>
    public IReadOnlyList<PixelBuffer> Images { get; }

    /// <summary>How long each frame is held, in milliseconds.</summary>
    public IReadOnlyList<int> Durations { get; }

    /// <summary>The tag this clip came from, or <see langword="null"/> for a whole timeline.</summary>
    public AnimationTag? Tag { get; }

    /// <summary>How many frames the clip has.</summary>
    public int Count => Images.Count;

    /// <summary>The size of one frame, or an empty size for a clip with no frames.</summary>
    public ImageSize FrameSize => Images.Count == 0 ? default : Images[0].Size;

    /// <summary>The total running time in milliseconds.</summary>
    public int TotalDurationMilliseconds => Durations.Sum();

    /// <summary>
    /// Builds the clips an export should produce: one per tag, one for a chosen tag, or a
    /// single clip covering the whole timeline.
    /// </summary>
    /// <param name="document">The document to render.</param>
    /// <param name="options">How the export is configured.</param>
    /// <returns>The clips, never empty.</returns>
    /// <exception cref="ImageFormatException">The document has no frames to export.</exception>
    public static IReadOnlyList<AnimationClip> Build(PixelDocument document, AnimationExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        if (document.Frames.Count == 0)
        {
            throw new ImageFormatException("The document has no frames to export.");
        }

        List<AnimationTag?> tags = SelectTags(document, options);

        // One compositor for the whole export: it keeps scratch buffers sized to the
        // document, and building a new one per clip would throw them away each time.
        LayerCompositor compositor = new();
        List<AnimationClip> clips = [];

        foreach (AnimationTag? tag in tags)
        {
            AnimationSequence sequence = AnimationSequence.Build(document, tag, options.ExpandLoop);
            if (sequence.IsEmpty)
            {
                continue;
            }

            List<PixelBuffer> images = [];
            List<int> durations = [];

            for (int step = 0; step < sequence.Count; step++)
            {
                PixelBuffer flattened = new(document.Size);
                compositor.Compose(document, sequence.FrameAt(step), flattened);

                images.Add(options.Scale > 1 ? ImageScaling.ScaleNearestNeighbour(flattened, options.Scale) : flattened);
                durations.Add(sequence.DurationAt(step));
            }

            clips.Add(new AnimationClip(
                AnimationExportOptions.SanitiseFileName(tag?.Name ?? options.BaseName),
                images,
                durations,
                tag));
        }

        if (clips.Count == 0)
        {
            throw new ImageFormatException("The selected animation contains no frames.");
        }

        return clips;
    }

    /// <summary>
    /// Decides which animations the export covers.
    /// </summary>
    /// <remarks>
    /// A <see langword="null"/> entry means "the whole timeline as one unnamed animation",
    /// which is what a document with no tags exports as. That fallback is deliberate: a
    /// simple flipbook should not have to declare a tag before it can be exported.
    /// </remarks>
    private static List<AnimationTag?> SelectTags(PixelDocument document, AnimationExportOptions options)
    {
        if (options.Tag is { } chosen)
        {
            return [chosen];
        }

        if (document.Tags.Count == 0)
        {
            return [null];
        }

        if (options.SeparateFilePerTag || options.Layout == SpriteSheetLayout.RowPerAnimation)
        {
            return [.. document.Tags.Cast<AnimationTag?>()];
        }

        return [null];
    }
}
