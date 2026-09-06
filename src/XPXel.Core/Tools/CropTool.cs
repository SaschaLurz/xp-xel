using XPXel.Core.Drawing;
using XPXel.Core.History;
using XPXel.Core.Primitives;

namespace XPXel.Core.Tools;

/// <summary>
/// Drags out the rectangle to keep, and throws away everything outside it.
/// </summary>
/// <remarks>
/// <para>
/// <b>It crops on release rather than asking for confirmation.</b> Photoshop's crop puts up
/// handles and waits for Enter, which is the right design when a crop is slow to undo. Here it
/// is one entry on the undo stack that restores every layer and every frame exactly, so the
/// faster gesture is the better one: drag, let go, and press Ctrl+Z if it was wrong.
/// </para>
/// <para>
/// <b>A click without a drag does nothing.</b> Cropping to a single pixel is never what
/// somebody meant by a stray click, and it is the one mistake this tool could make that would
/// be alarming to look at.
/// </para>
/// <para>
/// <b>The selection is cleared afterwards.</b> A mask is allocated for the canvas it was made
/// on, so it cannot survive a resize; keeping it would leave every tool testing coordinates
/// against a mask of the wrong shape.
/// </para>
/// </remarks>
public sealed class CropTool : ITool
{
    /// <summary>The stable identifier of this tool.</summary>
    public const string ToolId = "xpxel.tool.crop";

    private ImagePoint _anchor;
    private bool _isDragging;

    /// <inheritdoc />
    public string Id => ToolId;

    /// <inheritdoc />
    public string DisplayName => "Crop";

    /// <inheritdoc />
    public string Description =>
        "Drag the part of the picture to keep; everything outside it is trimmed away. Hold Shift for a square. One Ctrl+Z puts it all back.";

    /// <inheritdoc />
    public void OnPointerPressed(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(e);

        if (e.Button != ToolPointerButton.Left)
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

        if (_isDragging)
        {
            context.Overlay.Marquee = BuildRect(context, e);
        }
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

        if (region.Width <= 1 && region.Height <= 1)
        {
            return;
        }

        ResizeCanvasCommand command = new(
            "Crop",
            context.Document,
            region.Size,
            new ImagePoint(-region.X, -region.Y));

        if (!command.HasChanges)
        {
            return;
        }

        context.Workspace.History.Execute(command);
        context.Workspace.Selection = null;
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
