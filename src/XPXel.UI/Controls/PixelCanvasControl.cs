using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.Primitives;
using XPXel.Core.Tools;
using XPXel.UI.Rendering;

namespace XPXel.UI.Controls;

/// <summary>
/// The drawing surface: it shows the document, converts pointer input into tool actions, and
/// handles zooming and panning.
/// </summary>
/// <remarks>
/// <para>
/// This control is the <em>only</em> place where screen coordinates exist. It converts them
/// to image coordinates through <see cref="CanvasViewTransform"/> before handing anything to
/// a tool, which is why every tool in <c>XPXel.Core</c> is free of UI concepts and can be
/// unit-tested.
/// </para>
/// <para>
/// <b>Crispness.</b> Bitmap interpolation is forced to <see cref="BitmapInterpolationMode.None"/>
/// so that zooming in produces hard-edged square pixels. Any smoothing here would defeat the
/// entire purpose of the application.
/// </para>
/// <para>
/// <b>Repainting.</b> The control listens to the document's single change event and passes
/// the dirty rectangle straight to <see cref="DocumentRenderTarget"/>, so drawing a pixel
/// recomposites a pixel rather than the canvas.
/// </para>
/// <para>
/// <b>Input conventions</b>, chosen to match what artists know from other editors: left and
/// right buttons draw with the primary and secondary colours, the middle button pans, and
/// the wheel zooms around the cursor.
/// </para>
/// </remarks>
public sealed class PixelCanvasControl : Control, IDisposable
{
    /// <summary>The document being edited and displayed.</summary>
    public static readonly StyledProperty<DocumentWorkspace?> WorkspaceProperty =
        AvaloniaProperty.Register<PixelCanvasControl, DocumentWorkspace?>(nameof(Workspace));

    /// <summary>The context handed to tools on every interaction.</summary>
    public static readonly StyledProperty<ToolContext?> ToolContextProperty =
        AvaloniaProperty.Register<PixelCanvasControl, ToolContext?>(nameof(ToolContext));

    /// <summary>The tool that receives pointer input.</summary>
    public static readonly StyledProperty<ITool?> ActiveToolProperty =
        AvaloniaProperty.Register<PixelCanvasControl, ITool?>(nameof(ActiveTool));

    /// <summary>Whether to overlay a one-pixel grid when zoomed in far enough.</summary>
    public static readonly StyledProperty<bool> ShowPixelGridProperty =
        AvaloniaProperty.Register<PixelCanvasControl, bool>(nameof(ShowPixelGrid), defaultValue: true);

    /// <summary>The colour painted behind and around the document.</summary>
    public static readonly StyledProperty<IBrush?> CanvasBackgroundProperty =
        AvaloniaProperty.Register<PixelCanvasControl, IBrush?>(nameof(CanvasBackground));

    /// <summary>The lighter square of the transparency checkerboard.</summary>
    public static readonly StyledProperty<Color> CheckerboardLightProperty =
        AvaloniaProperty.Register<PixelCanvasControl, Color>(
            nameof(CheckerboardLight), Color.FromRgb(0xC8, 0xC8, 0xC8));

    /// <summary>The darker square of the transparency checkerboard.</summary>
    public static readonly StyledProperty<Color> CheckerboardDarkProperty =
        AvaloniaProperty.Register<PixelCanvasControl, Color>(
            nameof(CheckerboardDark), Color.FromRgb(0x96, 0x96, 0x96));

    /// <summary>The colour of the pixel grid overlay.</summary>
    public static readonly StyledProperty<IBrush?> PixelGridBrushProperty =
        AvaloniaProperty.Register<PixelCanvasControl, IBrush?>(nameof(PixelGridBrush));

    /// <summary>The colour of the outline drawn around the document.</summary>
    public static readonly StyledProperty<IBrush?> CanvasBorderBrushProperty =
        AvaloniaProperty.Register<PixelCanvasControl, IBrush?>(nameof(CanvasBorderBrush));

    /// <summary>The current zoom, in screen pixels per image pixel.</summary>
    public static readonly DirectProperty<PixelCanvasControl, double> ZoomProperty =
        AvaloniaProperty.RegisterDirect<PixelCanvasControl, double>(
            nameof(Zoom), o => o.Zoom, (o, v) => o.Zoom = v);

    /// <summary>
    /// The image pixel currently under the pointer, or <see langword="null"/> when the
    /// pointer is outside the canvas. The status bar binds to this.
    /// </summary>
    public static readonly DirectProperty<PixelCanvasControl, ImagePoint?> HoveredPixelProperty =
        AvaloniaProperty.RegisterDirect<PixelCanvasControl, ImagePoint?>(
            nameof(HoveredPixel), o => o.HoveredPixel);

    /// <summary>Beyond this many grid lines the overlay stops being useful and is skipped.</summary>
    private const int MaximumGridLines = 4000;

    /// <summary>The checkerboard square size in screen pixels; fixed so it never appears to zoom.</summary>
    private const double CheckerboardSquareSize = 8.0;

    private DocumentRenderTarget? _renderTarget;
    private PixelDocument? _observedDocument;
    private CanvasViewTransform _transform = CanvasViewTransform.Identity;
    private ImageBrush? _checkerboardBrush;
    private bool _hasFittedView;

    private ToolPointerButton _activeButton = ToolPointerButton.None;
    private bool _isPanning;
    private Point _panOrigin;
    private Vector _panStartOffset;
    private ImagePoint? _hoveredPixel;
    private double _zoom = 1.0;

    /// <summary>
    /// The most recent arranged size. Used in preference to <see cref="Visual.Bounds"/>
    /// because Bounds is only assigned *after* ArrangeOverride returns, so reading it while
    /// arranging yields a zero size - which silently turned "zoom to fit" into "zoom to
    /// 100% and park the document off the top-left corner".
    /// </summary>
    private Size _viewport;

    /// <summary>Initialises the control.</summary>
    public PixelCanvasControl()
    {
        ClipToBounds = true;
        Focusable = true;

        // Nearest-neighbour sampling, applied once here so every draw inherits it.
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    /// <summary>The document being edited and displayed.</summary>
    public DocumentWorkspace? Workspace
    {
        get => GetValue(WorkspaceProperty);
        set => SetValue(WorkspaceProperty, value);
    }

    /// <summary>The context handed to tools on every interaction.</summary>
    public ToolContext? ToolContext
    {
        get => GetValue(ToolContextProperty);
        set => SetValue(ToolContextProperty, value);
    }

    /// <summary>The tool that receives pointer input.</summary>
    public ITool? ActiveTool
    {
        get => GetValue(ActiveToolProperty);
        set => SetValue(ActiveToolProperty, value);
    }

    /// <summary>Whether to overlay a one-pixel grid when zoomed in far enough.</summary>
    public bool ShowPixelGrid
    {
        get => GetValue(ShowPixelGridProperty);
        set => SetValue(ShowPixelGridProperty, value);
    }

    /// <summary>The colour painted behind and around the document.</summary>
    public IBrush? CanvasBackground
    {
        get => GetValue(CanvasBackgroundProperty);
        set => SetValue(CanvasBackgroundProperty, value);
    }

    /// <summary>The lighter square of the transparency checkerboard.</summary>
    public Color CheckerboardLight
    {
        get => GetValue(CheckerboardLightProperty);
        set => SetValue(CheckerboardLightProperty, value);
    }

    /// <summary>The darker square of the transparency checkerboard.</summary>
    public Color CheckerboardDark
    {
        get => GetValue(CheckerboardDarkProperty);
        set => SetValue(CheckerboardDarkProperty, value);
    }

    /// <summary>The colour of the pixel grid overlay.</summary>
    public IBrush? PixelGridBrush
    {
        get => GetValue(PixelGridBrushProperty);
        set => SetValue(PixelGridBrushProperty, value);
    }

    /// <summary>The colour of the outline drawn around the document.</summary>
    public IBrush? CanvasBorderBrush
    {
        get => GetValue(CanvasBorderBrushProperty);
        set => SetValue(CanvasBorderBrushProperty, value);
    }

    /// <summary>The current zoom, in screen pixels per image pixel.</summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            double clamped = Math.Clamp(value, CanvasViewTransform.MinimumScale, CanvasViewTransform.MaximumScale);
            if (Math.Abs(clamped - _zoom) < double.Epsilon)
            {
                return;
            }

            ApplyTransform(_transform.ZoomTo(clamped, ViewportCenter));
        }
    }

    /// <summary>The image pixel currently under the pointer, or <see langword="null"/>.</summary>
    public ImagePoint? HoveredPixel
    {
        get => _hoveredPixel;
        private set => SetAndRaise(HoveredPixelProperty, ref _hoveredPixel, value);
    }

    /// <summary>Zooms in one step, centred on the viewport.</summary>
    public void ZoomIn() => ZoomStep(zoomIn: true, ViewportCenter);

    /// <summary>Zooms out one step, centred on the viewport.</summary>
    public void ZoomOut() => ZoomStep(zoomIn: false, ViewportCenter);

    /// <summary>Resets the view to 1:1 and centres the document.</summary>
    public void ZoomToActualSize() => CenterDocument(1.0);

    /// <summary>Zooms so the whole document is visible and centres it.</summary>
    public void ZoomToFit()
    {
        if (Workspace is null)
        {
            return;
        }

        CenterDocument(CanvasViewTransform.GetFitScale(_viewport, Workspace.Document.Size));
    }

    private Point ViewportCenter => new(_viewport.Width / 2, _viewport.Height / 2);

    /// <summary>
    /// Releases the composited bitmap and stops observing the document.
    /// </summary>
    /// <remarks>
    /// A control is not normally disposable, but this one owns a GPU-backed bitmap sized to
    /// the document. Waiting for finalisation would keep that memory alive for an
    /// indeterminate time after a document is closed, which on a large canvas is tens of
    /// megabytes. The shell disposes the canvas when it tears down a document view.
    /// </remarks>
    public void Dispose()
    {
        if (_observedDocument is not null)
        {
            _observedDocument.Changed -= OnDocumentChanged;
            _observedDocument = null;
        }

        _renderTarget?.Dispose();
        _renderTarget = null;
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Rect viewport = new(Bounds.Size);

        if (CanvasBackground is { } background)
        {
            context.FillRectangle(background, viewport);
        }

        if (Workspace is not { } workspace || _renderTarget is null)
        {
            return;
        }

        Rect documentRect = _transform.GetDocumentRect(workspace.Document.Size);

        DrawCheckerboard(context, documentRect);

        WriteableBitmap bitmap = _renderTarget.GetBitmap(workspace.Document, workspace.ActiveFrame);
        context.DrawImage(bitmap, new Rect(bitmap.Size), documentRect);

        DrawPixelGrid(context, workspace.Document.Size, documentRect, viewport);
        DrawDocumentBorder(context, documentRect);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => availableSize;

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        Size arranged = base.ArrangeOverride(finalSize);
        _viewport = finalSize;

        // Fit the document the first time the control is given a real size, so a newly
        // opened document appears centred and fully visible without the artist doing
        // anything.
        TryFitOnce();

        return arranged;
    }

    /// <summary>
    /// Fits the document once, as soon as both a document and a real viewport size exist.
    /// Which of those two arrives first depends on binding order, so both paths call this.
    /// </summary>
    private void TryFitOnce()
    {
        if (_hasFittedView || Workspace is null || _viewport is not { Width: > 0, Height: > 0 })
        {
            return;
        }

        _hasFittedView = true;
        ZoomToFit();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WorkspaceProperty)
        {
            OnWorkspaceChanged(change.GetNewValue<DocumentWorkspace?>());
        }
        else if (change.Property == ToolContextProperty)
        {
            // Workspace and ToolContext arrive as two separate bindings in an unspecified
            // order, so whichever lands second has to wire up the colour picker's sampler.
            if (change.GetNewValue<ToolContext?>() is { } context)
            {
                context.CompositeSampler = _renderTarget is null ? null : _renderTarget.SampleComposite;
            }
        }
        else if (change.Property == CheckerboardLightProperty || change.Property == CheckerboardDarkProperty)
        {
            _checkerboardBrush = null;
            InvalidateVisual();
        }
        else if (change.Property == ShowPixelGridProperty)
        {
            InvalidateVisual();
        }
    }

    // ---------------------------------------------------------------------------------
    // Pointer input
    // ---------------------------------------------------------------------------------

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        Point position = e.GetPosition(this);

        if (properties.IsMiddleButtonPressed)
        {
            _isPanning = true;
            _panOrigin = position;
            _panStartOffset = _transform.Offset;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (ToolContext is not { } context || ActiveTool is not { } tool)
        {
            return;
        }

        _activeButton = MapButton(properties);
        if (_activeButton == ToolPointerButton.None)
        {
            return;
        }

        e.Pointer.Capture(this);
        tool.OnPointerPressed(context, CreateToolArgs(position, _activeButton, e.KeyModifiers));
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        Point position = e.GetPosition(this);
        UpdateHoveredPixel(position);

        if (_isPanning)
        {
            ApplyTransform(_transform with { Offset = _panStartOffset + (position - _panOrigin) });
            e.Handled = true;
            return;
        }

        if (ToolContext is not { } context || ActiveTool is not { } tool)
        {
            return;
        }

        tool.OnPointerMoved(context, CreateToolArgs(position, _activeButton, e.KeyModifiers));
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isPanning)
        {
            _isPanning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_activeButton != ToolPointerButton.None && ToolContext is { } context && ActiveTool is { } tool)
        {
            tool.OnPointerReleased(context, CreateToolArgs(e.GetPosition(this), _activeButton, e.KeyModifiers));
            e.Handled = true;
        }

        _activeButton = ToolPointerButton.None;
        e.Pointer.Capture(null);
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        // Losing capture mid-stroke — an alt-tab, a system dialog — must not leave a
        // half-recorded edit behind, so the stroke is rolled back rather than committed.
        if (ToolContext is { } context)
        {
            ActiveTool?.Cancel(context);
        }

        _activeButton = ToolPointerButton.None;
        _isPanning = false;
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        HoveredPixel = null;
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (e.Delta.Y == 0)
        {
            return;
        }

        ZoomStep(e.Delta.Y > 0, e.GetPosition(this));
        e.Handled = true;
    }

    // ---------------------------------------------------------------------------------
    // Document observation
    // ---------------------------------------------------------------------------------

    private void OnWorkspaceChanged(DocumentWorkspace? workspace)
    {
        if (_observedDocument is not null)
        {
            _observedDocument.Changed -= OnDocumentChanged;
        }

        _renderTarget?.Dispose();
        _renderTarget = null;
        _observedDocument = workspace?.Document;
        _hasFittedView = false;

        if (_observedDocument is not null)
        {
            _observedDocument.Changed += OnDocumentChanged;
            _renderTarget = new DocumentRenderTarget(_observedDocument.Size);
        }

        // The colour picker samples what is on screen, and the render target is the only
        // thing that holds it.
        if (ToolContext is { } context)
        {
            context.CompositeSampler = _renderTarget is null ? null : _renderTarget.SampleComposite;
        }

        TryFitOnce();
        InvalidateVisual();
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (_renderTarget is null)
        {
            return;
        }

        if (e.Kind == DocumentChangeKind.Pixels)
        {
            _renderTarget.Invalidate(e.Region);
        }
        else
        {
            // Anything structural can change what every pixel looks like.
            _renderTarget.InvalidateAll();
        }

        InvalidateVisual();
    }

    // ---------------------------------------------------------------------------------
    // Drawing helpers
    // ---------------------------------------------------------------------------------

    private void DrawCheckerboard(DrawingContext context, Rect documentRect)
    {
        _checkerboardBrush ??= CreateCheckerboardBrush();
        context.FillRectangle(_checkerboardBrush, documentRect);
    }

    /// <summary>
    /// Builds the transparency checkerboard as a tiled two-by-two bitmap.
    /// </summary>
    /// <remarks>
    /// Drawing the squares individually would mean thousands of fill operations per frame on
    /// a large canvas. A tiled brush is one operation, and pinning its tile size to screen
    /// pixels keeps the checks a constant size no matter how far the artist zooms — which is
    /// what makes them read as "nothing here" rather than as part of the artwork.
    /// </remarks>
    private ImageBrush CreateCheckerboardBrush()
    {
        WriteableBitmap tile = new(new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

        using (ILockedFramebuffer framebuffer = tile.Lock())
        {
            Color[,] colors =
            {
                { CheckerboardLight, CheckerboardDark },
                { CheckerboardDark, CheckerboardLight },
            };

            byte[] row = new byte[2 * 4];
            for (int y = 0; y < 2; y++)
            {
                for (int x = 0; x < 2; x++)
                {
                    Color color = colors[y, x];
                    row[(x * 4) + 0] = color.B;
                    row[(x * 4) + 1] = color.G;
                    row[(x * 4) + 2] = color.R;
                    row[(x * 4) + 3] = byte.MaxValue;
                }

                System.Runtime.InteropServices.Marshal.Copy(
                    row, 0, framebuffer.Address + (y * framebuffer.RowBytes), row.Length);
            }
        }

        return new ImageBrush(tile)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.Fill,
            DestinationRect = new RelativeRect(
                0,
                0,
                CheckerboardSquareSize * 2,
                CheckerboardSquareSize * 2,
                RelativeUnit.Absolute),
        };
    }

    private void DrawPixelGrid(DrawingContext context, ImageSize size, Rect documentRect, Rect viewport)
    {
        if (!ShowPixelGrid
            || _transform.Scale < CanvasViewTransform.GridVisibilityScale
            || PixelGridBrush is not { } brush)
        {
            return;
        }

        Rect visible = documentRect.Intersect(viewport);
        if (visible.Width <= 0 || visible.Height <= 0)
        {
            return;
        }

        // Only the lines inside the viewport are drawn; a large canvas at high zoom would
        // otherwise emit tens of thousands of invisible lines every frame.
        int firstColumn = Math.Max(0, (int)Math.Floor((visible.Left - documentRect.X) / _transform.Scale));
        int lastColumn = Math.Min(size.Width, (int)Math.Ceiling((visible.Right - documentRect.X) / _transform.Scale));
        int firstRow = Math.Max(0, (int)Math.Floor((visible.Top - documentRect.Y) / _transform.Scale));
        int lastRow = Math.Min(size.Height, (int)Math.Ceiling((visible.Bottom - documentRect.Y) / _transform.Scale));

        if ((lastColumn - firstColumn) + (lastRow - firstRow) > MaximumGridLines)
        {
            return;
        }

        Pen pen = new(brush, 1.0);

        for (int x = firstColumn; x <= lastColumn; x++)
        {
            double viewX = Math.Round(documentRect.X + (x * _transform.Scale)) + 0.5;
            context.DrawLine(pen, new Point(viewX, visible.Top), new Point(viewX, visible.Bottom));
        }

        for (int y = firstRow; y <= lastRow; y++)
        {
            double viewY = Math.Round(documentRect.Y + (y * _transform.Scale)) + 0.5;
            context.DrawLine(pen, new Point(visible.Left, viewY), new Point(visible.Right, viewY));
        }
    }

    private void DrawDocumentBorder(DrawingContext context, Rect documentRect)
    {
        if (CanvasBorderBrush is not { } brush)
        {
            return;
        }

        // Offsetting by half a pixel puts the one-pixel stroke on the device grid instead of
        // straddling two rows, which would render it as a soft two-pixel line.
        context.DrawRectangle(new Pen(brush, 1.0), documentRect.Inflate(0.5));
    }

    // ---------------------------------------------------------------------------------
    // View manipulation
    // ---------------------------------------------------------------------------------

    private void ZoomStep(bool zoomIn, Point focus) =>
        ApplyTransform(_transform.ZoomTo(_transform.GetSteppedScale(zoomIn), focus));

    private void CenterDocument(double scale)
    {
        if (Workspace is not { } workspace)
        {
            return;
        }

        ApplyTransform(CanvasViewTransform.Centered(_viewport, workspace.Document.Size, scale));
    }

    private void ApplyTransform(CanvasViewTransform transform)
    {
        _transform = transform;
        SetAndRaise(ZoomProperty, ref _zoom, transform.Scale);
        InvalidateVisual();
    }

    private void UpdateHoveredPixel(Point position)
    {
        if (Workspace is not { } workspace)
        {
            HoveredPixel = null;
            return;
        }

        ImagePoint point = _transform.ToImagePoint(position);
        HoveredPixel = workspace.Document.Bounds.Contains(point) ? point : null;
    }

    private ToolPointerEventArgs CreateToolArgs(Point position, ToolPointerButton button, KeyModifiers modifiers) =>
        new(_transform.ToImagePoint(position), button, MapModifiers(modifiers));

    private static ToolPointerButton MapButton(PointerPointProperties properties)
    {
        if (properties.IsLeftButtonPressed)
        {
            return ToolPointerButton.Left;
        }

        if (properties.IsRightButtonPressed)
        {
            return ToolPointerButton.Right;
        }

        return properties.IsMiddleButtonPressed ? ToolPointerButton.Middle : ToolPointerButton.None;
    }

    private static ToolModifiers MapModifiers(KeyModifiers modifiers)
    {
        ToolModifiers result = ToolModifiers.None;

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            result |= ToolModifiers.Shift;
        }

        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            result |= ToolModifiers.Control;
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            result |= ToolModifiers.Alt;
        }

        return result;
    }
}
