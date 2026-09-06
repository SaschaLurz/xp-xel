using XPXel.Core.Drawing;
using XPXel.Core.Primitives;
using XPXel.Core.Selection;

namespace XPXel.Core.Tools;

/// <summary>
/// Marks the whole area of one colour that a click lands on.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it is for.</b> Selecting a face, a shirt, a patch of sky — anything that was drawn
/// as one flat colour — by clicking it once. Marking that by hand with the pixel tool is
/// possible and nobody wants to do it.
/// </para>
/// <para>
/// <b>It reads the active layer, not the flattened picture.</b> Whatever is marked is what the
/// artist will then move, recolour or erase, and all of those happen on the active layer. A
/// wand that marked what was <em>visible</em> would hand back a selection full of pixels that
/// are not on the layer being edited, and the move that followed would come up empty. When the
/// active layer holds nothing on this frame there is nothing to walk, and the click marks
/// nothing.
/// </para>
/// <para>
/// <b>It shares the fill tool's options</b> — tolerance, and whether the area has to be joined
/// up. They are the same question asked of the same algorithm, so an artist who has set a
/// tolerance to fill an anti-aliased edge does not have to set it again to select one.
/// </para>
/// <para>
/// Shift adds to what is already marked and Control takes away from it, exactly as on every
/// other selection tool; see <see cref="SelectionTools.GetEditMode"/>.
/// </para>
/// </remarks>
public sealed class MagicWandTool : ITool
{
    /// <summary>The stable identifier of this tool.</summary>
    public const string ToolId = "xpxel.tool.select.wand";

    /// <inheritdoc />
    public string Id => ToolId;

    /// <inheritdoc />
    public string DisplayName => "Magic Wand";

    /// <inheritdoc />
    public string Description =>
        "Click an area to mark every pixel of that colour; hold Shift to add, Control to take away.";

    /// <inheritdoc />
    public void OnPointerPressed(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(e);

        if (e.Button != ToolPointerButton.Left)
        {
            return;
        }

        SelectionEditMode mode = SelectionTools.GetEditMode(e);

        // Pressing on what is already marked means "move this", the same as on the marquee —
        // unless a modifier says the artist is still marking.
        if (mode == SelectionEditMode.Replace && SelectionTools.TryBeginMove(context, e.Position))
        {
            return;
        }

        if (context.Workspace.FindActiveCel() is not { } cel)
        {
            return;
        }

        SelectionMask mask = SelectionTools.GetTargetMask(context, mode);

        // The flood fill walks the cel's own buffer, which may be larger than the canvas and
        // may sit at an offset once a layer has been moved; the mask is in document space.
        ImagePoint origin = cel.Offset;
        ImagePoint start = new(e.Position.X - origin.X, e.Position.Y - origin.Y);

        FloodFill.Fill(
            cel.Pixels,
            start,
            context.Settings.FillTolerance,
            context.Settings.IsFillContiguous,
            canVisit: null,
            (x, y) => SelectionTools.SetPixel(mask, x + origin.X, y + origin.Y, mode));

        SelectionTools.Publish(context, mask);
    }

    /// <inheritdoc />
    public void OnPointerMoved(ToolContext context, ToolPointerEventArgs e)
    {
        // A wand is a click; dragging it does nothing.
    }

    /// <inheritdoc />
    public void OnPointerReleased(ToolContext context, ToolPointerEventArgs e)
    {
        // Everything happens on press.
    }

    /// <inheritdoc />
    public void Cancel(ToolContext context)
    {
        // Nothing is held between events, so there is nothing to cancel.
    }
}
