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

    /// <summary>
    /// The named animations cut out of the timeline, or an empty list for a still image.
    /// </summary>
    /// <remarks>
    /// Added after schema version 1 shipped, and needing no migration for the same reason the
    /// layer label colour did not: it is optional with a sensible default. A file written by
    /// an older build simply has no animations, and one written by a newer build still opens
    /// in an older one, which ignores the property and shows every frame as a flat timeline.
    /// </remarks>
    public List<TagManifest> Tags { get; set; } = [];

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
    /// The kind of layer: <c>"raster"</c>, <c>"group"</c> or <c>"text"</c>. Stored as text so that new
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

    /// <summary>
    /// The layer's organisational label colour as <c>#RRGGBBAA</c>, or <see langword="null"/>
    /// when the layer carries no label.
    /// </summary>
    /// <remarks>
    /// Added after schema version 1 shipped. It needs no migration and no version bump
    /// because it is optional with a sensible default: a file written by an older build
    /// simply has no colour, and a file written by a newer build still opens in an older
    /// build, which ignores the property.
    /// </remarks>
    public string? TagColor { get; set; }

    /// <summary>Nested layers, bottom-most first. Empty for a raster layer.</summary>
    public List<LayerManifest> Children { get; set; } = [];

    /// <summary>
    /// The words and typography of a text layer, or <see langword="null"/> for every other
    /// kind of layer.
    /// </summary>
    /// <remarks>
    /// This is what makes text re-editable across a save and reload. The rendered glyphs are
    /// stored separately as an ordinary cel, so a reader that ignores this property still
    /// shows the text correctly — it just cannot change the words.
    /// </remarks>
    public TextManifest? Text { get; set; }
}

/// <summary>The editable content of a text layer.</summary>
internal sealed class TextManifest
{
    /// <summary>The text itself, which may contain line breaks.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>The horizontal position of the text block's top-left corner.</summary>
    public int OriginX { get; set; }

    /// <summary>The vertical position of the text block's top-left corner.</summary>
    public int OriginY { get; set; }

    /// <summary>
    /// The font family name. A name this machine does not have is substituted by the font
    /// engine rather than rejected, which is why the glyphs are also stored as pixels.
    /// </summary>
    public string FontFamily { get; set; } = string.Empty;

    /// <summary>The font size in pixels.</summary>
    public double FontSize { get; set; } = 16.0;

    /// <summary>Whether the text is bold.</summary>
    public bool IsBold { get; set; }

    /// <summary>Whether the text is italic.</summary>
    public bool IsItalic { get; set; }

    /// <summary>Whether the text is underlined.</summary>
    public bool IsUnderline { get; set; }

    /// <summary>Whether the text is struck through.</summary>
    public bool IsStrikethrough { get; set; }

    /// <summary>The alignment name; an unrecognised value loads as Left.</summary>
    public string Alignment { get; set; } = nameof(Core.Text.TextAlignment.Left);

    /// <summary>The line height multiplier, where 1.0 is single spacing.</summary>
    public double LineSpacing { get; set; } = 1.0;

    /// <summary>Extra space between characters, in pixels.</summary>
    public double LetterSpacing { get; set; }

    /// <summary>Whether glyph edges are smoothed.</summary>
    public bool IsAntiAliased { get; set; }

    /// <summary>The glyph colour as <c>#RRGGBB</c> or <c>#RRGGBBAA</c>.</summary>
    public string Color { get; set; } = "#000000";
}

/// <summary>One named animation over a range of frames.</summary>
internal sealed class TagManifest
{
    /// <summary>The tag's stable identity.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The animation's name, which becomes a file name on export.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The first frame index, inclusive.</summary>
    public int From { get; set; }

    /// <summary>The last frame index, inclusive.</summary>
    public int To { get; set; }

    /// <summary>
    /// The loop direction name; an unrecognised value loads as Forward.
    /// </summary>
    public string Direction { get; set; } = nameof(Core.Documents.LoopDirection.Forward);

    /// <summary>How many times the animation plays, or 0 for an endless loop.</summary>
    public int RepeatCount { get; set; }

    /// <summary>The tag's bar colour as <c>#RRGGBBAA</c>, or <see langword="null"/> for none.</summary>
    public string? Color { get; set; }
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
