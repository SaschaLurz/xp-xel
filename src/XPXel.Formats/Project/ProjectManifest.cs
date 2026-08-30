using System.Text.Json.Serialization;

namespace XPXel.Formats.Project;

/// <summary>
/// The data transfer objects mirroring <c>manifest.json</c> inside a <c>.xpxel</c> archive.
/// </summary>
/// <remarks>
/// <para>
/// These types are the on-disk contract and are intentionally separate from the live model
/// in <c>XPXel.Core.Documents</c>. Keeping them apart means the document model can be
/// refactored freely — renaming a property, changing a type, restructuring the tree —
/// without silently changing the file format, and conversely that the file format can gain
/// fields the model does not have yet.
/// </para>
/// <para>
/// <b>Compatibility rules for anyone editing this file:</b>
/// </para>
/// <list type="bullet">
///   <item><description>
///     Adding an optional property is always safe. Give it a sensible default so older
///     files, which lack it, still load.
///   </description></item>
///   <item><description>
///     Renaming, removing or changing the meaning of a property requires bumping
///     <see cref="ProjectSchema.CurrentVersion"/> and adding a migration.
///   </description></item>
///   <item><description>
///     Enumerations are stored by <em>name</em>, not by number, and an unrecognised name
///     falls back to a default rather than failing the load. That is what lets a file
///     written by a newer XP-xel still open in an older one.
///   </description></item>
/// </list>
/// </remarks>
internal sealed class ProjectManifest
{
    /// <summary>The schema version this file was written with.</summary>
    public int SchemaVersion { get; set; } = ProjectSchema.CurrentVersion;

    /// <summary>The application that wrote the file, for diagnostics.</summary>
    public string Application { get; set; } = "XP-xel";

    /// <summary>The version of that application, for diagnostics.</summary>
    public string ApplicationVersion { get; set; } = string.Empty;

    /// <summary>The canvas width in pixels.</summary>
    public int Width { get; set; }

    /// <summary>The canvas height in pixels.</summary>
    public int Height { get; set; }

    /// <summary>The timeline, in playback order.</summary>
    public List<FrameManifest> Frames { get; set; } = [];

    /// <summary>The top-level layers, bottom-most first.</summary>
    public List<LayerManifest> Layers { get; set; } = [];

    /// <summary>Every cel, each naming the archive entry holding its pixels.</summary>
    public List<CelManifest> Cels { get; set; } = [];

    /// <summary>The document palette.</summary>
    public PaletteManifest Palette { get; set; } = new();

    /// <summary>Free-form document metadata.</summary>
    public Dictionary<string, string> Metadata { get; set; } = [];
}

/// <summary>One entry of the timeline.</summary>
internal sealed class FrameManifest
{
    /// <summary>The frame's stable identity.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>How long the frame is held during playback.</summary>
    public int DurationMilliseconds { get; set; } = 100;
}

/// <summary>One node of the layer tree.</summary>
internal sealed class LayerManifest
{
    /// <summary>The layer's stable identity, used as half of every cel key.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The kind of layer: <c>"raster"</c> or <c>"group"</c>. Stored as text so that new
    /// layer kinds can be added without renumbering, and an unknown kind can be reported
    /// clearly instead of being misread as an existing one.
    /// </summary>
    public string Type { get; set; } = ProjectSchema.RasterLayerType;

    /// <summary>The display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether the layer contributes to the composited image.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>Whether the layer rejects edits.</summary>
    public bool IsLocked { get; set; }

    /// <summary>The layer's opacity, 0 to 1.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>The blend mode name; an unrecognised value loads as Normal.</summary>
    public string BlendMode { get; set; } = nameof(Core.Imaging.BlendMode.Normal);

    /// <summary>Nested layers, bottom-most first. Empty for a raster layer.</summary>
    public List<LayerManifest> Children { get; set; } = [];
}

/// <summary>The pixel content of one layer on one frame.</summary>
internal sealed class CelManifest
{
    /// <summary>The owning layer's identity.</summary>
    public string LayerId { get; set; } = string.Empty;

    /// <summary>The owning frame's identity.</summary>
    public string FrameId { get; set; } = string.Empty;

    /// <summary>
    /// The archive entry holding this cel's pixels, for example <c>cels/0.png</c>. Cel
    /// images are stored as PNG rather than raw bytes so a project file stays a fraction of
    /// its uncompressed size, and so its contents can be inspected with any zip tool.
    /// </summary>
    public string Image { get; set; } = string.Empty;

    /// <summary>The horizontal offset of the cel buffer within the canvas.</summary>
    public int OffsetX { get; set; }

    /// <summary>The vertical offset of the cel buffer within the canvas.</summary>
    public int OffsetY { get; set; }

    /// <summary>The cel's own opacity multiplier, 0 to 1.</summary>
    public double Opacity { get; set; } = 1.0;
}

/// <summary>The document's colour palette.</summary>
internal sealed class PaletteManifest
{
    /// <summary>The palette's display name.</summary>
    public string Name { get; set; } = "Palette";

    /// <summary>The colours as <c>#RRGGBB</c> or <c>#RRGGBBAA</c> strings.</summary>
    public List<string> Colors { get; set; } = [];
}

/// <summary>
/// The source-generated JSON serializer for the manifest.
/// </summary>
/// <remarks>
/// Source generation rather than reflection is a deliberate choice: it is required for the
/// WebAssembly build to survive trimming, and it will be required again if the desktop build
/// ever moves to NativeAOT. Reflection-based serialization would work today and fail exactly
/// when the project tries to ship on those targets.
/// </remarks>
// Every property is written, including ones that happen to equal their CLR default.
// Omitting defaults looks like a harmless size optimisation and is a correctness trap: a
// hidden layer stores isVisible=false, which is bool's default and would be dropped, and the
// property would then reload as true because that is the manifest's default. The same
// applies to a fully transparent layer at opacity 0. The manifest is a few kilobytes inside
// a zip; there is nothing to gain by being clever here.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(ProjectManifest))]
internal sealed partial class ProjectManifestJsonContext : JsonSerializerContext;
