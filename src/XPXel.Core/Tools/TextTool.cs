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
/// <b>A drag means one of two things, decided by where it starts.</b> Inside the words it
/// selects characters, which is what dragging does in every text field ever built. On the border
/// of the box it moves the whole block, which is the answer to "I cannot move my text": the
/// alternative was switching to the move tool, and an artist who is still typing has no reason
/// to expect that they must.
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

    /// <summary>
    /// How near the box border a press counts as grabbing it, in screen pixels.
    /// </summary>
    /// <remarks>
    /// Measured on screen for the same reason the crop tool measures its grips there: how close
    /// a pointer has to come is a question about fingers and mice, and a tolerance in image
    /// pixels would be an invisible sliver at 32x and would swallow whole words at 1:1.
    /// </remarks>
    public const double BoxGripScreenDistance = 6.0;

    private readonly ITextRasterizer _rasterizer;

    private TextEditSession? _dragging;
    private ImagePoint _dragAnchor;
    private ImagePoint _dragOrigin;
    private bool _isMovingBlock;

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

        int tolerance = GetTolerance(context);

        if (workspace.ActiveTextEdit is { } editing)
        {
            // The border first: it overlaps the glyphs by a pixel or two, and there the artist
            // means the border, since selecting one character is a click away and repositioning
            // the block has nowhere else to be asked for.
            if (editing.IsOnBoxEdge(e.Position, tolerance))
            {
                BeginBlockMove(editing, e.Position);
                return;
            }

            // Clicking inside the text already being edited just moves the caret, which is what
            // makes correcting a typo in the middle of a word possible. The press also begins a
            // range: a drag from here selects characters, and a click that never moves selects
            // nothing, which is exactly the behaviour of a caret.
            if (HitTest(editing.Layer, e.Position))
            {
                editing.MoveCaretTo(e.Position);
                _dragging = editing;
                _isMovingBlock = false;
                return;
            }
        }

        if (FindTextLayerAt(context, e.Position) is { } existing)
        {
            workspace.ActiveLayer = existing;

            TextEditSession session = new(workspace, existing, _rasterizer, isNewLayer: false);
            workspace.BeginTextEdit(session);
            session.MoveCaretTo(e.Position);

            _dragging = session;
            _isMovingBlock = false;
            return;
        }

        StartNewText(context, e.Position);
    }

    /// <inheritdoc />
    public void OnPointerMoved(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (_dragging is not { } editing)
        {
            return;
        }

        if (_isMovingBlock)
        {
            editing.MoveOrigin(new ImagePoint(
                _dragOrigin.X + (e.Position.X - _dragAnchor.X),
                _dragOrigin.Y + (e.Position.Y - _dragAnchor.Y)));

            return;
        }

        // Extending from where the press put the caret, so the range grows and shrinks with the
        // pointer rather than being rebuilt from the character under it.
        editing.MoveCaretTo(e.Position, extend: true);
    }

    /// <inheritdoc />
    public void OnPointerReleased(ToolContext context, ToolPointerEventArgs e)
    {
        // The drag ends; the edit does not. The artist is still in the text, with a caret or a
        // selected range, and the session is closed by clicking away or switching tool.
        _dragging = null;
        _isMovingBlock = false;
    }

    /// <summary>Starts dragging a whole text block by its border.</summary>
    private void BeginBlockMove(TextEditSession editing, ImagePoint position)
    {
        _dragging = editing;
        _isMovingBlock = true;
        _dragAnchor = position;
        _dragOrigin = editing.Layer.Origin;
    }

    /// <summary>
    /// How far from the box border a press still counts as being on it, in image pixels.
    /// </summary>
    private static int GetTolerance(ToolContext context) =>
        Math.Max(1, (int)Math.Round(BoxGripScreenDistance / Math.Max(context.ViewScale, 0.01)));

    /// <inheritdoc />
    public void Cancel(ToolContext context)
    {
        // The drag is dropped, the edit is not. The canvas raises this after every ordinary
        // click, because releasing the pointer drops its capture, so ending the edit here would
        // make it impossible to type a single character. The edit ends when the artist presses
        // Escape, switches tool, or clicks elsewhere — see the canvas control.
        _dragging = null;
        _isMovingBlock = false;
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
    /// <param name="document">The document to search.</param>
    /// <param name="rasterizer">The font engine to measure with.</param>
    /// <param name="position">The point, in document coordinates.</param>
    /// <returns>The layer, or <see langword="null"/> if no text is there.</returns>
    /// <remarks>
    /// Public and static because the canvas asks the same question the tool does, when the
    /// artist double-clicks text while holding some other tool. Two implementations of "is
    /// there text here" would eventually disagree, and the symptom would be a double-click
    /// that opens a different layer from the one a click opens.
    /// </remarks>
    public static TextLayer? FindTextLayerAt(PixelDocument document, ITextRasterizer rasterizer, ImagePoint position)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(rasterizer);

        TextLayer? found = null;

        // EnumerateDescendants walks bottom-most first, so the last match is the top-most —
        // which is the one an artist means when text overlaps.
        foreach (LayerNode node in document.EnumerateLayers())
        {
            if (node is TextLayer text && text.IsEffectivelyVisible && !text.IsLocked
                && HitTest(rasterizer, text, position))
            {
                found = text;
            }
        }

        return found;
    }

    private TextLayer? FindTextLayerAt(ToolContext context, ImagePoint position) =>
        FindTextLayerAt(context.Document, _rasterizer, position);

    private bool HitTest(TextLayer layer, ImagePoint position) =>
        HitTest(_rasterizer, layer, position);

    private static bool HitTest(ITextRasterizer rasterizer, TextLayer layer, ImagePoint position)
    {
        if (rasterizer.Rasterize(layer.Text, layer.Style) is not { } raster)
        {
            // Empty text has no glyphs to hit, but it does have a caret, so a small target
            // around the origin keeps a text layer clickable while it is still empty.
            ImageRect empty = rasterizer
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
