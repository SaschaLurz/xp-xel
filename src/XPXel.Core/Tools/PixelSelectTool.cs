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
/// selection directly, at whatever brush size is set.
/// </para>
/// <para>
/// <b>It adds rather than replaces.</b> Every press extends the mark, because a selection built
/// pixel by pixel is built over several strokes and starting again on each one would make it
/// impossible. Alt takes pixels back out, which is the same convention the colour picker uses
/// for "the other meaning of this gesture".
/// </para>
/// <para>
/// <b>Pressing on pixels it has already marked picks them up</b> — see
/// <see cref="SelectionTools"/>. That is the same gesture the marquee has, deliberately: which
/// tool did the marking should not change how the marked pixels are moved.
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

    private ImagePoint _lastPoint;
    private bool _isMarking;
    private bool _isErasing;

    /// <inheritdoc />
    public string Id => ToolId;

    /// <inheritdoc />
    public string DisplayName => "Pixel Select";

    /// <inheritdoc />
    public string Description =>
        "Click or drag to mark individual pixels; hold Alt to unmark. Press on marked pixels to pick them up and move them.";

    /// <inheritdoc />
    public void OnPointerPressed(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(e);

        if (e.Button != ToolPointerButton.Left)
        {
            return;
        }

        // Pressing on what is already marked means "move this", not "mark it again".
        if (!e.IsAltDown && SelectionTools.TryBeginMove(context, e.Position))
        {
            return;
        }

        _isMarking = true;
        _isErasing = e.IsAltDown;
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

        if (context.Workspace.Selection is not { } mask)
        {
            return;
        }

        // Unmarking can only ever have left the bounds too large, so they are recomputed once
        // here rather than on every pixel of the drag.
        mask.RecalculateBounds();

        // An empty mask and no mask are the same thing to every tool that reads it, but only
        // the second says so; leaving an empty one behind would clip every drawing tool to
        // nothing, which looks exactly like the editor having stopped working.
        if (mask.IsEmpty)
        {
            context.Workspace.Selection = null;
        }
        else
        {
            context.Workspace.NotifySelectionChanged();
        }
    }

    /// <inheritdoc />
    public void Cancel(ToolContext context) => _isMarking = false;

    /// <summary>Marks or unmarks the brush's footprint around one point.</summary>
    private void Mark(ToolContext context, ImagePoint point)
    {
        SelectionMask mask = context.Workspace.Selection is { } existing && existing.Size == context.Document.Size
            ? existing
            : new SelectionMask(context.Document.Size);

        byte coverage = _isErasing ? (byte)0 : (byte)255;

        BrushStamp.Stamp(
            point,
            context.Settings.BrushSize,
            BrushShape.Square,
            (x, y) => mask.SetCoverage(x, y, coverage));

        if (ReferenceEquals(context.Workspace.Selection, mask))
        {
            // The same mask, mutated in place. Assigning it back would raise nothing, because
            // the property compares references.
            context.Workspace.NotifySelectionChanged();
        }
        else
        {
            context.Workspace.Selection = mask;
        }
    }
}
