using XPXel.Core.Drawing;
using XPXel.Core.Primitives;
using XPXel.Core.Selection;

namespace XPXel.Core.Tools;

/// <summary>
/// Drags out a rectangular selection, after which every drawing tool is clipped to it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This tool changes no existing tool.</b> The selection mask has been part of
/// <see cref="ToolContext"/> since v0.1 and is enforced inside
/// <see cref="History.RasterEditSession"/>, so every write already asked "may I edit this
/// pixel?" against a mask that was always <see langword="null"/>. Filling that mask in is all
/// this tool does — which is precisely the retro-fit the original design was arranged to
/// avoid having to make.
/// </para>
/// <para>
/// <b>A click without a drag deselects.</b> That is the convention in every editor with a
/// marquee, and it is the fastest way out of a selection that is in the way.
/// </para>
/// <para>
/// <b>Selecting is not undoable.</b> Ctrl+Z is how an artist takes back a mark they made on
/// the artwork, and spending it on a selection would mean pressing it twice to reach the
/// stroke they actually meant. Select All, Deselect and Invert in the Select menu are the
/// way back instead.
/// </para>
/// </remarks>
public sealed class RectangleSelectTool : ITool
{
    /// <summary>The stable identifier of this tool.</summary>
    public const string ToolId = "xpxel.tool.select.rectangle";

    private ImagePoint _anchor;
    private bool _isDragging;

    /// <inheritdoc />
    public string Id => ToolId;

    /// <inheritdoc />
    public string DisplayName => "Rectangular Select";

    /// <inheritdoc />
    public string Description =>
        "Drag to select a rectangle; drawing is then limited to it. Click once to deselect. Hold Shift for a square.";

    /// <inheritdoc />
    public void OnPointerPressed(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(e);

        if (e.Button is not (ToolPointerButton.Left or ToolPointerButton.Right))
        {
            return;
        }

        _anchor = e.Position;
        _isDragging = true;
        context.Overlay.Marquee = ImageRect.FromPoints(_anchor, _anchor).Intersect(context.Document.Bounds);
    }

    /// <inheritdoc />
    public void OnPointerMoved(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(e);

        if (!_isDragging)
        {
            return;
        }

        context.Overlay.Marquee = BuildRect(context, e);
    }

    /// <inheritdoc />
    public void OnPointerReleased(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(e);

        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        context.Overlay.Marquee = null;

        ImageRect region = BuildRect(context, e);

        // A single pixel means the artist clicked rather than dragged, which is the gesture
        // for "get this selection out of my way".
        context.Workspace.Selection = region.Width <= 1 && region.Height <= 1
            ? null
            : SelectionMask.FromRect(context.Document.Size, region);
    }

    /// <inheritdoc />
    public void Cancel(ToolContext context)
    {
        _isDragging = false;

        if (context is not null)
        {
            context.Overlay.Marquee = null;
        }
    }

    private ImageRect BuildRect(ToolContext context, ToolPointerEventArgs e)
    {
        ImagePoint corner = e.IsShiftDown
            ? ShapeRasterizer.ConstrainToSquare(_anchor, e.Position)
            : e.Position;

        return ImageRect.FromPoints(_anchor, corner).Intersect(context.Document.Bounds);
    }
}
