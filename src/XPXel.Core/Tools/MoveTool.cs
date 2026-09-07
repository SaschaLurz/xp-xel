using XPXel.Core.Documents;
using XPXel.Core.History;
using XPXel.Core.Primitives;
using XPXel.Core.Text;

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
/// <b>Text moves as text.</b> A text layer keeps its pixels in a cel like everything else, but
/// those pixels are a cache regenerated from the words, so sliding the cel would put the glyphs
/// back where they started the next time anything re-rendered them. Dragging a text layer moves
/// its origin instead and re-renders from there, which is why text could not be moved at all
/// before: the drag was moving the one thing about a text layer that does not decide where it
/// appears.
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
    private TextLayer? _text;
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

        // A text layer is not something CanEditActiveLayer admits to — nothing may draw on one —
        // but it is very much something that can be moved, so it is asked about separately.
        bool movableText = context.Workspace.ActiveLayer is TextLayer { IsLocked: false } movable
            && movable.IsEffectivelyVisible;

        if (e.Button != ToolPointerButton.Left || (!context.Workspace.CanEditActiveLayer && !movableText))
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

        // Text is dragged by its origin, for the reason in the type remarks.
        if (context.Workspace.ActiveLayer is TextLayer { IsLocked: false } text && text.IsEffectivelyVisible)
        {
            _text = text;
            _originalOffset = text.Origin;
            _anchor = e.Position;
            _isDragging = true;
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

        if (!_isDragging)
        {
            return;
        }

        ImagePoint moved = _originalOffset.Translate(e.Position.X - _anchor.X, e.Position.Y - _anchor.Y);

        if (_text is { } text)
        {
            text.Origin = moved;
            TextLayerRenderer.Render(context.Document, text, context.Workspace.ActiveFrame, context.TextRasterizer);
        }
        else if (_cel is { } cel)
        {
            cel.Offset = moved;
        }
        else
        {
            return;
        }

        // The whole canvas, because the layer has moved as a whole and the region it vacated
        // needs repainting just as much as the one it arrived in.
        context.Document.NotifyPixelsChanged(context.Document.Bounds);
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

        TextLayer? text = _text;
        Cel? cel = _cel;

        _text = null;
        _cel = null;

        ImagePoint delta = new(e.Position.X - _anchor.X, e.Position.Y - _anchor.Y);

        if (text is not null)
        {
            CommitTextMove(context, text, delta);
            return;
        }

        if (cel is null)
        {
            return;
        }

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

        if (_text is { } text && context is not null)
        {
            text.Origin = _originalOffset;
            TextLayerRenderer.Render(context.Document, text, context.Workspace.ActiveFrame, context.TextRasterizer);
            context.Document.NotifyPixelsChanged(context.Document.Bounds);
        }

        _isDragging = false;
        _cel = null;
        _text = null;
    }

    /// <summary>
    /// Records a finished drag of a text layer as one undoable step.
    /// </summary>
    /// <remarks>
    /// The origin is handed back before the command runs, for the same reason the cel offset is:
    /// the drag was a live preview, and a command that started from where the preview left off
    /// would move the words twice as far.
    /// </remarks>
    private void CommitTextMove(ToolContext context, TextLayer text, ImagePoint delta)
    {
        ImagePoint moved = _originalOffset.Translate(delta.X, delta.Y);

        text.Origin = _originalOffset;
        TextLayerRenderer.Render(context.Document, text, context.Workspace.ActiveFrame, context.TextRasterizer);

        if (delta == ImagePoint.Origin)
        {
            context.Document.NotifyPixelsChanged(context.Document.Bounds);
            return;
        }

        context.History.Execute(new SetTextCommand(
            ActionName,
            context.Document,
            text,
            context.Workspace.ActiveFrame,
            context.TextRasterizer,
            text.Text,
            text.Style,
            _originalOffset,
            text.Text,
            text.Style,
            moved));
    }
}
