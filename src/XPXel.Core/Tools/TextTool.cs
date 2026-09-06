using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.Primitives;
using XPXel.Core.Text;

namespace XPXel.Core.Tools;

/// <summary>
/// Places and edits text. Clicking empty canvas starts a new text layer; clicking existing
/// text puts the caret in it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The tool only handles the pointer.</b> Typing is driven through
/// <see cref="TextEditSession"/> on the workspace, because keystrokes arrive at the canvas
/// rather than here and because a tool is a singleton that must hold no document state. This
/// class decides <em>which</em> text is being edited; the session decides what happens to it.
/// </para>
/// <para>
/// <b>Hit-testing re-measures rather than caching.</b> A text layer's rendered bounds could
/// be remembered on the layer, but then every path that changes text, style, origin, undo or
/// reload would have to keep that cache honest, and the symptom of getting it wrong is a
/// click that edits the wrong layer. Measuring costs one layout of a short string, on a
/// click.
/// </para>
/// </remarks>
public sealed class TextTool : ITool
{
    /// <summary>The stable identifier of this tool.</summary>
    public const string ToolId = "xpxel.tool.text";

    private readonly ITextRasterizer _rasterizer;

    /// <summary>Initialises the tool.</summary>
    /// <param name="rasterizer">The font engine used to measure and render text.</param>
    public TextTool(ITextRasterizer rasterizer)
    {
        ArgumentNullException.ThrowIfNull(rasterizer);
        _rasterizer = rasterizer;
    }

    /// <inheritdoc />
    public string Id => ToolId;

    /// <inheritdoc />
    public string DisplayName => "Text";

    /// <inheritdoc />
    public string Description =>
        "Click to place text, or click existing text to edit it. Text layers stay editable; style them in the Text panel.";

    /// <inheritdoc />
    public void OnPointerPressed(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(e);

        if (e.Button != ToolPointerButton.Left)
        {
            return;
        }

        DocumentWorkspace workspace = context.Workspace;

        // Clicking inside the text already being edited just moves the caret, which is what
        // makes correcting a typo in the middle of a word possible.
        if (workspace.ActiveTextEdit is { } editing && HitTest(editing.Layer, e.Position))
        {
            editing.MoveCaretTo(e.Position);
            return;
        }

        if (FindTextLayerAt(context, e.Position) is { } existing)
        {
            workspace.ActiveLayer = existing;

            TextEditSession session = new(workspace, existing, _rasterizer, isNewLayer: false);
            workspace.BeginTextEdit(session);
            session.MoveCaretTo(e.Position);
            return;
        }

        StartNewText(context, e.Position);
    }

    /// <inheritdoc />
    public void OnPointerMoved(ToolContext context, ToolPointerEventArgs e)
    {
        // Dragging does nothing yet. Dragging a text block to reposition it is the natural
        // next step and would move the layer's Origin through the session.
    }

    /// <inheritdoc />
    public void OnPointerReleased(ToolContext context, ToolPointerEventArgs e)
    {
        // Everything happens on press; there is no drag to finish.
    }

    /// <inheritdoc />
    public void Cancel(ToolContext context)
    {
        // Deliberately empty. The canvas raises this after every ordinary click, because
        // releasing the pointer drops its capture, so ending the edit here would make it
        // impossible to type a single character. The edit ends when the artist presses
        // Escape, switches tool, or clicks elsewhere — see the canvas control.
    }

    private void StartNewText(ToolContext context, ImagePoint position)
    {
        DocumentWorkspace workspace = context.Workspace;

        TextStyle style = context.Settings.TextDefaults.Clone();

        // New text adopts the primary colour, the same way a new stroke does. An artist who
        // has just picked a colour expects the next thing they make to use it.
        style.Color = context.Colors.Primary;

        TextLayer layer = workspace.Document.CreateTextLayer(BuildLayerName(workspace.Document), style);
        layer.Origin = position;

        workspace.ActiveLayer = layer;
        workspace.BeginTextEdit(new TextEditSession(workspace, layer, _rasterizer, isNewLayer: true));
    }

    /// <summary>Finds the top-most visible text layer whose glyphs cover a point.</summary>
    private TextLayer? FindTextLayerAt(ToolContext context, ImagePoint position)
    {
        TextLayer? found = null;

        // EnumerateDescendants walks bottom-most first, so the last match is the top-most —
        // which is the one an artist means when text overlaps.
        foreach (LayerNode node in context.Document.EnumerateLayers())
        {
            if (node is TextLayer text && text.IsEffectivelyVisible && !text.IsLocked && HitTest(text, position))
            {
                found = text;
            }
        }

        return found;
    }

    private bool HitTest(TextLayer layer, ImagePoint position)
    {
        if (_rasterizer.Rasterize(layer.Text, layer.Style) is not { } raster)
        {
            // Empty text has no glyphs to hit, but it does have a caret, so a small target
            // around the origin keeps a text layer clickable while it is still empty.
            ImageRect empty = _rasterizer
                .MeasureCaret(layer.Text, layer.Style, 0)
                .Translate(layer.Origin.X, layer.Origin.Y);

            return empty.Inflate(2).Contains(position);
        }

        ImageRect bounds = new(
            new ImagePoint(layer.Origin.X + raster.Origin.X, layer.Origin.Y + raster.Origin.Y),
            raster.Pixels.Size);

        return bounds.Contains(position);
    }

    private static string BuildLayerName(PixelDocument document)
    {
        int count = 1;

        foreach (LayerNode node in document.EnumerateLayers())
        {
            if (node is TextLayer)
            {
                count++;
            }
        }

        return $"Text {count}";
    }
}
