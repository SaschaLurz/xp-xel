namespace XPXel.Formats.Project;

/// <summary>
/// Version numbers and well-known names of the <c>.xpxel</c> project format.
/// </summary>
/// <remarks>
/// <para>
/// A <c>.xpxel</c> file is an ordinary zip archive laid out as:
/// </para>
/// <code>
/// manifest.json    the document structure - canvas size, layers, frames, cel index, palette
/// cels/0.png       one PNG per cel, referenced by name from the manifest
/// cels/1.png
/// thumbnail.png    a flattened preview, for future recent-files and browser UI
/// </code>
/// <para>
/// <b>Why a zip of JSON and PNGs rather than a custom binary format.</b> The format has to
/// survive years of feature growth — animation, layer groups, tags, game-engine export
/// hints — and every one of those additions is a new JSON property or a new archive entry
/// rather than a change to a byte layout. It is also inspectable: when a file will not open,
/// anyone can unzip it and read the manifest, which is worth a great deal in an open source
/// project. The pixel data is already PNG-compressed, so the size cost over a binary format
/// is small.
/// </para>
/// </remarks>
internal static class ProjectSchema
{
    /// <summary>
    /// The schema version this build writes. Increment it whenever a change would make an
    /// older reader misinterpret a file, and add a matching migration.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>The archive entry holding the document structure.</summary>
    public const string ManifestEntryName = "manifest.json";

    /// <summary>The archive entry holding the flattened preview image.</summary>
    public const string ThumbnailEntryName = "thumbnail.png";

    /// <summary>The archive folder holding per-cel images.</summary>
    public const string CelFolderName = "cels/";

    /// <summary>The <see cref="LayerManifest.Type"/> value for a pixel layer.</summary>
    public const string RasterLayerType = "raster";

    /// <summary>The <see cref="LayerManifest.Type"/> value for a folder.</summary>
    public const string GroupLayerType = "group";
}
