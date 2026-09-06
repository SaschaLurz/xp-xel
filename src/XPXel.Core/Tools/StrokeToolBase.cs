using XPXel.Core.Drawing;
using XPXel.Core.History;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;

namespace XPXel.Core.Tools;

/// <summary>
/// Shared implementation for tools that paint along the pointer's path — the pencil, the
/// brush and the eraser today, and any future airbrush, smudge or dither brush.
/// </summary>
/// <remarks>
/// <para>
/// Subclasses supply only a name and the colour to lay down; everything else — starting the
/// undo session, stabilising the path, interpolating between pointer samples, stamping the
/// brush, publishing interim repaints, committing at mouse-up, and cleaning up on cancel —
/// is handled here. That keeps the tricky parts in one tested place rather than copied per
/// tool.
/// </para>
/// <para>
/// <b>Brush softness is opt-in.</b> It is honoured only by tools that return
/// <see langword="true"/> from <see cref="SupportsBrushDynamics"/>. The pencil returns
/// <see langword="false"/> on purpose: its entire job is to put one exact colour in one exact
/// pixel at the position the artist pointed at, and a soft edge would break that promise by
/// producing half-transparent pixels around it.
/// </para>
/// <para>
/// <b>Why coverage is remembered for the whole stroke.</b> A soft brush stamps a fading disc
/// at every interpolated point, and consecutive stamps overlap almost completely.
/// Compositing each one onto the result of the last would darken the stroke wherever the
/// pointer moved slowly, until a soft brush painted a hard line with fuzzy ends. Instead
/// every pixel remembers the highest coverage the stroke has reached, and each stamp
/// composites against the pixel's <em>pre-stroke</em> value, so passing over a pixel twice
/// looks exactly like passing over it once.
/// </para>
/// <para>
/// <b>Holding shift locks the stroke to one axis.</b> The axis is not chosen when shift goes
/// down — it is chosen by the first movement afterwards, because that is the moment the artist
/// has said which way they meant. Releasing shift re-arms the choice, so a horizontal run and
/// then a vertical one is shift, drag, let go, shift, drag, without ever lifting the button.
/// </para>
/// <para>
/// <b>Why the delegates are fields.</b> Stamping a brush calls back once per covered pixel,
/// and interpolating a fast drag calls back once per pixel of travel. Allocating a closure
/// at each of those call sites would produce thousands of short-lived objects per stroke, so
/// the callbacks are built once when the stroke begins and reused for its duration.
/// </para>
/// </remarks>
public abstract class StrokeToolBase : ITool
{
    /// <summary>Which way a shift-locked stroke is allowed to travel.</summary>
    private enum StrokeAxis
    {
        /// <summary>Shift is not held; the stroke goes wherever the pointer goes.</summary>
        Free,

        /// <summary>Shift is held, but the pointer has not yet said which way it is going.</summary>
        Undecided,

        /// <summary>Locked to a row.</summary>
        Horizontal,

        /// <summary>Locked to a column.</summary>
        Vertical,
    }

    /// <summary>
    /// The highest coverage each pixel has reached in this stroke, keyed by packed
    /// coordinate. Only populated for a soft brush; a hard brush needs no bookkeeping,
    /// because writing the same opaque colour twice is already idempotent.
    /// </summary>
    private readonly Dictionary<long, double> _coverage = [];

    private RasterEditSession? _session;
    private ImagePoint _lastPoint;
    private PixelColor _color;
    private int _brushSize = 1;
    private BrushShape _brushShape;
    private double _softness;
    private Action<int, int>? _stampBrush;
    private Action<int, int, double>? _applyCoverage;
    private StrokeAxis _axis;
    private ImagePoint _axisAnchor;

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
    /// Gets a value indicating whether this tool honours the brush softness and stroke
    /// stabilisation settings. <see langword="false"/> by default, which is the correct
    /// answer for a pencil; brush-like tools override it.
    /// </summary>
    protected virtual bool SupportsBrushDynamics => false;

    /// <summary>
    /// Returns the footprint this tool stamps. Defaults to whatever shape the artist chose.
    /// </summary>
    /// <param name="context">The document and settings being acted on.</param>
    /// <returns>The brush shape to stamp with.</returns>
    /// <remarks>
    /// Overridden by the pencil, which is square whatever the setting says; see
    /// <see cref="PencilTool"/> for why that is a promise rather than a limitation.
    /// </remarks>
    protected virtual BrushShape ResolveBrushShape(ToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Settings.BrushShape;
    }

    /// <summary>
    /// Returns the colour this tool lays down for the given button. The pencil returns a
    /// drawing colour; the eraser returns transparent.
    /// </summary>
    /// <param name="context">The document and settings being acted on.</param>
    /// <param name="button">The button that started the stroke.</param>
    /// <returns>The colour to write.</returns>
    protected abstract PixelColor ResolveStrokeColor(ToolContext context, ToolPointerButton button);

    /// <summary>
    /// Writes one pixel of the stroke, at the coverage the brush gives it.
    /// </summary>
    /// <param name="session">The session to write through.</param>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    /// <param name="color">The stroke colour.</param>
    /// <param name="coverage">How much of the pixel the brush covers, above 0 and up to 1.</param>
    /// <remarks>
    /// Compositing is against <see cref="RasterEditSession.GetOriginalPixel"/> rather than
    /// the pixel's current value; see the type remarks for why that matters.
    /// </remarks>
    protected virtual void ApplyStrokePixel(
        RasterEditSession session,
        int x,
        int y,
        PixelColor color,
        double coverage)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (coverage >= 1.0)
        {
            session.SetPixel(x, y, color);
            return;
        }

        PixelColor faded = color.WithAlpha((byte)Math.Round(color.A * coverage));
        session.SetPixel(x, y, ColorBlending.SourceOver(faded, session.GetOriginalPixel(x, y)));
    }

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

        bool dynamics = SupportsBrushDynamics;

        _session = session;
        _color = ResolveStrokeColor(context, e.Button);
        _brushSize = context.Settings.BrushSize;
        _brushShape = ResolveBrushShape(context);
        _softness = dynamics ? context.Settings.BrushSoftness / 100.0 : 0.0;
        _stampBrush = StampAt;
        _applyCoverage = ApplyCoverage;

        _lastPoint = e.Position;

        StampAt(e.Position.X, e.Position.Y);
        session.PublishInterimChanges();
    }

    /// <inheritdoc />
    public void OnPointerMoved(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (_session is null || _stampBrush is null)
        {
            return;
        }

        ImagePoint target = ApplyAxisLock(e.Position, e.IsShiftDown);

        if (target == _lastPoint)
        {
            return;
        }

        // Pointer samples can be many pixels apart on a fast drag; joining them keeps the
        // stroke continuous instead of dotted.
        LineRasterizer.PlotLine(_lastPoint, target, _stampBrush);
        _lastPoint = target;
        _session.PublishInterimChanges();
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
    /// Projects a point onto the axis that a held shift key has locked the stroke to.
    /// </summary>
    /// <param name="target">Where the pointer wants the stroke to go.</param>
    /// <param name="shiftHeld">Whether shift was down for this sample.</param>
    /// <returns>The point the stroke may actually reach.</returns>
    /// <remarks>
    /// The anchor is where the stroke had got to when shift went down, not where the stroke
    /// began: locking back to the start would jump the line across everything drawn since.
    /// Releasing shift is what re-arms the choice of axis, which is how the artist turns a
    /// corner without lifting the button.
    /// </remarks>
    private ImagePoint ApplyAxisLock(ImagePoint target, bool shiftHeld)
    {
        if (!shiftHeld)
        {
            _axis = StrokeAxis.Free;
            return target;
        }

        if (_axis == StrokeAxis.Free)
        {
            _axisAnchor = _lastPoint;
            _axis = StrokeAxis.Undecided;
        }

        int dx = target.X - _axisAnchor.X;
        int dy = target.Y - _axisAnchor.Y;

        if (_axis == StrokeAxis.Undecided)
        {
            // Nothing has moved yet, so there is still nothing to infer a direction from.
            if (dx == 0 && dy == 0)
            {
                return _axisAnchor;
            }

            _axis = Math.Abs(dx) >= Math.Abs(dy) ? StrokeAxis.Horizontal : StrokeAxis.Vertical;
        }

        return _axis == StrokeAxis.Horizontal
            ? new ImagePoint(target.X, _axisAnchor.Y)
            : new ImagePoint(_axisAnchor.X, target.Y);
    }

    /// <summary>
    /// Stamps the brush centred on one point, feeding every covered pixel through
    /// <see cref="ApplyStrokePixel"/>.
    /// </summary>
    private void StampAt(int x, int y)
    {
        if (_session is not { } session)
        {
            return;
        }

        if (_softness <= 0.0)
        {
            // The hard path skips the coverage bookkeeping entirely, which is what a pencil
            // takes for every pixel of every stroke.
            BrushStamp.Stamp(
                new ImagePoint(x, y),
                _brushSize,
                _brushShape,
                (px, py) => ApplyStrokePixel(session, px, py, _color, 1.0));

            return;
        }

        BrushStamp.StampSoft(new ImagePoint(x, y), _brushSize, _brushShape, _softness, _applyCoverage!);
    }

    private void ApplyCoverage(int x, int y, double coverage)
    {
        if (_session is not { } session)
        {
            return;
        }

        long key = ((long)y << 32) | (uint)x;

        // Only ever increase a pixel's coverage. A stamp that would leave the pixel lighter
        // than the stroke has already made it is not a change the artist asked for.
        if (_coverage.TryGetValue(key, out double existing) && coverage <= existing)
        {
            return;
        }

        _coverage[key] = coverage;
        ApplyStrokePixel(session, x, y, _color, coverage);
    }

    private void Reset()
    {
        _session = null;
        _stampBrush = null;
        _applyCoverage = null;
        _coverage.Clear();
        _axis = StrokeAxis.Free;
    }
}
