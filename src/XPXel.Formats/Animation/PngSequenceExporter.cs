using System.Globalization;
using XPXel.Core.Documents;
using XPXel.Core.Imaging;
using XPXel.Formats.Png;

namespace XPXel.Formats.Animation;

/// <summary>
/// Writes an animation as one numbered PNG per frame.
/// </summary>
/// <remarks>
/// <para>
/// The universal fallback. Every engine, every tool and every artist on earth can open a
/// folder of numbered PNGs, which is exactly why it is worth having next to the sprite sheet:
/// when an importer will not cooperate, or a frame has to be handed to somebody else, this
/// always works.
/// </para>
/// <para>
/// <b>Frame numbers are zero-padded to a fixed width.</b> Unpadded numbers sort as text into
/// 1, 10, 11, 2 — which silently scrambles the animation in any tool that ingests a folder in
/// name order, and is one of the most common ways a sprite sequence arrives broken.
/// </para>
/// </remarks>
public sealed class PngSequenceExporter : IAnimationExporter
{
    private static readonly FormatDescriptor SequenceDescriptor = new(
        "xpxel.format.pngsequence",
        "PNG Frame Sequence",
        [".png"],
        "image/png",
        FormatCapabilities.Animation | FormatCapabilities.Alpha);

    /// <inheritdoc />
    public FormatDescriptor Descriptor => SequenceDescriptor;

    /// <inheritdoc />
    public string Summary =>
        "One PNG per frame, numbered so they sort correctly. Works with every engine and "
        + "every tool, and is the safest thing to hand to somebody else.";

    /// <inheritdoc />
    public bool ProducesSingleFile => false;

    /// <inheritdoc />
    public IReadOnlyList<ExportArtifact> Export(PixelDocument document, AnimationExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<AnimationClip> clips = AnimationClip.Build(document, options);
        List<ExportArtifact> artifacts = [];

        // The width is taken from the longest clip so that every file in one export is padded
        // alike, which keeps them sorting together as a single sequence.
        int digits = Math.Max(3, clips.Max(c => c.Count - 1).ToString(CultureInfo.InvariantCulture).Length);

        foreach (AnimationClip clip in clips)
        {
            // A single untagged animation is just "name_000.png"; several tagged ones carry
            // the animation's name so a folder holding all of them stays readable.
            string prefix = clips.Count > 1 || clip.Tag is not null
                ? $"{options.BaseName}_{clip.Name}"
                : options.BaseName;

            for (int i = 0; i < clip.Count; i++)
            {
                using MemoryStream stream = new();
                PngCodec.EncodeImage(stream, clip.Images[i]);

                string number = i.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
                artifacts.Add(new ExportArtifact($"{prefix}_{number}.png", stream.ToArray(), "image/png"));
            }
        }

        return artifacts;
    }
}
