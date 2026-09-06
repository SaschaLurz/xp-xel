using XPXel.Core.Drawing;
using XPXel.Core.History;
using XPXel.Core.Primitives;

namespace XPXel.Core.Tools;

/// <summary>
/// Fills a contiguous area of similar colour with the drawing colour.
/// </summary>
/// <remarks>
/// <para>
/// The whole operation happens on button-down: unlike a stroke there is nothing to
/// accumulate as the pointer moves, so the fill runs and commits its history entry
/// immediately.
/// </para>
/// <para>
/// The session is opened in snapshot mode because a fill can legitimately cover the entire
/// canvas, and copying the cel once is cheaper than recording several hundred thousand
/// individual pixel values.
/// </para>
/// <para>
/// The fill reads and writes the <em>active layer only</em>, not the flattened image. That
/// is what artists expect: filling a region on an upper layer must not be stopped by lines
/// drawn on a layer beneath it. A "sample all layers" option is the natural future addition
/// and would supply a different buffer to <see cref="FloodFill"/> while still writing to the
/// active cel.
/// </para>
/// </remarks>
public sealed class BucketFillTool : ITool
{
    /// <summary>The stable identifier of this tool.</summary>
    public const string ToolId = "xpxel.tool.bucket";

    /// <inheritdoc />
    public string Id => ToolId;

    /// <inheritdoc />
    public string DisplayName => "Bucket Fill";

    /// <inheritdoc />
    public string Description => "Flood fill an area of matching colour.";

    /// <inheritdoc />
    public void OnPointerPressed(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(e);

        if (e.Button is not (ToolPointerButton.Left or ToolPointerButton.Right))
        {
            return;
        }

        if (!context.Document.Bounds.Contains(e.Position))
        {
            return;
        }

        RasterEditSession? session = context.BeginRasterEdit(DisplayName, useSnapshot: true);
        if (session is null)
        {
            return;
        }

        PixelColor color = context.GetColorForButton(e.Button);

        // The fill walks the cel's own buffer, which sits at an offset once its layer has been
        // moved; everything the artist and the selection speak of is in document coordinates,
        // so each pixel is converted on the way out.
        ImagePoint origin = session.Origin;

        // The selection gates traversal as well as writing, so a selection edge stops the
        // fill from leaking rather than merely preventing the write.
        Func<int, int, bool>? canVisit = context.Selection is not { } selection
            ? null
            : (x, y) => selection.IsSelected(x + origin.X, y + origin.Y);

        FloodFill.Fill(
            session.Pixels,
            new ImagePoint(e.Position.X - origin.X, e.Position.Y - origin.Y),
            context.Settings.FillTolerance,
            context.Settings.IsFillContiguous,
            canVisit,
            (x, y) => session.SetPixel(x + origin.X, y + origin.Y, color));

        context.CommitRasterEdit(session);
    }

    /// <inheritdoc />
    public void OnPointerMoved(ToolContext context, ToolPointerEventArgs e)
    {
        // A fill is instantaneous; dragging after the click does nothing.
    }

    /// <inheritdoc />
    public void OnPointerReleased(ToolContext context, ToolPointerEventArgs e)
    {
        // Committed already in OnPointerPressed.
    }

    /// <inheritdoc />
    public void Cancel(ToolContext context)
    {
        // Nothing is held between events, so there is nothing to cancel.
    }
}
