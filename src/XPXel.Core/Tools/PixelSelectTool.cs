using XPXel.Core.Drawing;
using XPXel.Core.Primitives;
using XPXel.Core.Selection;

namespace XPXel.Core.Tools;

/// <summary>
/// Marks individual pixels by clicking or dragging over them, and picks up what it has marked.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it is for.</b> The rectangular marquee can only ever say "this box". A great deal of
/// pixel art is not a box — an arm, a highlight, three stray pixels that ended up a shade wrong
/// — and marking those meant, until now, drawing several boxes and hoping. This paints the
/// selection directly, at whatever brush size is set, and the pixels it marks need not touch
/// each other.
/// </para>
/// <para>
/// <b>A plain click starts again.</b> Clicking marks that one pixel and unmarks everything
/// else, which is what makes a stray click cost nothing, and what lets an artist pick out a
/// handful of pixels that are nowhere near each other: click the first, then Shift-click the
/// rest. Shift adds to what is already marked and Control takes away from it, the same as on
/// every other selection tool — see <see cref="SelectionTools.GetEditMode"/>.
/// </para>
/// <para>
/// <b>Pressing on pixels it has already marked picks them up</b> — see
/// <see cref="SelectionTools"/>. That is the same gesture the marquee has, deliberately: which
/// tool did the marking should not change how the marked pixels are moved. Holding a modifier
/// says "I am still marking", so adding a pixel next to one already marked never turns into an
/// accidental drag.
/// </para>
/// <para>
/// <b>Marking is not undoable</b>, for the same reason the marquee's is not: Ctrl+Z is how an
/// artist takes back a mark they made on the artwork, and spending it on a selection would mean
/// pressing it twice to reach the stroke they actually meant.
/// </para>
/// </remarks>
public sealed class PixelSelectTool : ITool
{
    /// <summary>The stable identifier of this tool.</summary>
    public const string ToolId = "xpxel.tool.select.pixel";

    private SelectionMask? _mask;
    private SelectionEditMode _mode;
    private ImagePoint _lastPoint;
    private bool _isMarking;

    /// <inheritdoc />
    public string Id => ToolId;

    /// <inheritdoc />
    public string DisplayName => "Pixel Select";

    /// <inheritdoc />
    public string Description =>
        "Click or drag to mark pixels; hold Shift to add, Control to take away. Press on marked pixels to move them.";

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

        // Pressing on what is already marked means "move this" — but only when no modifier is
        // held, because a modifier is the artist saying they are still marking.
        if (_mode == SelectionEditMode.Replace && SelectionTools.TryBeginMove(context, e.Position))
        {
            return;
        }

        _mask = SelectionTools.GetTargetMask(context, _mode);
        _isMarking = true;
        _lastPoint = e.Position;

        Mark(context, e.Position);
    }

    /// <inheritdoc />
    public void OnPointerMoved(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(e);

        if (!_isMarking || e.Position == _lastPoint)
        {
            return;
        }

        // Joined up the same way a stroke is, so a quick drag marks a continuous run rather
        // than a dotted line of wherever the pointer happened to be sampled.
        LineRasterizer.PlotLine(_lastPoint, e.Position, (x, y) => Mark(context, new ImagePoint(x, y)));
        _lastPoint = e.Position;
    }

    /// <inheritdoc />
    public void OnPointerReleased(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_isMarking)
        {
            return;
        }

        _isMarking = false;

        if (_mask is { } mask)
        {
            SelectionTools.Publish(context, mask);
        }

        _mask = null;
    }

    /// <inheritdoc />
    public void Cancel(ToolContext context)
    {
        _isMarking = false;
        _mask = null;
    }

    /// <summary>Marks or unmarks the brush's footprint around one point.</summary>
    private void Mark(ToolContext context, ImagePoint point)
    {
        if (_mask is not { } mask)
        {
            return;
        }

        BrushStamp.Stamp(
            point,
            context.Settings.BrushSize,
            BrushShape.Square,
            (x, y) => SelectionTools.SetPixel(mask, x, y, _mode));

        // Published as the drag goes so the artist sees the marks appear. The bounds are put
        // right once, on release, because subtracting is the only thing that can leave them
        // wrong and rescanning per pixel would be absurd.
        if (ReferenceEquals(context.Workspace.Selection, mask))
        {
            context.Workspace.NotifySelectionChanged();
        }
        else
        {
            context.Workspace.Selection = mask;
        }
    }
}
