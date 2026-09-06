using XPXel.Core.History;
using XPXel.Core.Primitives;

namespace XPXel.Core.Tools;

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
/// <b>What it does not do is add handles to the canvas.</b> The edges of the frame are the
/// handles: pressing near one drags it, pressing well inside moves the whole frame, and
/// pressing outside starts a fresh frame from that corner. That is three gestures with nothing
/// to draw and nothing to hit at low zoom, where an eight-pixel handle would be invisible.
/// </para>
/// </remarks>
public sealed class CropTool : ITool, IInteractiveTool
{
    /// <summary>The stable identifier of this tool.</summary>
    public const string ToolId = "xpxel.tool.crop";

    /// <summary>Which part of the frame a drag is moving.</summary>
    [Flags]
    private enum Grip
    {
        None = 0,
        Left = 1,
        Right = 2,
        Top = 4,
        Bottom = 8,
        All = Left | Right | Top | Bottom,
    }

    private ImageRect _frame;
    private ImageRect _dragStart;
    private ImagePoint _anchor;
    private Grip _grip;
    private bool _isDragging;

    /// <inheritdoc />
    public string Id => ToolId;

    /// <inheritdoc />
    public string DisplayName => "Crop";

    /// <inheritdoc />
    public string Description =>
        "Drag the frame's edges to trim the picture or to make it bigger, then press Enter. Escape puts the frame back.";

    /// <inheritdoc />
    public void OnActivated(ToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _frame = context.Document.Bounds;
        _isDragging = false;
        context.Overlay.Marquee = _frame;
    }

    /// <inheritdoc />
    public void OnDeactivated(ToolContext context)
    {
        _isDragging = false;

        if (context is not null)
        {
            context.Overlay.Marquee = null;
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

        _grip = GetGrip(e.Position);
        _anchor = e.Position;
        _dragStart = _frame;
        _isDragging = true;

        if (_grip == Grip.None)
        {
            // Nowhere near the frame and not inside it: the artist is drawing a new one.
            _frame = new ImageRect(e.Position.X, e.Position.Y, 0, 0);
            _dragStart = _frame;
            _grip = Grip.Right | Grip.Bottom;
        }

        context.Overlay.Marquee = _frame;
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

        _frame = _grip == Grip.All
            ? _dragStart.Translate(dx, dy)
            : Resize(_dragStart, _grip, dx, dy);

        context.Overlay.Marquee = _frame;
    }

    /// <inheritdoc />
    public void OnPointerReleased(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);

        _isDragging = false;
        context.Overlay.Marquee = _frame;
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

    /// <summary>Works out what a press at a point is asking to drag.</summary>
    /// <remarks>
    /// The tolerance grows with the frame but is capped, so the edges stay catchable on a
    /// 16-pixel sprite without swallowing the whole middle of a 512-pixel background.
    /// </remarks>
    private Grip GetGrip(ImagePoint point)
    {
        int tolerance = Math.Clamp(Math.Min(_frame.Width, _frame.Height) / 8, 1, 6);

        bool nearLeft = Math.Abs(point.X - _frame.Left) <= tolerance;
        bool nearRight = Math.Abs(point.X - _frame.Right) <= tolerance;
        bool nearTop = Math.Abs(point.Y - _frame.Top) <= tolerance;
        bool nearBottom = Math.Abs(point.Y - _frame.Bottom) <= tolerance;

        bool withinRows = point.Y >= _frame.Top - tolerance && point.Y <= _frame.Bottom + tolerance;
        bool withinColumns = point.X >= _frame.Left - tolerance && point.X <= _frame.Right + tolerance;

        Grip grip = Grip.None;

        if (nearLeft && withinRows)
        {
            grip |= Grip.Left;
        }
        else if (nearRight && withinRows)
        {
            grip |= Grip.Right;
        }

        if (nearTop && withinColumns)
        {
            grip |= Grip.Top;
        }
        else if (nearBottom && withinColumns)
        {
            grip |= Grip.Bottom;
        }

        // Not on an edge, but inside: the whole frame moves.
        return grip == Grip.None && _frame.Contains(point) ? Grip.All : grip;
    }

    /// <summary>Moves the gripped edges of a frame, keeping it the right way out.</summary>
    private static ImageRect Resize(ImageRect frame, Grip grip, int dx, int dy)
    {
        int left = frame.Left + (grip.HasFlag(Grip.Left) ? dx : 0);
        int right = frame.Right + (grip.HasFlag(Grip.Right) ? dx : 0);
        int top = frame.Top + (grip.HasFlag(Grip.Top) ? dy : 0);
        int bottom = frame.Bottom + (grip.HasFlag(Grip.Bottom) ? dy : 0);

        // Dragging an edge past its opposite flips the frame rather than inverting it, which
        // is what every other rectangle drag in the application does.
        return ImageRect.FromEdges(
            Math.Min(left, right),
            Math.Min(top, bottom),
            Math.Max(left, right),
            Math.Max(top, bottom));
    }
}
