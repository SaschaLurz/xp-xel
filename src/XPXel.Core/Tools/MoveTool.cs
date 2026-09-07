using XPXel.Core.Documents;
using XPXel.Core.History;
using XPXel.Core.Primitives;

namespace XPXel.Core.Tools;

/// <summary>
/// Grabs everything on the active layer and slides it around the canvas.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it is for.</b> Text placed a little too high, a character that needs to be two pixels
/// left, a background that was drawn off-centre. Selecting all of it and dragging works, but the
/// thing an artist actually means is "move this layer", and that should not require making a
/// selection first.
/// </para>
/// <para>
/// <b>Nothing is lost over the edge.</b> Whatever leaves the canvas stays on the layer, hidden
/// rather than cut, so a layer nudged too far can simply be nudged back — see
/// <see cref="MoveLayerContentCommand"/>. It is what a slide editor does with a shape dragged
/// half off the slide, and it is the only behaviour that makes a rough drag safe.
/// </para>
/// <para>
/// <b>The drag moves the cel's offset; the release moves the cel.</b> Shifting an offset costs
/// nothing, so the preview keeps up on a large canvas, where reallocating the buffer on every
/// pointer sample would not. The single allocation happens once, on release, inside the one
/// history entry the whole drag produces.
/// </para>
/// <para>
/// <b>It moves the layer, wherever the press landed.</b> Requiring the artist to press on an
/// opaque pixel would make a layer of thin outlines nearly impossible to grab, and asking "which
/// layer did you mean?" is what the layers panel is for.
/// </para>
/// <para>
/// <b>A selection narrows what "this" means.</b> Having marked some pixels, an artist who
/// reaches for the move tool means those pixels and not the layer they sit on — moving the whole
/// layer at that point would silently undo the work of making the selection. So when something
/// is marked the press is handed to <see cref="Editing.SelectionTransform"/> instead, exactly as
/// a press inside the marquee is on every selection tool, and only an empty selection moves the
/// layer as a whole.
/// </para>
/// </remarks>
public sealed class MoveTool : ITool
{
    /// <summary>The stable identifier of this tool.</summary>
    public const string ToolId = "xpxel.tool.move";

    /// <summary>What the Edit menu calls a completed drag.</summary>
    private const string ActionName = "Move Layer";

    private Cel? _cel;
    private ImagePoint _originalOffset;
    private ImagePoint _anchor;
    private bool _isDragging;

    /// <inheritdoc />
    public string Id => ToolId;

    /// <inheritdoc />
    public string DisplayName => "Move";

    /// <inheritdoc />
    public string Description =>
        "Drag to move the marked pixels, or the whole layer when nothing is marked. What goes past the edge is kept, not cut.";

    /// <inheritdoc />
    public void OnPointerPressed(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(e);

        if (e.Button != ToolPointerButton.Left || !context.Workspace.CanEditActiveLayer)
        {
            return;
        }

        // Marked pixels are what the artist means by "this", so they are lifted and the layer
        // is left where it is. The canvas notices the transform appearing and turns the press
        // it has already delivered into a drag of the lifted artwork, so the move starts on
        // this gesture rather than on the next one.
        if (context.Workspace.Selection is { IsEmpty: false } && context.Workspace.BeginTransform())
        {
            return;
        }

        // A layer with nothing on this frame has nothing to move. No cel is created for the
        // attempt: an empty cel left behind on every stray click would bloat the file.
        if (context.Workspace.FindActiveCel() is not { } cel)
        {
            return;
        }

        _cel = cel;
        _originalOffset = cel.Offset;
        _anchor = e.Position;
        _isDragging = true;
    }

    /// <inheritdoc />
    public void OnPointerMoved(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(e);

        if (!_isDragging || _cel is not { } cel)
        {
            return;
        }

        cel.Offset = _originalOffset.Translate(e.Position.X - _anchor.X, e.Position.Y - _anchor.Y);

        // The whole canvas, because the layer has moved as a whole and the region it vacated
        // needs repainting just as much as the one it arrived in.
        context.Document.NotifyPixelsChanged(context.Document.Bounds);
    }

    /// <inheritdoc />
    public void OnPointerReleased(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(e);

        if (!_isDragging || _cel is not { } cel)
        {
            return;
        }

        _isDragging = false;
        _cel = null;

        ImagePoint delta = new(e.Position.X - _anchor.X, e.Position.Y - _anchor.Y);

        // The preview was a borrowed offset, so it is handed back before the real move runs.
        // Otherwise the command would move the layer a second time, from where the drag left it.
        cel.Offset = _originalOffset;

        if (delta == ImagePoint.Origin || context.Workspace.ActiveLayer is not { } layer)
        {
            context.Document.NotifyPixelsChanged(context.Document.Bounds);
            return;
        }

        context.History.Execute(new MoveLayerContentCommand(
            ActionName,
            context.Document,
            layer,
            context.Workspace.ActiveFrame,
            delta));
    }

    /// <inheritdoc />
    public void Cancel(ToolContext context)
    {
        if (_cel is { } cel)
        {
            cel.Offset = _originalOffset;
            context?.Document.NotifyPixelsChanged(context.Document.Bounds);
        }

        _isDragging = false;
        _cel = null;
    }
}
