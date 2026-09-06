using Avalonia.Media;

namespace XPXel.UI.Media;

/// <summary>
/// The toolbox icon set, as vector geometry.
/// </summary>
/// <remarks>
/// <para>
/// <b>One drawing style, applied to every icon.</b> Each path below is drawn on a 24×24 grid
/// and is meant to be <em>stroked</em>, not filled — a consistent outline weight is what makes
/// a set of icons read as a set rather than as a collection. Anything added here should keep
/// to that: same grid, same visual weight, no filled shapes mixed in.
/// </para>
/// <para>
/// <b>Why geometry rather than a font or images.</b> An icon font needs a font file shipped
/// and licensed per platform; bitmaps need one asset per scale factor and go soft on a
/// high-density display. Geometry is a string, scales exactly, and re-colours with the theme —
/// and the same paths are reused to draw the mouse cursors, which is what keeps a tool's
/// cursor recognisably the same picture as its button.
/// </para>
/// <para>
/// The geometries are parsed once on first use and cached. Parsing is not expensive, but the
/// toolbox asks for them on every layout pass.
/// </para>
/// </remarks>
public static class ToolIcons
{
    private static readonly Dictionary<string, Geometry> Cache = new(StringComparer.Ordinal);

    /// <summary>A pencil, drawn nib-down.</summary>
    public const string Pencil = "M4 20 L4 16 L15 5 L19 9 L8 20 Z M13 7 L17 11";

    /// <summary>A round paintbrush with a loaded tip.</summary>
    public const string Brush =
        "M19 3 L21 5 L13 13 L11 11 Z " +
        "M10 12.5 C7.5 12.5 6.5 14.5 6.5 16.5 C6.5 18.5 4.5 19 3 20 " +
        "C5 22 8 22 10 21 C12 20 13 18.5 13 16.5 C13 14.5 12 12.5 10 12.5 Z";

    /// <summary>An eraser resting on the line it has cleared.</summary>
    public const string Eraser = "M3 16 L13 6 L18 11 L8 21 L3 21 Z M22 21 L10 21 M8 11 L13 16";

    /// <summary>A tipped paint bucket with a falling drop.</summary>
    public const string Bucket =
        "M19 11 L11 3 L3 11.5 L10 18.5 Z M5 2 L10 7 M2 13 L17 13 " +
        "M22 20 C22 21.1 21.1 22 20 22 C18.9 22 18 21.1 18 20 " +
        "C18 18.4 19.7 17.6 20 16 C20.3 17.6 22 18.4 22 20 Z";

    /// <summary>An eyedropper, tip towards the lower left.</summary>
    public const string Eyedropper = "M14 3 L21 10 L17 14 L10 7 Z M12 9 L4 17 L4 20 L7 20 L15 12";

    /// <summary>A line between two handles.</summary>
    public const string Line = "M6 18 L18 6 M3 17 L7 17 L7 21 L3 21 Z M17 3 L21 3 L21 7 L17 7 Z";

    /// <summary>A rectangle.</summary>
    public const string Rectangle = "M3 5 L21 5 L21 19 L3 19 Z";

    /// <summary>An ellipse.</summary>
    public const string Ellipse = "M3 12 A9 7 0 1 0 21 12 A9 7 0 1 0 3 12 Z";

    /// <summary>A capital T with a serif foot.</summary>
    public const string Text = "M4 7 L4 4 L20 4 L20 7 M12 4 L12 20 M8.5 20 L15.5 20";

    /// <summary>A marching-ants rectangle, drawn as a path with deliberate gaps.</summary>
    public const string Marquee =
        "M3 8 L3 5 L6 5 M10 5 L14 5 M18 5 L21 5 L21 8 M21 12 L21 14 " +
        "M21 18 L21 21 L18 21 M14 21 L10 21 M6 21 L3 21 L3 18 M3 14 L3 12";

    /// <summary>A question mark, shown for a tool with no icon of its own.</summary>
    /// <summary>A pixel wand: a marked square with a pointer at it.</summary>
    public const string PixelSelect =
        "M3 3 L9 3 L9 9 L3 9 Z M13 4 L20 11 L16 12 L18 17 L16 18 L14 13 L11 15 Z";

    /// <summary>A crop frame: two overlapping corner brackets, the printer's mark for it.</summary>
    public const string Crop = "M7 2 L7 17 L22 17 M2 7 L17 7 L17 22";

    public const string Unknown =
        "M9 8 A3 3 0 1 1 12 12 L12 15 M12 19 L12 19.01";

    /// <summary>Parses a path string into geometry, reusing an earlier parse of the same path.</summary>
    /// <param name="pathData">SVG path syntax, as the constants on this class use.</param>
    /// <returns>The parsed geometry.</returns>
    public static Geometry Get(string pathData)
    {
        ArgumentNullException.ThrowIfNull(pathData);

        if (Cache.TryGetValue(pathData, out Geometry? cached))
        {
            return cached;
        }

        Geometry geometry = StreamGeometry.Parse(pathData);
        Cache[pathData] = geometry;
        return geometry;
    }
}
