using XPXel.Core.Drawing;
using XPXel.Core.History;
using XPXel.Core.Primitives;

namespace XPXel.Core.Tools;

/// <summary>
/// Shared implementation for tools that are dragged out between two corners — the line,
/// rectangle and ellipse today, and any future arrow, polygon or gradient tool.
/// </summary>
/// <remarks>
/// <para>
/// <b>The preview is the real thing.</b> A shape tool draws its shape onto the actual layer
/// on every pointer move rather than onto a separate overlay, and undoes the previous draw
/// first. That means what the artist sees while dragging is exactly what they will get —
/// correct blend mode, correct opacity, correctly clipped by the selection — with no second
/// rendering path to keep in agreement with the first.
/// </para>
/// <para>
/// The mechanism is <see cref="RasterEditSession.ResetToOriginal"/>: one session is opened
/// on button-down and kept open for the whole drag, restoring the original pixels before each
/// redraw. Because it is one session, the drag produces exactly one undo entry no matter how
/// far the pointer travelled.
/// </para>
/// <para>
/// The session uses the snapshot strategy, because a shape dragged across the canvas can
/// touch every pixel on the layer and per-pixel bookkeeping would cost more than one copy of
/// the cel.
/// </para>
/// </remarks>
public abstract class ShapeToolBase : ITool
{
    private RasterEditSession? _session;
    private ImagePoint _anchor;
    private ImagePoint _current;
    private PixelColor _color;
    private int _brushSize = 1;
    private BrushShape _brushShape;
    private bool _isFilled;
    private Action<int, int>? _plotStamped;

    /// <inheritdoc />
    public abstract string Id { get; }

    /// <inheritdoc />
    public abstract string DisplayName { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <summary>
    /// Gets a value indicating whether this shape can be filled. The line cannot; the
    /// rectangle and ellipse can.
    /// </summary>
    protected virtual bool SupportsFill => true;

    /// <summary>Plots the shape spanning two points.</summary>
    /// <param name="anchor">Where the drag started.</param>
    /// <param name="current">Where the pointer is now, already constrained.</param>
    /// <param name="isFilled">Whether to fill the shape.</param>
    /// <param name="plot">Receives the x and y of each covered pixel.</param>
    protected abstract void PlotShape(ImagePoint anchor, ImagePoint current, bool isFilled, Action<int, int> plot);

    /// <summary>
    /// Applies the Shift constraint to a dragged point. Shapes constrain to a square by
    /// default; the line overrides this to snap to 45° steps instead.
    /// </summary>
    /// <param name="anchor">Where the drag started.</param>
    /// <param name="current">Where the pointer is now.</param>
    /// <returns>The constrained position.</returns>
    protected virtual ImagePoint Constrain(ImagePoint anchor, ImagePoint current) =>
        ShapeRasterizer.ConstrainToSquare(anchor, current);

    /// <inheritdoc />
    public void OnPointerPressed(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(e);

        if (e.Button is not (ToolPointerButton.Left or ToolPointerButton.Right))
        {
            return;
        }

        Cancel(context);

        RasterEditSession? session = context.BeginRasterEdit(DisplayName, useSnapshot: true);
        if (session is null)
        {
            return;
        }

        _session = session;
        _anchor = e.Position;
        _current = e.Position;
        _color = context.GetColorForButton(e.Button);
        _brushSize = context.Settings.BrushSize;
        _brushShape = context.Settings.BrushShape;
        _isFilled = SupportsFill && context.Settings.IsShapeFilled;

        // The outline is stroked with the brush so that a thick line stays thick when it is a
        // rectangle's edge. A fill has no outline to thicken and writes pixels directly.
        _plotStamped = (x, y) => BrushStamp.Stamp(
            new ImagePoint(x, y),
            _brushSize,
            _brushShape,
            (px, py) => session.SetPixel(px, py, _color));

        Redraw();
    }

    /// <inheritdoc />
    public void OnPointerMoved(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (_session is null)
        {
            return;
        }

        ImagePoint target = e.IsShiftDown ? Constrain(_anchor, e.Position) : e.Position;
        if (target == _current)
        {
            return;
        }

        _current = target;
        Redraw();
    }

    /// <inheritdoc />
    public void OnPointerReleased(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(e);

        if (_session is not { } session)
        {
            return;
        }

        ImagePoint target = e.IsShiftDown ? Constrain(_anchor, e.Position) : e.Position;
        if (target != _current)
        {
            _current = target;
            Redraw();
        }

        context.CommitRasterEdit(session);
        Reset();
    }

    /// <inheritdoc />
    public void Cancel(ToolContext context)
    {
        _session?.Rollback();
        Reset();
    }

    /// <summary>
    /// Erases the previous preview and draws the shape at its current size.
    /// </summary>
    private void Redraw()
    {
        if (_session is not { } session || _plotStamped is null)
        {
            return;
        }

        session.ResetToOriginal();

        PlotShape(
            _anchor,
            _current,
            _isFilled,
            _isFilled ? (x, y) => session.SetPixel(x, y, _color) : _plotStamped);

        session.PublishInterimChanges();
    }

    private void Reset()
    {
        _session = null;
        _plotStamped = null;
    }
}
