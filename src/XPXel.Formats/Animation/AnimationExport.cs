using XPXel.Core.Documents;

namespace XPXel.Formats.Animation;

/// <summary>How the frames of a sprite sheet are arranged on the image.</summary>
public enum SpriteSheetLayout
{
    /// <summary>
    /// One row per named animation, frames running left to right.
    /// </summary>
    /// <remarks>
    /// The classic character sheet, and the reason it is first: it is the arrangement a
    /// person can read. Row 3 is the attack; frame 5 of it is the fifth cell along. Every
    /// engine slices it the same way it slices any grid, and rows shorter than the longest
    /// animation are simply padded with empty cells.
    /// </remarks>
    RowPerAnimation = 0,

    /// <summary>Frames packed into the squarest grid that holds them.</summary>
    /// <remarks>
    /// The most texture-efficient option, and the right one for a single animation or for a
    /// long one that would otherwise produce an absurdly wide image.
    /// </remarks>
    Grid = 1,

    /// <summary>Every frame in a single row.</summary>
    HorizontalStrip = 2,

    /// <summary>Every frame in a single column.</summary>
    VerticalStrip = 3,
}

/// <summary>
/// One file produced by an export.
/// </summary>
/// <param name="FileName">The name to save it under, including its extension.</param>
/// <param name="Content">The file's bytes.</param>
/// <param name="MimeType">The content type, for clipboard and web integration.</param>
/// <remarks>
/// Exports are modelled as a list of these rather than as a write to a single stream, because
/// most animation exports are not one file: a sprite sheet is an image plus its metadata, and
/// a frame sequence is one file per frame. Handing back named byte arrays keeps every
/// exporter free of file handles, folders and platform storage APIs — which is what lets the
/// same code run in the browser, where none of those exist in the usual form.
/// </remarks>
public sealed record ExportArtifact(string FileName, byte[] Content, string MimeType);

/// <summary>
/// How an animation should be written out.
/// </summary>
/// <remarks>
/// Separate from <see cref="ExportOptions"/>, which describes writing one still image. The
/// two overlap only in <see cref="Scale"/>, and merging them would give every still-image
/// export a set of animation settings that mean nothing to it.
/// </remarks>
public sealed class AnimationExportOptions
{
    /// <summary>The default options: every frame, at original size, as a readable sheet.</summary>
    public static AnimationExportOptions Default { get; } = new();

    /// <summary>
    /// The base file name, without an extension. Exporters append their own extension, an
    /// animation name, or a frame number as appropriate.
    /// </summary>
    public string BaseName
    {
        get;
        init => field = SanitiseFileName(value);
    } = "animation";

    /// <summary>
    /// An integer magnification applied with nearest-neighbour sampling.
    /// </summary>
    /// <remarks>
    /// Doing this in the exporter rather than in the engine guarantees the enlargement stays
    /// perfectly crisp. It is routine for pixel art destined for a high-resolution display.
    /// </remarks>
    public int Scale
    {
        get;
        init => field = Math.Clamp(value, 1, 32);
    } = 1;

    /// <summary>
    /// The single animation to export, or <see langword="null"/> for every frame in the
    /// document.
    /// </summary>
    public AnimationTag? Tag { get; init; }

    /// <summary>
    /// Whether each named animation becomes its own file rather than sharing one.
    /// </summary>
    /// <remarks>
    /// Ignored when <see cref="Tag"/> names a single animation, and meaningless for a
    /// document with no tags. One file per animation is what an engine wanting one clip per
    /// motion prefers; one shared file is fewer textures to load.
    /// </remarks>
    public bool SeparateFilePerTag { get; init; }

    /// <summary>How frames are arranged on a sprite sheet.</summary>
    public SpriteSheetLayout Layout { get; init; } = SpriteSheetLayout.RowPerAnimation;

    /// <summary>Transparent pixels left between and around cells, in exported pixels.</summary>
    /// <remarks>
    /// Zero by default, which is what a grid slice expects. Padding exists because some
    /// renderers sample slightly outside a sprite's rectangle when the texture is filtered or
    /// scaled, pulling in a neighbouring frame's edge — the "texture bleeding" artefact.
    /// Pixel art is normally drawn with filtering off, where it cannot happen.
    /// </remarks>
    public int Padding
    {
        get;
        init => field = Math.Clamp(value, 0, 64);
    }

    /// <summary>Whether to write the JSON sidecar describing the sheet.</summary>
    public bool IncludeMetadata { get; init; } = true;

    /// <summary>
    /// Whether a reverse or ping-pong animation is written out in its played order rather
    /// than its authored order.
    /// </summary>
    /// <remarks>
    /// True for the animated image formats, which have no way to express a loop direction and
    /// must therefore contain the frames in the order they are seen. False for sprite sheets,
    /// where duplicating the return leg of a ping-pong would store the same drawing twice and
    /// the direction is recorded in the metadata for the engine to act on.
    /// </remarks>
    public bool ExpandLoop { get; init; }

    /// <summary>
    /// Strips the characters that cannot appear in a file name, so an animation called
    /// "run/jump" does not produce a path.
    /// </summary>
    /// <param name="name">The proposed name.</param>
    /// <returns>A usable file name, never empty.</returns>
    public static string SanitiseFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "animation";
        }

        Span<char> buffer = stackalloc char[name.Length];
        int length = 0;

        foreach (char c in name)
        {
            buffer[length++] = char.IsLetterOrDigit(c) || c is '-' or '_' or ' ' or '.' ? c : '_';
        }

        string cleaned = new string(buffer[..length]).Trim();
        return cleaned.Length == 0 ? "animation" : cleaned;
    }
}

/// <summary>
/// Writes a document's animation to one or more files.
/// </summary>
/// <remarks>
/// <para>
/// The animation counterpart to <see cref="IImageExporter"/>. It is a separate interface
/// rather than a widening of that one because the shapes genuinely differ: a still export is
/// one stream in, one image out, while an animation export produces a set of named files and
/// needs to know about tags, layout and looping.
/// </para>
/// <para>
/// <b>Adding a format</b> is a class implementing this plus one registration line, exactly as
/// with the still formats. <see cref="AnimationExportRegistry"/> and the export dialog pick
/// it up with no further change.
/// </para>
/// </remarks>
public interface IAnimationExporter
{
    /// <summary>The format this exporter writes.</summary>
    FormatDescriptor Descriptor { get; }

    /// <summary>
    /// A sentence describing what the artist gets, shown beside the format in the export
    /// dialog.
    /// </summary>
    /// <remarks>
    /// Formats differ in ways that matter to the decision and are invisible from the name —
    /// a GIF loses colours, a sheet needs slicing, a sequence is a folder full of files. The
    /// dialog shows this rather than hard-coding a description per format.
    /// </remarks>
    string Summary { get; }

    /// <summary>Gets a value indicating whether the export is always exactly one file.</summary>
    /// <remarks>
    /// The shell asks for a file name when this is true and a destination folder when it is
    /// not, which is the only difference between the two paths.
    /// </remarks>
    bool ProducesSingleFile { get; }

    /// <summary>Gets a value indicating whether the format arranges frames on a sheet.</summary>
    /// <remarks>
    /// Drives whether the export dialog offers the layout and padding controls, so that the
    /// dialog does not have to know which format is which.
    /// </remarks>
    bool UsesSheetLayout => false;

    /// <summary>Writes the animation.</summary>
    /// <param name="document">The document to export.</param>
    /// <param name="options">How to export it.</param>
    /// <returns>The files produced, in the order they should be written.</returns>
    /// <exception cref="ImageFormatException">The document cannot be expressed in this format.</exception>
    IReadOnlyList<ExportArtifact> Export(PixelDocument document, AnimationExportOptions options);
}
