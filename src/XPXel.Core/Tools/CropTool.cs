using XPXel.Core.History;
using XPXel.Core.Primitives;

namespace XPXel.Core.Tools;

/// <summary>
/// Which part of a crop frame a point is over.
/// </summary>
/// <remarks>
/// Public because the canvas asks the tool this on every pointer move, so that the pointer can
/// say whether there is anything to take hold of before the artist presses. The edges combine —
/// <c>Left | Top</c> is the top-left corner — and <see cref="All"/> is the inside of the frame,
/// which drags the whole thing.
/// </remarks>
[Flags]
public enum CropGrip
{
    /// <summary>The point is outside the frame and not near an edge.</summary>
    None = 0,

    /// <summary>The left edge.</summary>
    Left = 1,

    /// <summary>The right edge.</summary>
    Right = 2,

    /// <summary>The top edge.</summary>
    Top = 4,

    /// <summary>The bottom edge.</summary>
    Bottom = 8,

    /// <summary>Inside the frame, which moves all four edges together.</summary>
    All = Left | Right | Top | Bottom,
}

/// <summary>
/// Puts a frame around the picture that can be dragged in to trim it or out to enlarge it,
/// and applies it on Enter.
/// </summary>
/// <remarks>
/// <para>
/// <b>The frame starts as the picture itself.</b> Choosing the tool shows the edges the canvas
/// already has, so the gesture is always "adjust what is there" rather than "draw a new
/// rectangle and hope you got the other three edges right". Dragging an edge outwards makes
/// the canvas larger, which is the same operation as trimming and is why one tool does both:
/// to an artist, "there is not enough room at the top" and "there is too much room at the top"
/// are the same thought.
/// </para>
/// <para>
/// <b>It waits for Enter.</b> Unlike a drag-and-release crop, a frame is something the artist
/// nudges several times before it is right, and applying on release would make each nudge a
/// separate resize of the document. Escape puts the frame back around the whole picture.
/// </para>
/// <para>
/// <b>The grips are measured on screen, not on the canvas.</b> How close the pointer must come
/// to an edge before it takes hold of it is a question about fingers and mice, so the tolerance
/// is a fixed number of screen pixels converted through <see cref="ToolContext.ViewScale"/>.
/// A tolerance in image pixels would be an invisible sliver at 32× and would swallow a whole
/// 16-pixel sprite at 1:1 — which is exactly the complaint that "you cannot grab anything".
/// </para>
/// <para>
/// <b>The frame is always whole pixels</b>, because it is measured in them: every coordinate
/// here is an <see cref="int"/>, so an edge cannot come to rest halfway through a pixel and the
/// resulting canvas cannot have a fractional size.
/// </para>
/// </remarks>
public sealed class CropTool : ITool, IInteractiveTool
{
    /// <summary>The stable identifier of this tool.</summary>
    public const string ToolId = "xpxel.tool.crop";

    /// <summary>
    /// How near an edge the pointer counts as grabbing it, in screen pixels.
    /// </summary>
    /// <remarks>
    /// Matched to the size the canvas draws its handles, so that everything that looks grabbable
    /// is grabbable and nothing else is.
    /// </remarks>
    public const double GripScreenDistance = 7.0;

    private ImageRect _frame;
    private ImageRect _dragStart;
    private ImagePoint _anchor;
    private CropGrip _grip;
    private bool _isDragging;

    /// <inheritdoc />
    public string Id => ToolId;

    /// <inheritdoc />
    public string DisplayName => "Crop";

    /// <inheritdoc />
    public string Description =>
        "Drag the frame's handles to trim the picture or to make it bigger, then press Enter. Escape puts the frame back.";

    /// <summary>Gets a value indicating whether the frame is being dragged right now.</summary>
    /// <remarks>
    /// Read by the canvas, which closes the pointer's hand while a drag is under way and opens
    /// it again on release. A grab that looks the same held as hovering says nothing about
    /// whether the press was actually taken.
    /// </remarks>
    public bool IsDragging => _isDragging;

    /// <summary>Reports what a point is over, without touching the frame.</summary>
    /// <param name="context">The document being acted on, for the zoom the tolerance is measured in.</param>
    /// <param name="point">The point to test, in image coordinates.</param>
    /// <returns>The part of the frame that a press there would take hold of.</returns>
    /// <remarks>
    /// This is the same question <see cref="OnPointerPressed"/> asks, asked a moment earlier and
    /// with no side effects, so that the pointer can promise exactly what the press will do.
    /// </remarks>
    public CropGrip HitTest(ToolContext context, ImagePoint point)
    {
        ArgumentNullException.ThrowIfNull(context);

        return _frame.IsEmpty ? CropGrip.None : GetGrip(point, GetTolerance(context));
    }

    /// <inheritdoc />
    public void OnActivated(ToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _frame = context.Document.Bounds;
        _isDragging = false;
        context.Overlay.CropFrame = _frame;
    }

    /// <inheritdoc />
    public void OnDeactivated(ToolContext context)
    {
        _isDragging = false;

        if (context is not null)
        {
            context.Overlay.CropFrame = null;
        }
    }

    /// <inheritdoc />
    public bool OnKeyDown(ToolContext context, ToolKey key)
    {
        ArgumentNullException.ThrowIfNull(context);

        switch (key)
        {
            case ToolKey.Enter:
                Apply(context);
                return true;

            case ToolKey.Escape:
                OnActivated(context);
                return true;

            default:
                return false;
        }
    }

    /// <inheritdoc />
    public void OnPointerPressed(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(e);

        if (e.Button != ToolPointerButton.Left)
        {
            return;
        }

        if (_frame.IsEmpty)
        {
            _frame = context.Document.Bounds;
        }

        _grip = GetGrip(e.Position, GetTolerance(context));
        _anchor = e.Position;
        _dragStart = _frame;
        _isDragging = true;

        if (_grip == CropGrip.None)
        {
            // Nowhere near the frame and not inside it: the artist is drawing a new one.
            _frame = new ImageRect(e.Position.X, e.Position.Y, 0, 0);
            _dragStart = _frame;
            _grip = CropGrip.Right | CropGrip.Bottom;
        }

        context.Overlay.CropFrame = _frame;
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

        int dx = e.Position.X - _anchor.X;
        int dy = e.Position.Y - _anchor.Y;

        _frame = _grip == CropGrip.All
            ? _dragStart.Translate(dx, dy)
            : Resize(_dragStart, _grip, dx, dy);

        context.Overlay.CropFrame = _frame;
    }

    /// <inheritdoc />
    public void OnPointerReleased(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);

        _isDragging = false;
        context.Overlay.CropFrame = _frame;
    }

    /// <inheritdoc />
    public void Cancel(ToolContext context) => _isDragging = false;

    /// <summary>Applies the frame, resizing the canvas to it.</summary>
    private void Apply(ToolContext context)
    {
        if (_frame.Width < 1 || _frame.Height < 1)
        {
            return;
        }

        ResizeCanvasCommand command = new(
            "Crop",
            context.Document,
            _frame.Size,
            new ImagePoint(-_frame.X, -_frame.Y));

        if (command.HasChanges)
        {
            context.Workspace.History.Execute(command);

            // A mask is allocated for the canvas it was made on, so it cannot survive a
            // resize; keeping it would leave every tool testing against a mask of the wrong
            // shape.
            context.Workspace.Selection = null;
        }

        // The frame becomes the new picture, so the tool is ready for another adjustment
        // rather than still showing where the old edges were.
        OnActivated(context);
    }

    /// <summary>
    /// How far from an edge a press still counts as grabbing it, in image pixels.
    /// </summary>
    /// <remarks>
    /// At least one pixel, so the frame is grabbable when zoomed so far out that a whole pixel is
    /// smaller than a fingertip — at that zoom the artist cannot aim at a pixel anyway.
    /// </remarks>
    private static int GetTolerance(ToolContext context) =>
        Math.Max(1, (int)Math.Round(GripScreenDistance / Math.Max(context.ViewScale, 0.01)));

    /// <summary>Works out what a press at a point is asking to drag.</summary>
    private CropGrip GetGrip(ImagePoint point, int tolerance)
    {
        bool nearLeft = Math.Abs(point.X - _frame.Left) <= tolerance;
        bool nearRight = Math.Abs(point.X - _frame.Right) <= tolerance;
        bool nearTop = Math.Abs(point.Y - _frame.Top) <= tolerance;
        bool nearBottom = Math.Abs(point.Y - _frame.Bottom) <= tolerance;

        bool withinRows = point.Y >= _frame.Top - tolerance && point.Y <= _frame.Bottom + tolerance;
        bool withinColumns = point.X >= _frame.Left - tolerance && point.X <= _frame.Right + tolerance;

        CropGrip grip = CropGrip.None;

        if (nearLeft && withinRows)
        {
            grip |= CropGrip.Left;
        }
        else if (nearRight && withinRows)
        {
            grip |= CropGrip.Right;
        }

        if (nearTop && withinColumns)
        {
            grip |= CropGrip.Top;
        }
        else if (nearBottom && withinColumns)
        {
            grip |= CropGrip.Bottom;
        }

        // Not on an edge, but inside: the whole frame moves.
        return grip == CropGrip.None && _frame.Contains(point) ? CropGrip.All : grip;
    }

    /// <summary>Moves the gripped edges of a frame, keeping it the right way out.</summary>
    private static ImageRect Resize(ImageRect frame, CropGrip grip, int dx, int dy)
    {
        int left = frame.Left + (grip.HasFlag(CropGrip.Left) ? dx : 0);
        int right = frame.Right + (grip.HasFlag(CropGrip.Right) ? dx : 0);
        int top = frame.Top + (grip.HasFlag(CropGrip.Top) ? dy : 0);
        int bottom = frame.Bottom + (grip.HasFlag(CropGrip.Bottom) ? dy : 0);

        // Dragging an edge past its opposite flips the frame rather than inverting it, which
        // is what every other rectangle drag in the application does.
        return ImageRect.FromEdges(
            Math.Min(left, right),
            Math.Min(top, bottom),
            Math.Max(left, right),
            Math.Max(top, bottom));
    }
}
