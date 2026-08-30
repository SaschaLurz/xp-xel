using XPXel.Core.Drawing;
using XPXel.Core.History;
using XPXel.Core.Primitives;

namespace XPXel.Core.Tools;

/// <summary>
/// Shared implementation for tools that paint along the pointer's path — the pencil and the
/// eraser today, and any future airbrush, smudge or dither brush.
/// </summary>
/// <remarks>
/// <para>
/// Subclasses supply only a name and the colour to lay down; everything else — starting the
/// undo session, interpolating between pointer samples, stamping the brush, publishing
/// interim repaints, committing at mouse-up, and cleaning up on cancel — is handled here.
/// That keeps the tricky parts in one tested place rather than copied per tool.
/// </para>
/// <para>
/// <b>Why the delegates are fields.</b> Stamping a brush calls back once per covered pixel,
/// and interpolating a fast drag calls back once per pixel of travel. Allocating a closure
/// at each of those call sites would produce thousands of short-lived objects per stroke, so
/// the two callbacks are built once when the stroke begins and reused for its duration.
/// </para>
/// <para>
/// <b>Not yet implemented:</b> holding Shift to constrain a stroke to a straight line.
/// <see cref="ToolPointerEventArgs.IsShiftDown"/> is already plumbed through for it.
/// </para>
/// </remarks>
public abstract class StrokeToolBase : ITool
{
    private RasterEditSession? _session;
    private ImagePoint _lastPoint;
    private Action<int, int>? _plotPixel;
    private Action<int, int>? _stampBrush;

    /// <inheritdoc />
    public abstract string Id { get; }

    /// <inheritdoc />
    public abstract string DisplayName { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <summary>
    /// Gets a value indicating whether a stroke is currently in progress. Useful to
    /// subclasses that need to vary behaviour mid-stroke.
    /// </summary>
    protected bool IsStrokeInProgress => _session is not null;

    /// <summary>
    /// Returns the colour this tool lays down for the given button. The pencil returns a
    /// drawing colour; the eraser returns transparent.
    /// </summary>
    /// <param name="context">The document and settings being acted on.</param>
    /// <param name="button">The button that started the stroke.</param>
    /// <returns>The colour to write.</returns>
    protected abstract PixelColor ResolveStrokeColor(ToolContext context, ToolPointerButton button);

    /// <inheritdoc />
    public void OnPointerPressed(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(e);

        if (e.Button is not (ToolPointerButton.Left or ToolPointerButton.Right))
        {
            return;
        }

        // Defensive: a previous stroke should already have ended, but a lost pointer capture
        // can leave one open, and silently leaking a session would leak its undo data too.
        Cancel(context);

        RasterEditSession? session = context.BeginRasterEdit(DisplayName);
        if (session is null)
        {
            return;
        }

        PixelColor color = ResolveStrokeColor(context, e.Button);
        int brushSize = context.Settings.BrushSize;
        BrushShape brushShape = context.Settings.BrushShape;

        _session = session;
        _plotPixel = (x, y) => session.SetPixel(x, y, color);
        _stampBrush = (x, y) => BrushStamp.Stamp(new ImagePoint(x, y), brushSize, brushShape, _plotPixel);
        _lastPoint = e.Position;

        _stampBrush(e.Position.X, e.Position.Y);
        session.PublishInterimChanges();
    }

    /// <inheritdoc />
    public void OnPointerMoved(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (_session is null || _stampBrush is null || e.Position == _lastPoint)
        {
            return;
        }

        // Pointer samples can be many pixels apart on a fast drag; joining them keeps the
        // stroke continuous instead of dotted.
        LineRasterizer.PlotLine(_lastPoint, e.Position, _stampBrush);
        _lastPoint = e.Position;
        _session.PublishInterimChanges();
    }

    /// <inheritdoc />
    public void OnPointerReleased(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_session is null)
        {
            return;
        }

        context.CommitRasterEdit(_session);
        Reset();
    }

    /// <inheritdoc />
    public void Cancel(ToolContext context)
    {
        _session?.Rollback();
        Reset();
    }

    private void Reset()
    {
        _session = null;
        _plotPixel = null;
        _stampBrush = null;
    }
}
