using Avalonia.Media;
using XPXel.Core.Tools;

namespace XPXel.UI.Media;

/// <summary>
/// Which groups of settings a tool actually uses.
/// </summary>
/// <remarks>
/// A tool that ignores the brush size should not be offered one. Recording it here, beside
/// the icon and the cursor, keeps every presentation decision about a tool in one table
/// rather than spread across the views that happen to show its options; a tool with no entry
/// simply reports none and its settings panel says so.
/// </remarks>
[Flags]
public enum ToolOptions
{
    /// <summary>The tool has no settings of its own.</summary>
    None = 0,

    /// <summary>Brush size and shape.</summary>
    Brush = 1,

    /// <summary>Whether a shape is filled or outlined.</summary>
    Shape = 2,

    /// <summary>Flood fill tolerance and whether the fill is contiguous.</summary>
    Fill = 4,

    /// <summary>The default typeface, size and emphasis for new text.</summary>
    Text = 8,

    /// <summary>
    /// The tool honours softness and stabilisation. Deliberately separate from
    /// <see cref="Brush"/>, because showing a tool a slider it will not obey is worse than
    /// showing it nothing at all.
    /// </summary>
    Dynamics = 16,

    /// <summary>
    /// The tool takes a size but is always a hard square. Only the pencil, and it is its own
    /// flag rather than <see cref="Brush"/> minus <see cref="Dynamics"/> because the shape
    /// chooser has to disappear for it too.
    /// </summary>
    Pencil = 32,

    /// <summary>
    /// The tool marks pixels, and so reads Shift as "add to the selection" and Control as "take
    /// away from it". The canvas shows a plus or a minus beside the pointer while one of those
    /// is held — a modifier that changes what a click does is worth saying out loud, because
    /// otherwise the only way to discover it is to have been told.
    /// </summary>
    Selection = 64,
}

/// <summary>How a tool's pointer should look while that tool is active.</summary>
public enum ToolCursorKind
{
    /// <summary>
    /// A thin crosshair. The right answer for anything that places a pixel exactly, because
    /// an arrow's tip is ambiguous by several pixels and a pixel editor cannot afford that.
    /// </summary>
    Crosshair,

    /// <summary>A text-editing bar.</summary>
    Text,

    /// <summary>The tool's own icon, drawn as the cursor with its working point as the hotspot.</summary>
    Icon,
}

/// <summary>
/// Everything the user interface knows about how to present one tool: its icon, its cursor,
/// and whether the canvas should outline the brush footprint under the pointer.
/// </summary>
/// <param name="Icon">The path geometry drawn in the toolbox, from <see cref="ToolIcons"/>.</param>
/// <param name="Cursor">Which kind of pointer the tool uses.</param>
/// <param name="CursorHotspotX">
/// For <see cref="ToolCursorKind.Icon"/>, the working point of the icon on its 24×24 grid —
/// the bucket's spout, the eyedropper's tip — so the cursor points where the tool acts.
/// </param>
/// <param name="CursorHotspotY">The vertical half of that working point.</param>
/// <param name="ShowsBrushOutline">
/// Whether the canvas should outline the pixels the brush would cover. True for tools whose
/// mark is the brush footprint, false for tools where the brush size means nothing.
/// </param>
/// <param name="Shortcut">
/// The single key that selects this tool, shown in its tooltip. Bare letters, the way every
/// editor of this kind binds them, which is why the shell has to be careful never to claim one
/// while the artist is typing.
/// </param>
/// <param name="Options">
/// Which groups of settings the tool uses, which is what the right-click panel on the canvas
/// shows.
/// </param>
/// <param name="ShowsColorSwatch">
/// Whether the canvas should show a dot of the primary colour beside the pointer while this
/// tool is active. True for the tools that lay that colour down, false for the ones that do
/// not — the eraser writes transparency and the marquee writes nothing, so a colour beside
/// either would be a claim about what is going to happen that is simply untrue.
/// </param>
/// <param name="Group">
/// The identifier of the tool this one shares a toolbox slot with, or empty for a tool that
/// has a slot to itself.
/// </param>
/// <remarks>
/// <b>About <paramref name="Group"/>.</b> Tools that do the same job in different ways share
/// one square in the toolbox, and right-clicking it offers the rest — the arrangement every
/// artist coming from Photoshop already has in their hands. Each member names the same leader,
/// and the leader names itself; the slot then appears wherever the first of its members is
/// registered, so <c>AppServices</c> still decides the order. Every tool keeps its own
/// single-key shortcut, so grouping costs nothing at the keyboard.
/// </remarks>
public sealed record ToolPresentation(
    string Icon,
    ToolCursorKind Cursor,
    double CursorHotspotX,
    double CursorHotspotY,
    bool ShowsBrushOutline,
    string Shortcut = "",
    ToolOptions Options = ToolOptions.None,
    bool ShowsColorSwatch = false,
    string Group = "");

/// <summary>
/// Maps a tool's identity to how it is presented.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a lookup in the UI rather than properties on <see cref="ITool"/>.</b> A
/// tool in <c>XPXel.Core</c> must not know what an icon or a mouse cursor is; that is the
/// whole reason the tools are unit-testable without a display. Presentation is genuinely a
/// user-interface decision, so it lives here, keyed by the stable tool id.
/// </para>
/// <para>
/// <b>Adding a tool still needs no existing file to change</b> in order to work — an
/// unregistered tool appears in the toolbox with the fallback icon and a crosshair, and draws
/// perfectly well. Adding an entry here is what gives it its own look.
/// </para>
/// </remarks>
public static class ToolPresentations
{
    private static readonly ToolPresentation Fallback =
        new(ToolIcons.Unknown, ToolCursorKind.Crosshair, 12, 12, ShowsBrushOutline: false);

    private static readonly Dictionary<string, ToolPresentation> ById =
        new(StringComparer.Ordinal)
        {
            // The pen-xel leads the drawing slot; the brush is the same job done softly, which
            // is exactly how Photoshop pairs them.
            [PencilTool.ToolId] =
                new(ToolIcons.Pencil, ToolCursorKind.Crosshair, 12, 12, ShowsBrushOutline: true, Shortcut: "Q",
                    Options: ToolOptions.Pencil, ShowsColorSwatch: true, Group: PencilTool.ToolId),

            [BrushTool.ToolId] =
                new(ToolIcons.Brush, ToolCursorKind.Crosshair, 12, 12, ShowsBrushOutline: true, Shortcut: "B",
                    Options: ToolOptions.Brush | ToolOptions.Dynamics, ShowsColorSwatch: true,
                    Group: PencilTool.ToolId),

            [EraserTool.ToolId] =
                new(ToolIcons.Eraser, ToolCursorKind.Crosshair, 12, 12, ShowsBrushOutline: true, Shortcut: "E",
                    Options: ToolOptions.Brush | ToolOptions.Dynamics),

            // The bucket's hotspot is the lip the paint pours from, not the middle of the pot.
            [BucketFillTool.ToolId] =
                new(ToolIcons.Bucket, ToolCursorKind.Icon, 3, 11.5, ShowsBrushOutline: false, Shortcut: "G",
                    Options: ToolOptions.Fill, ShowsColorSwatch: true),

            // The eyedropper's hotspot is the very tip of the tube, which is the pixel it samples.
            [EyedropperTool.ToolId] =
                new(ToolIcons.Eyedropper, ToolCursorKind.Icon, 4, 20, ShowsBrushOutline: false, Shortcut: "I"),

            // The three shapes share a slot, as they do in every editor that has them.
            [LineTool.ToolId] =
                new(ToolIcons.Line, ToolCursorKind.Crosshair, 12, 12, ShowsBrushOutline: true, Shortcut: "L",
                    Options: ToolOptions.Brush, ShowsColorSwatch: true, Group: LineTool.ToolId),

            [RectangleTool.ToolId] =
                new(ToolIcons.Rectangle, ToolCursorKind.Crosshair, 12, 12, ShowsBrushOutline: true, Shortcut: "R",
                    Options: ToolOptions.Brush | ToolOptions.Shape, ShowsColorSwatch: true,
                    Group: LineTool.ToolId),

            [EllipseTool.ToolId] =
                new(ToolIcons.Ellipse, ToolCursorKind.Crosshair, 12, 12, ShowsBrushOutline: true, Shortcut: "O",
                    Options: ToolOptions.Brush | ToolOptions.Shape, ShowsColorSwatch: true,
                    Group: LineTool.ToolId),

            [TextTool.ToolId] =
                new(ToolIcons.Text, ToolCursorKind.Text, 12, 12, ShowsBrushOutline: false, Shortcut: "T",
                    Options: ToolOptions.Text, ShowsColorSwatch: true),

            // The three ways of marking pixels share a slot: they answer one question -
            // "which pixels?" - and differ only in how the artist would rather say it.
            [RectangleSelectTool.ToolId] =
                new(ToolIcons.Marquee, ToolCursorKind.Crosshair, 12, 12, ShowsBrushOutline: false, Shortcut: "M",
                    Options: ToolOptions.Selection, Group: RectangleSelectTool.ToolId),

            [PixelSelectTool.ToolId] =
                new(ToolIcons.PixelSelect, ToolCursorKind.Crosshair, 12, 12, ShowsBrushOutline: true,
                    Shortcut: "K", Options: ToolOptions.Pencil | ToolOptions.Selection,
                    Group: RectangleSelectTool.ToolId),

            // The wand asks the same two questions the bucket does — how close a colour counts,
            // and whether the area has to be joined up — so it shows the same options.
            [MagicWandTool.ToolId] =
                new(ToolIcons.MagicWand, ToolCursorKind.Crosshair, 12, 12, ShowsBrushOutline: false,
                    Shortcut: "W", Options: ToolOptions.Fill | ToolOptions.Selection,
                    Group: RectangleSelectTool.ToolId),

            [MoveTool.ToolId] =
                new(ToolIcons.Move, ToolCursorKind.Crosshair, 12, 12, ShowsBrushOutline: false, Shortcut: "V"),

            [CropTool.ToolId] =
                new(ToolIcons.Crop, ToolCursorKind.Crosshair, 12, 12, ShowsBrushOutline: false, Shortcut: "C"),
        };

    /// <summary>Looks up how a tool should be presented.</summary>
    /// <param name="toolId">The tool's stable identifier, or <see langword="null"/>.</param>
    /// <returns>The presentation, or a neutral fallback for a tool with no entry.</returns>
    public static ToolPresentation For(string? toolId) =>
        toolId is not null && ById.TryGetValue(toolId, out ToolPresentation? presentation)
            ? presentation
            : Fallback;

    /// <summary>Returns the icon geometry for a tool.</summary>
    /// <param name="toolId">The tool's stable identifier.</param>
    /// <returns>The parsed geometry, ready to bind to a <c>Path</c>.</returns>
    public static Geometry IconFor(string? toolId) => ToolIcons.Get(For(toolId).Icon);
}
