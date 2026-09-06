using System.Text.Json.Serialization;

namespace XPXel.Formats.Animation;

/// <summary>
/// The JSON written beside a sprite sheet, describing where each frame is and how long it is
/// held.
/// </summary>
/// <remarks>
/// <para>
/// <b>The shape deliberately matches Aseprite's array-format JSON.</b> That format is the
/// closest thing 2D game development has to an interchange standard for sprite sheets: Unity
/// importer packages, Godot plugins, Phaser, PixiJS and a great many hand-written importers
/// already read it. Matching it means an XP-xel sheet drops into an existing pipeline with no
/// work at all, which is worth far more than a tidier schema of our own.
/// </para>
/// <para>
/// The consequence is that these property names are <b>not</b> ours to tidy. Renaming
/// <c>frameTags</c> to <c>animations</c> would be an improvement in isolation and would break
/// every one of those importers.
/// </para>
/// </remarks>
internal sealed class SpriteSheetManifest
{
    /// <summary>Every frame on the sheet, in sheet order.</summary>
    public List<SheetFrameEntry> Frames { get; set; } = [];

    /// <summary>Information about the sheet as a whole.</summary>
    public SheetMetaEntry Meta { get; set; } = new();
}

/// <summary>One frame's position and timing.</summary>
internal sealed class SheetFrameEntry
{
    /// <summary>
    /// A name for the frame, conventionally "<c>animation index.ext</c>". Importers use it to
    /// match frames to clips when they ignore <c>frameTags</c>.
    /// </summary>
    public string Filename { get; set; } = string.Empty;

    /// <summary>The frame's rectangle on the sheet.</summary>
    public SheetRect Frame { get; set; } = new();

    /// <summary>
    /// Whether the frame was trimmed to its content. Always false: every cell is the full
    /// canvas, because a uniform grid is what makes the sheet sliceable.
    /// </summary>
    public bool Trimmed { get; set; }

    /// <summary>Where the frame's content sits inside the source canvas.</summary>
    public SheetRect SpriteSourceSize { get; set; } = new();

    /// <summary>The canvas size the frame was drawn on.</summary>
    public SheetSize SourceSize { get; set; } = new();

    /// <summary>How long the frame is held, in milliseconds.</summary>
    public int Duration { get; set; } = 100;
}

/// <summary>A rectangle in the JSON's own shape.</summary>
internal sealed class SheetRect
{
    /// <summary>The left edge.</summary>
    public int X { get; set; }

    /// <summary>The top edge.</summary>
    public int Y { get; set; }

    /// <summary>The width.</summary>
    public int W { get; set; }

    /// <summary>The height.</summary>
    public int H { get; set; }
}

/// <summary>A size in the JSON's own shape.</summary>
internal sealed class SheetSize
{
    /// <summary>The width.</summary>
    public int W { get; set; }

    /// <summary>The height.</summary>
    public int H { get; set; }
}

/// <summary>Sheet-wide information.</summary>
internal sealed class SheetMetaEntry
{
    /// <summary>The application that wrote the sheet.</summary>
    public string App { get; set; } = "XP-xel";

    /// <summary>That application's version.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>The image file this metadata describes.</summary>
    public string Image { get; set; } = string.Empty;

    /// <summary>The pixel format of that image.</summary>
    public string Format { get; set; } = "RGBA8888";

    /// <summary>The image's dimensions.</summary>
    public SheetSize Size { get; set; } = new();

    /// <summary>The magnification the sheet was exported at, as text.</summary>
    public string Scale { get; set; } = "1";

    /// <summary>
    /// The size of one cell, which is what an engine needs to slice the sheet.
    /// </summary>
    /// <remarks>
    /// An addition of ours rather than part of Aseprite's format. It is the single most
    /// useful number in the file — it is what you type into Unity's "Grid By Cell Size" —
    /// and an importer that does not know the property simply ignores it.
    /// </remarks>
    public SheetSize CellSize { get; set; } = new();

    /// <summary>How many cells the sheet has across and down.</summary>
    public SheetSize Grid { get; set; } = new();

    /// <summary>The named animations and the frame ranges they cover.</summary>
    public List<SheetTagEntry> FrameTags { get; set; } = [];

    /// <summary>The document's layers, for importers that reconstruct structure.</summary>
    public List<SheetLayerEntry> Layers { get; set; } = [];
}

/// <summary>One named animation's range within the sheet's frame list.</summary>
internal sealed class SheetTagEntry
{
    /// <summary>The animation's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The first frame index in this sheet's frame list, inclusive.</summary>
    public int From { get; set; }

    /// <summary>The last frame index in this sheet's frame list, inclusive.</summary>
    public int To { get; set; }

    /// <summary>The loop direction: <c>forward</c>, <c>reverse</c> or <c>pingpong</c>.</summary>
    public string Direction { get; set; } = "forward";

    /// <summary>How many times to play, as text, or an empty string for an endless loop.</summary>
    public string Repeat { get; set; } = string.Empty;
}

/// <summary>One layer of the source document.</summary>
internal sealed class SheetLayerEntry
{
    /// <summary>The layer's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The layer's opacity, 0 to 255.</summary>
    public int Opacity { get; set; } = 255;

    /// <summary>The layer's blend mode name.</summary>
    public string BlendMode { get; set; } = "normal";
}

/// <summary>
/// The source-generated serializer for the sheet manifest.
/// </summary>
/// <remarks>
/// Source generation rather than reflection, for the same reason the project manifest uses
/// it: the WebAssembly build has to survive trimming, and reflection-based serialization
/// would work everywhere except there.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(SpriteSheetManifest))]
internal sealed partial class SpriteSheetJsonContext : JsonSerializerContext;
