using XPXel.Core.Documents;

namespace XPXel.Formats;

/// <summary>
/// How a document should be written out.
/// </summary>
/// <remarks>
/// <para>
/// The options are shared by every exporter, so a new format inherits the whole export
/// dialog rather than defining its own. Format-specific settings (a JPEG quality slider,
/// say) should arrive later as a separate per-format options object rather than by widening
/// this one indefinitely.
/// </para>
/// <para>
/// <b>Why <see cref="Scale"/> matters here.</b> Pixel art is usually authored small and
/// displayed large. Exporting at 4x with nearest-neighbour sampling is a routine part of
/// preparing art for a store page or a website, and doing it in the exporter guarantees the
/// scaling stays perfectly crisp instead of relying on whatever the destination does.
/// </para>
/// </remarks>
public sealed class ExportOptions
{
    /// <summary>The default options: the first frame, at original size.</summary>
    public static ExportOptions Default { get; } = new();

    /// <summary>
    /// The frame to export, or <see langword="null"/> for the document's first frame.
    /// Formats that support animation ignore this and write every frame.
    /// </summary>
    public Frame? Frame { get; init; }

    /// <summary>
    /// An integer magnification applied with nearest-neighbour sampling, so every source
    /// pixel becomes an exact square block. Must be at least 1.
    /// </summary>
    public int Scale
    {
        get;
        init => field = Math.Max(1, value);
    } = 1;
}
