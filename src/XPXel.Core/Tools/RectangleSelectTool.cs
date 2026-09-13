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
/// <b>A press inside an existing selection moves it instead.</b> Having marked something, the
/// next thing an artist wants is almost always to move it; see <see cref="SelectionTools"/>.
/// </para>
/// <para>
/// <b>Shift adds and Control takes away</b>, as on every other selection tool — see
/// <see cref="SelectionTools.GetEditMode"/>. Constraining the drag to a square therefore moved
/// to Alt: modifiers that mean the same thing on every selection tool are worth more than one
/// tool's private shortcut, and a square marquee is still one key away.
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
    private SelectionEditMode _mode;
    private bool _isDragging;

    /// <inheritdoc />
    public string Id => ToolId;

    /// <inheritdoc />
    public string DisplayName => "Rectangular Select";

    /// <inheritdoc />
    public string Description =>
        "Drag to select a rectangle; Shift adds to the selection, Control takes away, Alt keeps it square. Click once to deselect.";

    /// <inheritdoc />
    public void OnPointerPressed(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(e);

        if (e.Button != ToolPointerButton.Left)
        {
            return;
        }

        _mode = SelectionTools.GetEditMode(e);

        // Pressing on pixels that are already marked means "move these", not "start again".
        // Shared with the pixel select tool, because which tool did the marking should not
        // change how the marked pixels are picked up. A modifier says the artist is still
        // marking, so it suppresses the pick-up.
        if (_mode == SelectionEditMode.Replace && SelectionTools.TryBeginMove(context, e.Position))
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
        // for "get this selection out of my way" — but only when replacing. Shift-clicking one
        // pixel is a request to add that pixel, and must not throw the rest away.
        if (region.Width <= 1 && region.Height <= 1 && _mode == SelectionEditMode.Replace)
        {
            context.Workspace.Selection = null;
            return;
        }

        SelectionMask mask = SelectionTools.GetTargetMask(context, _mode);

        for (int y = region.Top; y < region.Bottom; y++)
        {
            for (int x = region.Left; x < region.Right; x++)
            {
                SelectionTools.SetPixel(mask, x, y, _mode);
            }
        }

        SelectionTools.Publish(context, mask);
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
        ImagePoint corner = e.IsAltDown
            ? ShapeRasterizer.ConstrainToSquare(_anchor, e.Position)
            : e.Position;

        return ImageRect.FromPoints(_anchor, corner).Intersect(context.Document.Bounds);
    }
}
