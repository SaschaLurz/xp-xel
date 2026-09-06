using XPXel.Core.Documents;
using XPXel.Formats.Gif;
using XPXel.Formats.Png;

namespace XPXel.Formats.Animation;

/// <summary>
/// Writes an animation as an animated GIF.
/// </summary>
/// <remarks>
/// For showing the animation to a person rather than feeding it to an engine: a GIF plays in
/// a browser, a chat window and an issue tracker with nothing installed. It is the closest
/// thing to "a video of it" that can be produced without a native encoder — see
/// <see cref="ApngAnimationExporter"/> for the version that keeps every colour.
/// </remarks>
public sealed class GifAnimationExporter : IAnimationExporter
{
    private static readonly FormatDescriptor GifDescriptor = new(
        "xpxel.format.gif",
        "Animated GIF",
        [".gif"],
        "image/gif",
        FormatCapabilities.Animation | FormatCapabilities.IndexedColor);

    /// <inheritdoc />
    public FormatDescriptor Descriptor => GifDescriptor;

    /// <inheritdoc />
    public string Summary =>
        "A single looping image that plays anywhere — browsers, chat, issue trackers. "
        + "Limited to 256 colours and hard-edged transparency, which suits flat pixel art "
        + "but not soft brush work.";

    /// <inheritdoc />
    public bool ProducesSingleFile => true;

    /// <inheritdoc />
    public IReadOnlyList<ExportArtifact> Export(PixelDocument document, AnimationExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        return AnimatedImageExport.Write(
            document,
            options,
            ".gif",
            "image/gif",
            (stream, clip) => GifEncoder.Encode(stream, clip.Images, clip.Durations, LoopCountOf(clip)));
    }

    private static int LoopCountOf(AnimationClip clip) => clip.Tag?.RepeatCount ?? 0;
}

/// <summary>
/// Writes an animation as an animated PNG.
/// </summary>
/// <remarks>
/// Everything the GIF gives, without the losses: full colour and real transparency, because
/// an APNG is an ordinary PNG with animation chunks bolted on. The trade is reach — modern
/// browsers all play it, older image tools show only the first frame.
/// </remarks>
public sealed class ApngAnimationExporter : IAnimationExporter
{
    private static readonly FormatDescriptor ApngDescriptor = new(
        "xpxel.format.apng",
        "Animated PNG",
        [".apng", ".png"],
        "image/apng",
        FormatCapabilities.Animation | FormatCapabilities.Alpha);

    /// <inheritdoc />
    public FormatDescriptor Descriptor => ApngDescriptor;

    /// <inheritdoc />
    public string Summary =>
        "A single looping image like a GIF, but with every colour and every level of "
        + "transparency intact. Plays in modern browsers; older tools show the first frame.";

    /// <inheritdoc />
    public bool ProducesSingleFile => true;

    /// <inheritdoc />
    public IReadOnlyList<ExportArtifact> Export(PixelDocument document, AnimationExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        return AnimatedImageExport.Write(
            document,
            options,
            ".apng",
            "image/apng",
            (stream, clip) => ApngEncoder.Encode(stream, clip.Images, clip.Durations, clip.Tag?.RepeatCount ?? 0));
    }
}

/// <summary>
/// The part the animated image formats have in common: build the clips, and write one file
/// per clip.
/// </summary>
/// <remarks>
/// GIF and APNG differ only in their encoder. Sharing everything else keeps their file naming
/// and their handling of multiple animations identical, so that switching format never
/// silently changes what you get.
/// </remarks>
internal static class AnimatedImageExport
{
    public static IReadOnlyList<ExportArtifact> Write(
        PixelDocument document,
        AnimationExportOptions options,
        string extension,
        string mimeType,
        Action<Stream, AnimationClip> encode)
    {
        // A single image can only ever hold one animation, so a document with several tags
        // always produces one file each — regardless of what the caller asked for. The loop
        // is expanded because neither format can express a direction: the frames have to
        // arrive in the order they are meant to be seen.
        AnimationExportOptions perClip = new()
        {
            BaseName = options.BaseName,
            Scale = options.Scale,
            Tag = options.Tag,
            SeparateFilePerTag = true,
            Layout = options.Layout,
            Padding = 0,
            IncludeMetadata = false,
            ExpandLoop = true,
        };

        IReadOnlyList<AnimationClip> clips = AnimationClip.Build(document, perClip);
        List<ExportArtifact> artifacts = [];

        foreach (AnimationClip clip in clips)
        {
            using MemoryStream stream = new();
            encode(stream, clip);

            string name = clips.Count > 1
                ? $"{options.BaseName}_{clip.Name}{extension}"
                : $"{options.BaseName}{extension}";

            artifacts.Add(new ExportArtifact(name, stream.ToArray(), mimeType));
        }

        return artifacts;
    }
}
