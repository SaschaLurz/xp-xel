using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using XPXel.Core.Animation;
using XPXel.Core.Documents;
using XPXel.Core.Drawing;
using XPXel.Core.Editing;
using XPXel.Core.Primitives;
using XPXel.Core.Selection;
using XPXel.Core.Tools;
using XPXel.UI.Media;
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
/// <b>Input conventions</b>, chosen to match what artists know from other editors: the left
/// button draws, the middle button pans, holding space turns any button into a grab handle,
/// and the wheel zooms around the cursor. The right button is deliberately <i>not</i> a
/// second drawing button: it opens the active tool's settings, and the secondary colour is
/// reached by swapping with X.
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

    /// <summary>Whether a dot of the drawing colour follows the pointer.</summary>
    public static readonly StyledProperty<bool> ShowColorSwatchProperty =
        AvaloniaProperty.Register<PixelCanvasControl, bool>(nameof(ShowColorSwatch), defaultValue: true);

    /// <summary>How far one zoom step travels.</summary>
    public static readonly StyledProperty<double> ZoomSpeedProperty =
        AvaloniaProperty.Register<PixelCanvasControl, double>(
            nameof(ZoomSpeed),
            defaultValue: CanvasViewTransform.DefaultZoomSpeed);

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

    /// <summary>
    /// How the neighbouring frames are ghosted under the current one, or
    /// <see langword="null"/> for no onion skinning.
    /// </summary>
    public static readonly StyledProperty<OnionSkinSettings?> OnionSkinProperty =
        AvaloniaProperty.Register<PixelCanvasControl, OnionSkinSettings?>(nameof(OnionSkin));

    /// <summary>The bright half of every overlay outline the canvas draws.</summary>
    /// <remarks>
    /// Hover highlights, the brush footprint and the selection edge are all drawn twice — a
    /// dark line with a bright one beside it. A single-colour outline is invisible against
    /// artwork of that colour, and a pixel editor's canvas is by definition covered in every
    /// colour the artist has used. The pair carries its own contrast everywhere.
    /// </remarks>
    public static readonly StyledProperty<IBrush?> HighlightBrushProperty =
        AvaloniaProperty.Register<PixelCanvasControl, IBrush?>(nameof(HighlightBrush));

    /// <summary>The dark half of those outlines.</summary>
    public static readonly StyledProperty<IBrush?> HighlightShadowBrushProperty =
        AvaloniaProperty.Register<PixelCanvasControl, IBrush?>(nameof(HighlightShadowBrush));

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

    /// <summary>
    /// The canvas's zoom and pan, published so that the rulers beside it can line their
    /// numbers up with the artwork without duplicating any of the arithmetic.
    /// </summary>
    public static readonly DirectProperty<PixelCanvasControl, CanvasViewTransform> ViewTransformProperty =
        AvaloniaProperty.RegisterDirect<PixelCanvasControl, CanvasViewTransform>(
            nameof(ViewTransform), o => o.ViewTransform);

    /// <summary>
    /// The size of the document on display, or an empty size when there is none. Published
    /// for the same reason as the transform: the rulers need to know where the artwork ends,
    /// and the document does not raise a property change of its own for a resize.
    /// </summary>
    public static readonly DirectProperty<PixelCanvasControl, ImageSize> DocumentSizeProperty =
        AvaloniaProperty.RegisterDirect<PixelCanvasControl, ImageSize>(
            nameof(DocumentSize), o => o.DocumentSize);

    /// <summary>Beyond this many grid lines the overlay stops being useful and is skipped.</summary>
    private const int MaximumGridLines = 4000;

    /// <summary>The checkerboard square size in screen pixels; fixed so it never appears to zoom.</summary>
    private const double CheckerboardSquareSize = 8.0;

    /// <summary>The dash pattern used for the selection and marquee outlines.</summary>
    /// <remarks>
    /// Expressed in multiples of the pen width, so the dashes stay the same size on screen
    /// however far the artist has zoomed in. A selection edge that scaled with the zoom would
    /// become one long line at 64×, which is exactly where it needs to be readable.
    /// </remarks>
    private static readonly ImmutableDashStyle MarchingAnts = new([4, 4], 0);

    /// <summary>
    /// How dark the area outside a crop frame is drawn. A fixed translucent black rather than a
    /// theme colour: it has to darken whatever is behind it, and in a light theme a theme-derived
    /// shade would be the one thing that fails to.
    /// </summary>
    private static readonly IBrush CropShade = new ImmutableSolidColorBrush(Color.FromArgb(110, 0, 0, 0));

    /// <summary>
    /// The size of a crop handle in screen pixels, matched to the distance at which the tool
    /// treats a press as grabbing one.
    /// </summary>
    private const double CropHandleSize = CropTool.GripScreenDistance;

    private DocumentRenderTarget? _renderTarget;

    /// <summary>
    /// The artwork that currently lies outside the canvas, built only while the crop tool is
    /// showing a frame and disposed as soon as it is put down.
    /// </summary>
    private OverscanRenderTarget? _overscan;
    private PixelDocument? _observedDocument;
    private DocumentWorkspace? _observedWorkspace;
    private ToolSettings? _observedSettings;
    private OnionSkinSettings? _observedOnionSkin;
    private ToolOverlay? _observedOverlay;
    private TextEditSession? _observedTextEdit;

    /// <summary>
    /// The traced edge of the current selection, or <see langword="null"/> when it needs
    /// tracing again. Cleared whenever the workspace reports the selection changed.
    /// </summary>
    private IReadOnlyList<SelectionOutline.Segment>? _selectionOutline;

    /// <summary>
    /// How the active tool is presented: which cursor it uses, and whether the canvas should
    /// outline the brush footprint under the pointer.
    /// </summary>
    private ToolPresentation _presentation = ToolPresentations.For(null);

    /// <summary>
    /// The modifier keys held as of the last thing the canvas heard about, so the cursor can say
    /// what the next click will do before the artist makes it.
    /// </summary>
    private KeyModifiers _modifiers = KeyModifiers.None;
    private CanvasViewTransform _transform = CanvasViewTransform.Identity;
    private ImageSize _documentSize;
    private ImageBrush? _checkerboardBrush;
    /// <summary>
    /// Whether the artist has zoomed or panned this canvas themselves.
    /// </summary>
    /// <remarks>
    /// Until they have, the view keeps re-fitting whenever the viewport changes size, so
    /// the artwork stays centred while the window is resized or a panel is dragged wider.
    /// The first deliberate zoom or pan ends that: continuing to re-fit afterwards would
    /// throw away the position the artist chose, which is far worse than an off-centre
    /// canvas. Docking made this necessary — a canvas is now arranged several times at
    /// growing sizes while the layout settles, and fitting only to the first of those left
    /// the artwork parked in a corner.
    /// </remarks>
    private bool _hasUserAdjustedView;

    private ToolPointerButton _activeButton = ToolPointerButton.None;
    private bool _isPanning;

    /// <summary>
    /// Whether holding space has put the canvas into pan mode, so that the left button
    /// grabs the image instead of drawing on it.
    /// </summary>
    /// <remarks>
    /// Space is the convention in every pixel and image editor — Photoshop, Aseprite,
    /// Krita, Figma — because it lets an artist reposition the canvas without giving up the
    /// tool they are holding. Middle-button panning stays available for mice that have one.
    /// </remarks>
    private bool _isSpacePanArmed;
    private Point _panOrigin;
    private Vector _panStartOffset;
    private ImagePoint? _hoveredPixel;

    /// <summary>
    /// Where the pointer last was, in view space.
    /// </summary>
    /// <remarks>
    /// The colour swatch is offset from the pointer rather than from the hovered pixel,
    /// because at 32× a pixel is thirty-two points across and an offset measured in pixels
    /// would fling the swatch halfway across the canvas.
    /// </remarks>
    private Point? _pointerPosition;

    /// <summary>
    /// The modifiers seen on the last pointer event, for the handover when the tool changes
    /// under a held button — a key press carries no pointer state of its own.
    /// </summary>
    private KeyModifiers _lastKeyModifiers;

    private ImmutableSolidColorBrush? _swatchBrush;
    private PixelColor _swatchColor;

    private ColorSlots? _observedColors;

    /// <summary>
    /// Where the pointer went down on a free transform, and how far the artwork had already
    /// been moved at that moment. Together they turn a drag into an absolute offset, so a drag
    /// that starts on artwork already moved once carries on from there rather than snapping.
    /// </summary>
    private ImagePoint _transformGrabPoint;
    private ImagePoint _transformGrabOffset;
    private bool _isDraggingTransform;

    /// <summary>
    /// The cursor currently carrying the drawing colour, and the colour it carries. Owned by
    /// this control — one canvas wears one cursor — and replaced when the colour changes.
    /// </summary>
    private Cursor? _dotCursor;
    private PixelColor _dotCursorColor;

    /// <summary>
    /// Set once the platform has refused to make a bitmap cursor, after which the canvas
    /// paints the dot itself rather than asking again on every pointer move.
    /// </summary>
    private bool _dotCursorUnavailable;
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

    /// <summary>Whether a dot of the drawing colour follows the pointer.</summary>
    public bool ShowColorSwatch
    {
        get => GetValue(ShowColorSwatchProperty);
        set => SetValue(ShowColorSwatchProperty, value);
    }

    /// <summary>
    /// How far one zoom step travels, where 1 is the default. Shared by the wheel, the menu
    /// and the keyboard, so that "zoom in" means one thing however it is asked for.
    /// </summary>
    public double ZoomSpeed
    {
        get => GetValue(ZoomSpeedProperty);
        set => SetValue(ZoomSpeedProperty, value);
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

    /// <summary>
    /// How the neighbouring frames are ghosted under the current one, or
    /// <see langword="null"/> for no onion skinning.
    /// </summary>
    public OnionSkinSettings? OnionSkin
    {
        get => GetValue(OnionSkinProperty);
        set => SetValue(OnionSkinProperty, value);
    }

    /// <summary>The bright half of every overlay outline the canvas draws.</summary>
    public IBrush? HighlightBrush
    {
        get => GetValue(HighlightBrushProperty);
        set => SetValue(HighlightBrushProperty, value);
    }

    /// <summary>The dark half of those outlines.</summary>
    public IBrush? HighlightShadowBrush
    {
        get => GetValue(HighlightShadowBrushProperty);
        set => SetValue(HighlightShadowBrushProperty, value);
    }

    /// <summary>
    /// Gets a value indicating whether text is currently being typed on this canvas.
    /// </summary>
    /// <remarks>
    /// The shell asks before claiming a bare keystroke for a shortcut. While text is being
    /// edited, a space is a space and an "x" is an "x" — not "pan" and "swap colours".
    /// </remarks>
    public bool IsEditingText => _observedWorkspace?.ActiveTextEdit is not null;

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

            _hasUserAdjustedView = true;
            ApplyTransform(_transform.ZoomTo(clamped, ViewportCenter));
        }
    }

    /// <summary>The image pixel currently under the pointer, or <see langword="null"/>.</summary>
    public ImagePoint? HoveredPixel
    {
        get => _hoveredPixel;
        private set => SetAndRaise(HoveredPixelProperty, ref _hoveredPixel, value);
    }

    /// <summary>The canvas's current zoom and pan.</summary>
    public CanvasViewTransform ViewTransform
    {
        get => _transform;
        private set => SetAndRaise(ViewTransformProperty, ref _transform, value);
    }

    /// <summary>The size of the document on display, or an empty size when there is none.</summary>
    public ImageSize DocumentSize
    {
        get => _documentSize;
        private set => SetAndRaise(DocumentSizeProperty, ref _documentSize, value);
    }

    /// <summary>Zooms in one step, centred on the viewport.</summary>
    public void ZoomIn() => ZoomStep(zoomIn: true, ViewportCenter);

    /// <summary>Zooms out one step, centred on the viewport.</summary>
    public void ZoomOut() => ZoomStep(zoomIn: false, ViewportCenter);

    /// <summary>Resets the view to 1:1 and centres the document.</summary>
    public void ZoomToActualSize()
    {
        _hasUserAdjustedView = true;
        CenterDocument(1.0);
    }

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

        // A tool holding a frame for this document must let go of it before the document does.
        if (ToolContext is { } context)
        {
            (ActiveTool as IInteractiveTool)?.OnDeactivated(context);
        }

        ObserveWorkspace(null);
        ObserveToolContext(null);
        ObserveOnionSkin(null);

        // The control is going away, so the cursor it was wearing goes with it rather than
        // waiting on a finaliser to hand the platform resource back.
        Cursor = ToolCursors.Crosshair;
        TakeDotCursor()?.Dispose();

        _renderTarget?.Dispose();
        _renderTarget = null;

        _overscan?.Dispose();
        _overscan = null;
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

        // While a crop frame is up, everything is drawn across the frame and the canvas
        // together, so the artist can see what dragging outwards would bring back.
        ImageRect? cropFrame = _observedOverlay?.CropFrame;
        ImageRect extent = cropFrame is { } frame
            ? frame.Union(workspace.Document.Bounds)
            : workspace.Document.Bounds;

        Rect extentRect = _transform.GetPixelRect(extent);

        DrawCheckerboard(context, extentRect);
        DrawOverscan(context, workspace, extent);

        WriteableBitmap bitmap = _renderTarget.GetBitmap(
            workspace.Document,
            workspace.ActiveFrame,
            OnionSkin,
            workspace.ActiveTag);
        context.DrawImage(bitmap, new Rect(bitmap.Size), documentRect);

        DrawPixelGrid(context, extent, extentRect, viewport);
        DrawDocumentBorder(context, documentRect);

        // Overlays go on top of the artwork and of each other in the order an artist reads
        // them: what is already selected, what is being selected right now, and last of all
        // where the pointer is about to act.
        DrawSelection(context, workspace.Selection);
        DrawMarquee(context);
        DrawCropFrame(context, cropFrame, viewport);
        DrawHoverHighlight(context);
        DrawTextCaret(context);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => availableSize;

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        Size arranged = base.ArrangeOverride(finalSize);
        _viewport = finalSize;

        // Keep the document fitted and centred until the artist takes over the view.
        TryFitToViewport();

        return arranged;
    }

    /// <summary>
    /// Fits the document to the viewport, as long as a document and a real size both exist
    /// and the artist has not yet taken control of the view.
    /// </summary>
    /// <remarks>
    /// Which of the document and the size arrives first depends on binding order, so every
    /// path that could supply either calls this.
    /// </remarks>
    private void TryFitToViewport()
    {
        if (_hasUserAdjustedView || Workspace is null || _viewport is not { Width: > 0, Height: > 0 })
        {
            return;
        }

        // ZoomToFit is what actually moves the view, and it must not be taken as the artist
        // adjusting it, so the flag is left alone here.
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
            ToolContext? context = change.GetNewValue<ToolContext?>();
            ObserveToolContext(context);

            // Workspace and ToolContext arrive as two separate bindings in an unspecified
            // order, so whichever lands second has to wire up the colour picker's sampler.
            if (context is not null)
            {
                context.CompositeSampler = _renderTarget is null ? null : _renderTarget.SampleComposite;
                context.ViewScale = _transform.Scale;
            }
        }
        else if (change.Property == ActiveToolProperty)
        {
            OnActiveToolChanged(change.GetOldValue<ITool?>(), change.GetNewValue<ITool?>());
        }
        else if (change.Property == HighlightBrushProperty || change.Property == HighlightShadowBrushProperty)
        {
            InvalidateVisual();
        }
        else if (change.Property == ShowColorSwatchProperty)
        {
            UpdateCursor();
            InvalidateVisual();
        }
        else if (change.Property == OnionSkinProperty)
        {
            ObserveOnionSkin(change.GetNewValue<OnionSkinSettings?>());
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
    // Keyboard input
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Turns pan mode on or off.
    /// </summary>
    /// <param name="armed">Whether the left button should grab the canvas instead of drawing.</param>
    /// <remarks>
    /// Public because the space key has to work wherever the artist last clicked. If this
    /// were driven only by the control's own key handling, pressing space after touching the
    /// layers panel would do nothing, and an artist who has just picked a layer is exactly
    /// the artist who wants to reposition the canvas. The hosting view listens at the window
    /// level and calls this; see <c>EditorDocumentView</c>.
    /// </remarks>
    public void SetSpacePanArmed(bool armed)
    {
        if (armed)
        {
            // Arming mid-stroke would leave a half-drawn line behind, so the request is
            // ignored until the button is released. Key auto-repeat calls this every few
            // milliseconds, which is why it has to be idempotent. Typing takes precedence
            // over the shortcut outright: while text is being edited a space is a space.
            if (_isSpacePanArmed || _activeButton != ToolPointerButton.None || IsEditingText)
            {
                return;
            }

            _isSpacePanArmed = true;
            UpdateCursor();
            return;
        }

        DisarmSpacePan();
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);

        TrackModifiers(e.KeyModifiers);

        // Typing wins over every bare shortcut. Marking the key handled is what stops it
        // reaching the shell's key bindings, where "x" swaps the drawing colours — which is
        // not what an artist typing the word "next" is asking for.
        if (_observedWorkspace?.ActiveTextEdit is { } editing && HandleTextEditKey(editing, e))
        {
            e.Handled = true;
            return;
        }

        if (_observedWorkspace?.ActiveTransform is not null && HandleTransformKey(e))
        {
            e.Handled = true;
            return;
        }

        // Enter and Escape belong to whatever is waiting for an answer. The transform above
        // gets first refusal; a tool holding a frame gets them next.
        if (HandleInteractiveToolKey(e))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space)
        {
            SetSpacePanArmed(true);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Applies a keystroke to the pixels being moved.
    /// </summary>
    /// <returns><see langword="true"/> if the key belonged to the move.</returns>
    /// <remarks>
    /// The arrow keys are here because a move is often a nudge of one or two pixels, and at that
    /// size a mouse is the wrong instrument entirely. A key held with Control never belongs to
    /// the move, so Ctrl+Z still undoes and Ctrl+T still puts the artwork down.
    /// </remarks>
    private bool HandleTransformKey(KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            return false;
        }

        if (_observedWorkspace is not { ActiveTransform: { } transform } workspace)
        {
            return false;
        }

        switch (e.Key)
        {
            case Key.Escape:
                workspace.CancelTransform();
                return true;

            case Key.Enter:
                workspace.CommitTransform();
                return true;

            case Key.Left:
                transform.MoveBy(-1, 0);
                return true;

            case Key.Right:
                transform.MoveBy(1, 0);
                return true;

            case Key.Up:
                transform.MoveBy(0, -1);
                return true;

            case Key.Down:
                transform.MoveBy(0, 1);
                return true;

            default:
                return false;
        }
    }

    /// <summary>Offers Enter and Escape to a tool that is holding something.</summary>
    private bool HandleInteractiveToolKey(KeyEventArgs e)
    {
        if (e.KeyModifiers is not KeyModifiers.None
            || ActiveTool is not IInteractiveTool interactive
            || ToolContext is not { } context)
        {
            return false;
        }

        return e.Key switch
        {
            Key.Enter => interactive.OnKeyDown(context, ToolKey.Enter),
            Key.Escape => interactive.OnKeyDown(context, ToolKey.Escape),
            _ => false,
        };
    }

    /// <summary>Takes hold of the lifted artwork at a point, so a drag moves it from there.</summary>
    private void BeginTransformDrag(Point position)
    {
        if (_observedWorkspace?.ActiveTransform is not { } transform)
        {
            return;
        }

        _isDraggingTransform = true;
        _transformGrabPoint = _transform.ToImagePoint(position);
        _transformGrabOffset = transform.Offset;
        _pointerPosition = position;
    }

    /// <summary>Moves the lifted artwork to follow the pointer.</summary>
    private void DragTransform(Point position)
    {
        if (_observedWorkspace?.ActiveTransform is not { } transform)
        {
            _isDraggingTransform = false;
            return;
        }

        ImagePoint now = _transform.ToImagePoint(position);

        transform.MoveTo(new ImagePoint(
            _transformGrabOffset.X + (now.X - _transformGrabPoint.X),
            _transformGrabOffset.Y + (now.Y - _transformGrabPoint.Y)));
    }

    /// <inheritdoc />
    protected override void OnTextInput(TextInputEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnTextInput(e);

        if (_observedWorkspace?.ActiveTextEdit is not { } editing || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        // Control characters arrive here on some platforms as well as through OnKeyDown, and
        // inserting them literally would put an invisible glyph into the artist's text.
        if (e.Text.Length == 1 && char.IsControl(e.Text[0]))
        {
            return;
        }

        editing.InsertText(e.Text);
        e.Handled = true;
    }

    /// <summary>
    /// Applies a keystroke to the text being typed.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the key belonged to the text editor. A key held with Control
    /// never does, so Ctrl+S still saves and Ctrl+Z still undoes while text is being edited.
    /// </returns>
    private bool HandleTextEditKey(TextEditSession editing, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            return false;
        }

        switch (e.Key)
        {
            case Key.Escape:
                _observedWorkspace?.CancelTextEdit();
                return true;

            case Key.Enter:
                editing.InsertText("\n");
                return true;

            case Key.Back:
                editing.DeleteBackward();
                return true;

            case Key.Delete:
                editing.DeleteForward();
                return true;

            case Key.Left:
                editing.MoveCaret(CaretMovement.Left);
                return true;

            case Key.Right:
                editing.MoveCaret(CaretMovement.Right);
                return true;

            case Key.Up:
                editing.MoveCaret(CaretMovement.Up);
                return true;

            case Key.Down:
                editing.MoveCaret(CaretMovement.Down);
                return true;

            case Key.Home:
                editing.MoveCaret(CaretMovement.LineStart);
                return true;

            case Key.End:
                editing.MoveCaret(CaretMovement.LineEnd);
                return true;

            case Key.Tab:
                editing.InsertText("    ");
                return true;

            default:
                // Every other key is deliberately left unhandled, and that is load-bearing:
                // on Windows a key marked handled never produces the character message that
                // becomes OnTextInput, so swallowing letters here to keep them away from the
                // tool shortcuts would mean the artist could not type a letter at all. The
                // shell asks the canvas whether text is being edited before claiming any bare
                // key — see MainView — which is where that conflict is actually resolved.
                return false;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (e is not null)
        {
            TrackModifiers(e.KeyModifiers);
        }

        if (e?.Key == Key.Space)
        {
            DisarmSpacePan();
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);

        TrackModifiers(KeyModifiers.None);

        // Alt-tabbing away while holding space means the key-up never arrives here, which
        // would leave the canvas stuck in pan mode with a hand cursor.
        DisarmSpacePan();
    }

    private void DisarmSpacePan()
    {
        if (!_isSpacePanArmed)
        {
            return;
        }

        _isSpacePanArmed = false;

        // A pan already under way is allowed to finish on the button release rather than
        // being cut short, which is what an artist who lifts space slightly early expects.
        UpdateCursor();
    }

    /// <summary>
    /// Reflects the current mode in the pointer: an open hand while pan mode is armed, a
    /// closed one while actually dragging, and the tool's own cursor otherwise.
    /// </summary>
    private void UpdateCursor()
    {
        // Pan mode overrides the tool's own cursor, because while space is held the canvas is
        // a grab handle and nothing else.
        if (_isSpacePanArmed)
        {
            Cursor = new Cursor(_isPanning ? StandardCursorType.SizeAll : StandardCursorType.Hand);
            return;
        }

        // A selection tool says what its modifiers will do, which takes precedence over its
        // plain crosshair; no tool both marks pixels and draws with a colour, so the sign and
        // the colour dot can never contend for the same corner.
        if (_presentation.Options.HasFlag(ToolOptions.Selection)
            && ToolCursors.SelectionMode(GetSelectionEditMode()) is { } signed)
        {
            Cursor = signed;
            return;
        }

        Cursor? retired = RefreshColorDotCursor();

        Cursor = _dotCursor ?? ToolCursors.For(_presentation);

        // Only now that the control has been given the replacement is the cursor it was
        // wearing certainly unused, and safe to hand its platform resource back.
        retired?.Dispose();
    }

    /// <summary>What the modifier keys currently held mean to a selection tool.</summary>
    private SelectionEditMode GetSelectionEditMode()
    {
        if (_modifiers.HasFlag(KeyModifiers.Control))
        {
            return SelectionEditMode.Subtract;
        }

        return _modifiers.HasFlag(KeyModifiers.Shift) ? SelectionEditMode.Add : SelectionEditMode.Replace;
    }

    /// <summary>
    /// Records the modifier keys and repaints the cursor if what they mean has changed.
    /// </summary>
    /// <remarks>
    /// Driven from pointer moves as well as from the keyboard, because a modifier pressed while
    /// the canvas does not have focus never arrives as a key event — and the pointer is over the
    /// canvas in exactly the case that matters.
    /// </remarks>
    private void TrackModifiers(KeyModifiers modifiers)
    {
        if (_modifiers == modifiers)
        {
            return;
        }

        SelectionEditMode before = GetSelectionEditMode();
        _modifiers = modifiers;

        if (GetSelectionEditMode() != before)
        {
            UpdateCursor();
        }
    }

    /// <summary>
    /// Brings the cursor's colour dot up to date, and returns the cursor it replaced.
    /// </summary>
    /// <returns>
    /// The cursor that is no longer wanted, for the caller to dispose <em>after</em> putting
    /// its replacement on the control, or <see langword="null"/> if nothing was replaced.
    /// </returns>
    /// <remarks>
    /// Only the tools that draw with the primary colour get one, and only those wearing a
    /// crosshair: the bucket and the text tool have cursors of their own that say something
    /// more useful, and fall back to the painted dot.
    /// </remarks>
    private Cursor? RefreshColorDotCursor()
    {
        bool wanted = ShowColorSwatch
            && _presentation.ShowsColorSwatch
            && _presentation.Cursor == ToolCursorKind.Crosshair
            && !_dotCursorUnavailable
            && ToolContext is not null;

        if (!wanted)
        {
            return TakeDotCursor();
        }

        PixelColor color = ToolContext!.Colors.Primary;

        if (_dotCursor is not null && _dotCursorColor == color)
        {
            return null;
        }

        if (ToolCursors.CreateColorDotCrosshair(color) is not { } built)
        {
            _dotCursorUnavailable = true;
            return TakeDotCursor();
        }

        Cursor? retired = _dotCursor;

        _dotCursor = built;
        _dotCursorColor = color;

        return retired;
    }

    /// <summary>Gives up the coloured cursor, handing it to the caller to dispose.</summary>
    private Cursor? TakeDotCursor()
    {
        Cursor? stale = _dotCursor;
        _dotCursor = null;
        return stale;
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

        // Space turns any button into a grab handle, so the check comes before the tool is
        // ever consulted; the middle button pans regardless of mode.
        if (properties.IsMiddleButtonPressed || _isSpacePanArmed)
        {
            _isPanning = true;
            _panOrigin = position;
            _panStartOffset = _transform.Offset;
            UpdateCursor();
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // A right press belongs to the tool's settings panel, not to the tool. It is left
        // entirely alone here — unhandled and uncaptured — so that the ContextFlyout the
        // document view attaches opens on the release, the way every context menu on the
        // platform does. This is why the secondary colour is reached with X rather than by
        // drawing with the right button.
        if (properties.IsRightButtonPressed)
        {
            return;
        }

        // A free transform owns the canvas while it lasts: the pixels are off the layer and
        // in the artist's hand, and a tool given a press now would draw into the hole.
        if (_observedWorkspace?.ActiveTransform is not null)
        {
            BeginTransformDrag(position);
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
        _pointerPosition = position;
        _lastKeyModifiers = e.KeyModifiers;
        tool.OnPointerPressed(context, CreateToolArgs(position, _activeButton, e.KeyModifiers));

        // A marking tool answers a press that lands on marked pixels by picking them up. It
        // has no way to tell the canvas that, so the canvas asks afterwards — otherwise the
        // artist would have to press a second time before the drag they already started took
        // hold of anything.
        if (_observedWorkspace?.ActiveTransform is not null)
        {
            _activeButton = ToolPointerButton.None;
            BeginTransformDrag(position);
        }

        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        Point position = e.GetPosition(this);
        UpdateHoveredPixel(position);
        TrackModifiers(e.KeyModifiers);

        if (_isPanning)
        {
            _hasUserAdjustedView = true;
            ApplyTransform(_transform with { Offset = _panStartOffset + (position - _panOrigin) });
            e.Handled = true;
            return;
        }

        if (_isDraggingTransform)
        {
            DragTransform(position);
            e.Handled = true;
            return;
        }

        if (ToolContext is not { } context || ActiveTool is not { } tool)
        {
            return;
        }

        _lastKeyModifiers = e.KeyModifiers;
        tool.OnPointerMoved(context, CreateToolArgs(position, _activeButton, e.KeyModifiers));
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isPanning)
        {
            _isPanning = false;
            UpdateCursor();
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        // The drag ends, the transform does not: the artist is free to pick the artwork up
        // again, nudge it with the arrow keys, or press Enter to put it down.
        if (_isDraggingTransform)
        {
            _isDraggingTransform = false;
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

        // The drag stops, but the transform itself survives: the pixels are still in the air
        // and the artist has not said where to put them.
        _isDraggingTransform = false;

        UpdateCursor();
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (_hoveredPixel is not null)
        {
            HoveredPixel = null;
            InvalidateVisual();
        }
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

        ObserveWorkspace(workspace);

        _renderTarget?.Dispose();
        _renderTarget = null;
        _observedDocument = workspace?.Document;
        _hasUserAdjustedView = false;

        if (_observedDocument is not null)
        {
            _observedDocument.Changed += OnDocumentChanged;
            _renderTarget = new DocumentRenderTarget(_observedDocument.Size);
        }

        DocumentSize = _observedDocument?.Size ?? default;

        // The colour picker samples what is on screen, and the render target is the only
        // thing that holds it.
        if (ToolContext is { } context)
        {
            context.CompositeSampler = _renderTarget is null ? null : _renderTarget.SampleComposite;
            context.ViewScale = _transform.Scale;
        }

        TryFitToViewport();
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

        // The off-canvas preview has no dirty-rectangle bookkeeping of its own: it is rebuilt
        // when a crop frame changes, which is rarely, and this keeps it honest in between.
        _overscan?.Invalidate();

        if (e.Kind == DocumentChangeKind.Size && _observedDocument is { } document)
        {
            DocumentSize = document.Size;
        }

        InvalidateVisual();
    }

    // ---------------------------------------------------------------------------------
    // Drawing helpers
    // ---------------------------------------------------------------------------------

    /// <summary>Fills a rectangle with the transparency checkerboard.</summary>
    /// <remarks>
    /// Normally the document's own rectangle. While a crop frame is up it covers the frame as
    /// well, so that the area about to be added reads as empty rather than as page background.
    /// </remarks>
    private void DrawCheckerboard(DrawingContext context, Rect area)
    {
        _checkerboardBrush ??= CreateCheckerboardBrush();
        context.FillRectangle(_checkerboardBrush, area);
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

    /// <summary>Rules the pixel grid over an area of the canvas.</summary>
    /// <remarks>
    /// <para>
    /// The area is the document, or the document and the crop frame together while one is up.
    /// Ruling the grid across the empty space the frame has been dragged into is what says that
    /// the space is made of pixels and can be kept — without it, dragging a frame outwards looks
    /// like dragging it into nothing.
    /// </para>
    /// <para>
    /// The grid is forced on for the same reason while the crop frame is up, whatever the View
    /// menu says, because there it is not decoration but the explanation of the gesture.
    /// </para>
    /// </remarks>
    private void DrawPixelGrid(DrawingContext context, ImageRect area, Rect areaRect, Rect viewport)
    {
        bool cropping = _observedOverlay?.CropFrame is not null;

        if ((!ShowPixelGrid && !cropping)
            || _transform.Scale < CanvasViewTransform.GridVisibilityScale
            || PixelGridBrush is not { } brush)
        {
            return;
        }

        Rect documentRect = areaRect;
        ImageSize size = area.Size;
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
    // Overlays
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Outlines the pixel under the pointer, and the footprint the brush would cover.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the brush cursor.</b> Drawing the footprint here rather than baking it into
    /// a mouse cursor is what lets it be exact: it is expressed in document pixels and scaled
    /// by the same transform as the artwork, so at 32× it is a large accurate ring and at 1×
    /// it is a few pixels, with no bitmap to regenerate when either the brush size or the zoom
    /// changes.
    /// </para>
    /// <para>
    /// The single hovered pixel is always outlined as well, even under a large brush. The
    /// footprint says what the brush will cover; the pixel says where the pointer actually is,
    /// and on a 32×32 sprite those are two different questions.
    /// </para>
    /// </remarks>
    private void DrawHoverHighlight(DrawingContext context)
    {
        if (_hoveredPixel is not { } pixel || _isSpacePanArmed || _isPanning)
        {
            return;
        }

        if (HighlightBrush is not { } bright || HighlightShadowBrush is not { } dark)
        {
            return;
        }

        if (_presentation.ShowsBrushOutline && _observedSettings is { BrushSize: > 1 } settings)
        {
            Rect footprint = _transform.GetPixelRect(BrushStamp.GetBounds(pixel, settings.BrushSize));

            if (settings.BrushShape == BrushShape.Circle)
            {
                // Tracing the rasterised disc exactly would give a jagged ring nobody can read
                // at low zoom. The inscribed ellipse says the same thing — this is the area the
                // brush covers — and stays legible at every magnification.
                DrawOutlinedEllipse(context, footprint, dark, bright);
            }
            else
            {
                DrawOutlinedRect(context, footprint, dark, bright);
            }
        }

        DrawOutlinedRect(context, _transform.GetPixelRect(new ImageRect(pixel, new ImageSize(1, 1))), dark, bright);

        DrawColorSwatch(context, dark, bright);
    }

    /// <summary>
    /// Draws a round dot of the colour the tool is about to lay down, beside the pointer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the fallback. Where the cursor itself can carry the dot it does, because a
    /// painted marker can only move when the canvas repaints and so trails the pointer from
    /// pixel to pixel; see <see cref="ToolCursors.CreateColorDotCrosshair"/>. What is left for
    /// here is the tools whose cursor says something more useful than a crosshair — the bucket
    /// and the text tool — and any platform that will not make a bitmap cursor at all.
    /// </para>
    /// <para>
    /// Up and to the right of the pointer, matching where the cursor puts it so that the two
    /// never disagree. It is drawn in view space at a fixed size rather than in document
    /// pixels, so it stays the same small dot at every magnification instead of growing into a
    /// disc at 32×.
    /// </para>
    /// </remarks>
    private void DrawColorSwatch(DrawingContext context, IBrush dark, IBrush bright)
    {
        if (!ShowColorSwatch
            || !_presentation.ShowsColorSwatch
            || _dotCursor is not null
            || _pointerPosition is not { } position
            || ToolContext is not { } tools)
        {
            return;
        }

        const double radius = 3.0;
        const double offsetX = 13.0;
        const double offsetY = -11.0;

        // The brush is cached rather than built per frame: this runs on every pointer move,
        // and the colour changes when a person clicks a swatch, not sixty times a second.
        PixelColor color = tools.Colors.Primary;
        if (_swatchBrush is null || _swatchColor != color)
        {
            _swatchColor = color;
            _swatchBrush = new ImmutableSolidColorBrush(color.ToAvalonia());
        }

        Point middle = new(position.X + offsetX, position.Y + offsetY);

        context.DrawEllipse(Brushes.White, null, middle, radius, radius);
        context.DrawEllipse(_swatchBrush, null, middle, radius, radius);
    }

    /// <summary>
    /// Gets a value indicating whether the colour dot is being painted rather than worn, and
    /// so has to be repainted as the pointer moves rather than only as it changes pixel.
    /// </summary>
    private bool IsPaintingColorDot =>
        ShowColorSwatch && _presentation.ShowsColorSwatch && _dotCursor is null;

    /// <summary>Outlines the current selection, if there is one.</summary>
    /// <remarks>
    /// <para>
    /// The mask's own edge is traced rather than its bounding rectangle. Ants around the bounds
    /// would tell an artist who has marked five scattered pixels that a box is selected, and
    /// they would believe it — then drag the box and watch five pixels move.
    /// </para>
    /// <para>
    /// The trace is cached, because it costs a pass over the selection's bounds and the canvas
    /// repaints for reasons that have nothing to do with the selection: every pointer move, every
    /// zoom, every stroke. It is thrown away whenever the workspace says the selection changed.
    /// </para>
    /// </remarks>
    private void DrawSelection(DrawingContext context, SelectionMask? selection)
    {
        if (selection is null
            || selection.IsEmpty
            || HighlightBrush is not { } bright
            || HighlightShadowBrush is not { } dark)
        {
            return;
        }

        _selectionOutline ??= SelectionOutline.Trace(selection);

        // While the artwork is in the air the outline goes with it, because the outline is what
        // the artist is aiming; the selection itself does not move until it is put down.
        ImagePoint shift = _observedWorkspace?.ActiveTransform is { } transform
            ? transform.Offset
            : ImagePoint.Origin;

        Pen shadowPen = new(dark, 1.0);
        Pen highlightPen = new(bright, 1.0) { DashStyle = MarchingAnts };

        foreach (SelectionOutline.Segment segment in _selectionOutline)
        {
            Point start = ToViewPoint(segment.Start, shift);
            Point end = ToViewPoint(segment.End, shift);

            context.DrawLine(shadowPen, start, end);
            context.DrawLine(highlightPen, start, end);
        }
    }

    /// <summary>Converts a selection outline's grid corner into a point on screen.</summary>
    /// <remarks>
    /// The half-pixel offset puts the one-pixel stroke on the device grid rather than straddling
    /// two rows of it, which would render the ants as a soft two-pixel smear.
    /// </remarks>
    private Point ToViewPoint(ImagePoint corner, ImagePoint shift)
    {
        Rect pixel = _transform.GetPixelRect(
            new ImageRect(corner.X + shift.X, corner.Y + shift.Y, 1, 1));

        return new Point(Math.Round(pixel.X) + 0.5, Math.Round(pixel.Y) + 0.5);
    }

    /// <summary>Outlines the rectangle a selection tool is currently dragging out.</summary>
    private void DrawMarquee(DrawingContext context)
    {
        if (_observedOverlay?.Marquee is not { } marquee
            || marquee.IsEmpty
            || HighlightBrush is not { } bright
            || HighlightShadowBrush is not { } dark)
        {
            return;
        }

        DrawMarchingAnts(context, _transform.GetPixelRect(marquee), dark, bright);
    }

    /// <summary>Draws the text caret while text is being typed.</summary>
    /// <remarks>
    /// The caret does not blink. Blinking needs a timer invalidating the canvas several times
    /// a second for as long as the artist is typing, which on a large document is a real cost
    /// for a decorative effect — and a solid caret over artwork is, if anything, easier to
    /// find than one that keeps disappearing.
    /// </remarks>
    private void DrawTextCaret(DrawingContext context)
    {
        if (_observedTextEdit is not { } editing
            || HighlightBrush is not { } bright
            || HighlightShadowBrush is not { } dark)
        {
            return;
        }

        Rect caret = _transform.GetPixelRect(editing.CaretBounds);

        // The caret is one image pixel wide, which at 100% zoom is a single screen pixel and
        // effectively invisible. A floor keeps it findable without making it inaccurate at
        // high zoom, where the true width is already wider than the floor.
        Rect visible = caret.Width < 2 ? caret.WithWidth(2) : caret;

        context.FillRectangle(dark, visible.Inflate(1));
        context.FillRectangle(bright, visible);
    }

    private static void DrawOutlinedRect(DrawingContext context, Rect rect, IBrush shadow, IBrush highlight)
    {
        // Half-pixel offsets put each one-pixel stroke on the device grid rather than
        // straddling two rows, which would render it as a soft two-pixel line.
        context.DrawRectangle(new Pen(shadow, 1.0), rect.Inflate(0.5));
        context.DrawRectangle(new Pen(highlight, 1.0), rect.Deflate(0.5));
    }

    private static void DrawOutlinedEllipse(DrawingContext context, Rect rect, IBrush shadow, IBrush highlight)
    {
        DrawEllipse(context, rect.Inflate(0.5), shadow);
        DrawEllipse(context, rect.Deflate(0.5), highlight);
    }

    private static void DrawEllipse(DrawingContext context, Rect rect, IBrush brush)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        context.DrawEllipse(null, new Pen(brush, 1.0), rect.Center, rect.Width / 2, rect.Height / 2);
    }

    /// <summary>
    /// Draws the artwork that currently lies outside the canvas, while a crop frame is up.
    /// </summary>
    /// <remarks>
    /// Only ever called with a crop frame showing, and only for the part of the region that is
    /// not the canvas — the canvas itself is drawn over this a moment later by the ordinary
    /// render target, which is the one that keeps a stroke cheap.
    /// </remarks>
    private void DrawOverscan(DrawingContext context, DocumentWorkspace workspace, ImageRect extent)
    {
        if (extent == workspace.Document.Bounds)
        {
            _overscan?.Dispose();
            _overscan = null;
            return;
        }

        _overscan ??= new OverscanRenderTarget();

        if (_overscan.GetBitmap(workspace.Document, workspace.ActiveFrame, extent) is not { } bitmap)
        {
            return;
        }

        context.DrawImage(bitmap, new Rect(bitmap.Size), _transform.GetPixelRect(extent));
    }

    /// <summary>
    /// Draws the crop frame: the area outside it dimmed, and a handle on each corner and edge.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Handles, because an edge is not a target.</b> A dashed line says "this is a boundary";
    /// a solid square at the corner of one says "take hold of me here", and an artist who has
    /// used any other editor already knows to reach for it. They are drawn at a fixed size in
    /// screen pixels and hit-tested at the same distance, so what looks grabbable is grabbable
    /// at every zoom — see <see cref="CropTool.GripScreenDistance"/>.
    /// </para>
    /// <para>
    /// <b>Dimming outside, not inside.</b> The frame is what will be kept, so it stays at full
    /// brightness and everything else is shaded — including the parts of the current picture
    /// about to be trimmed away, which is precisely the thing the artist is deciding about.
    /// </para>
    /// </remarks>
    private void DrawCropFrame(DrawingContext context, ImageRect? cropFrame, Rect viewport)
    {
        if (cropFrame is not { } frame
            || frame.IsEmpty
            || HighlightBrush is not { } bright
            || HighlightShadowBrush is not { } dark)
        {
            return;
        }

        Rect rect = _transform.GetPixelRect(frame);

        // Four bands rather than a path with a hole: a rectangle subtracted from a rectangle is
        // always four rectangles, and four fills are cheaper than a geometry every frame.
        context.FillRectangle(CropShade, new Rect(viewport.X, viewport.Y, viewport.Width, rect.Top - viewport.Y));
        context.FillRectangle(CropShade, new Rect(viewport.X, rect.Bottom, viewport.Width, viewport.Bottom - rect.Bottom));
        context.FillRectangle(CropShade, new Rect(viewport.X, rect.Top, rect.Left - viewport.X, rect.Height));
        context.FillRectangle(CropShade, new Rect(rect.Right, rect.Top, viewport.Right - rect.Right, rect.Height));

        DrawOutlinedRect(context, rect, dark, bright);

        // Thirds, the composition guide every crop tool draws. Faint, because they are advice.
        DrawCropThirds(context, rect, dark);

        foreach (Point handle in GetCropHandles(rect))
        {
            Rect box = new(
                handle.X - (CropHandleSize / 2.0),
                handle.Y - (CropHandleSize / 2.0),
                CropHandleSize,
                CropHandleSize);

            context.FillRectangle(dark, box.Inflate(1));
            context.FillRectangle(bright, box);
        }
    }

    /// <summary>The eight points a crop frame can be taken hold of: four corners, four edges.</summary>
    private static IEnumerable<Point> GetCropHandles(Rect rect)
    {
        double midX = rect.X + (rect.Width / 2.0);
        double midY = rect.Y + (rect.Height / 2.0);

        yield return new Point(rect.Left, rect.Top);
        yield return new Point(midX, rect.Top);
        yield return new Point(rect.Right, rect.Top);
        yield return new Point(rect.Right, midY);
        yield return new Point(rect.Right, rect.Bottom);
        yield return new Point(midX, rect.Bottom);
        yield return new Point(rect.Left, rect.Bottom);
        yield return new Point(rect.Left, midY);
    }

    /// <summary>Rules the rule-of-thirds guides inside a crop frame.</summary>
    /// <remarks>
    /// Skipped on a frame too small to divide meaningfully: on a 16-pixel sprite the guides would
    /// be three lines two pixels apart, which is clutter rather than composition.
    /// </remarks>
    private static void DrawCropThirds(DrawingContext context, Rect rect, IBrush brush)
    {
        const double minimumSize = 60.0;

        if (rect.Width < minimumSize || rect.Height < minimumSize)
        {
            return;
        }

        Pen pen = new(brush, 1.0) { DashStyle = new ImmutableDashStyle([2, 4], 0) };

        for (int i = 1; i <= 2; i++)
        {
            double x = Math.Round(rect.X + (rect.Width * i / 3.0)) + 0.5;
            double y = Math.Round(rect.Y + (rect.Height * i / 3.0)) + 0.5;

            context.DrawLine(pen, new Point(x, rect.Top), new Point(x, rect.Bottom));
            context.DrawLine(pen, new Point(rect.Left, y), new Point(rect.Right, y));
        }
    }

    private static void DrawMarchingAnts(DrawingContext context, Rect rect, IBrush shadow, IBrush highlight)
    {
        // A solid dark line with a dashed bright one over it: the gaps in the bright dashes
        // show the dark line beneath, so the edge stays visible whatever it crosses.
        context.DrawRectangle(new Pen(shadow, 1.0), rect.Inflate(0.5));
        context.DrawRectangle(new Pen(highlight, 1.0) { DashStyle = MarchingAnts }, rect.Inflate(0.5));
    }

    // ---------------------------------------------------------------------------------
    // Observation
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Follows the workspace's selection and its text editing state, so that either one
    /// changing repaints the canvas.
    /// </summary>
    private void ObserveWorkspace(DocumentWorkspace? workspace)
    {
        if (ReferenceEquals(_observedWorkspace, workspace))
        {
            return;
        }

        // A document swapped away while its text is half-typed, or while its pixels are in
        // mid-air, must keep the work rather than discard it. Committing here is what makes
        // clicking another tab safe.
        _observedWorkspace?.CommitTextEdit();
        _observedWorkspace?.CommitTransform();

        if (_observedWorkspace is not null)
        {
            _observedWorkspace.PropertyChanged -= OnWorkspacePropertyChanged;
        }

        _observedWorkspace = workspace;

        if (_observedWorkspace is not null)
        {
            _observedWorkspace.PropertyChanged += OnWorkspacePropertyChanged;
        }

        ObserveTextEdit(_observedWorkspace?.ActiveTextEdit);
    }

    private void ObserveToolContext(ToolContext? toolContext)
    {
        ColorSlots? colors = toolContext?.Colors;

        if (!ReferenceEquals(_observedColors, colors))
        {
            if (_observedColors is not null)
            {
                _observedColors.PropertyChanged -= OnColorSlotsChanged;
            }

            _observedColors = colors;

            if (_observedColors is not null)
            {
                _observedColors.PropertyChanged += OnColorSlotsChanged;
            }
        }

        ToolSettings? settings = toolContext?.Settings;

        if (!ReferenceEquals(_observedSettings, settings))
        {
            if (_observedSettings is not null)
            {
                _observedSettings.PropertyChanged -= OnToolSettingsChanged;
            }

            _observedSettings = settings;

            if (_observedSettings is not null)
            {
                _observedSettings.PropertyChanged += OnToolSettingsChanged;
            }
        }

        ToolOverlay? overlay = toolContext?.Overlay;

        if (ReferenceEquals(_observedOverlay, overlay))
        {
            return;
        }

        if (_observedOverlay is not null)
        {
            _observedOverlay.PropertyChanged -= OnOverlayChanged;
        }

        _observedOverlay = overlay;

        if (_observedOverlay is not null)
        {
            _observedOverlay.PropertyChanged += OnOverlayChanged;
        }
    }

    /// <summary>
    /// Follows the onion skin settings, so that turning ghosts on or changing their strength
    /// repaints without anything else having to notice.
    /// </summary>
    private void ObserveOnionSkin(OnionSkinSettings? settings)
    {
        if (ReferenceEquals(_observedOnionSkin, settings))
        {
            return;
        }

        if (_observedOnionSkin is not null)
        {
            _observedOnionSkin.PropertyChanged -= OnOnionSkinChanged;
        }

        _observedOnionSkin = settings;

        if (_observedOnionSkin is not null)
        {
            _observedOnionSkin.PropertyChanged += OnOnionSkinChanged;
        }

        OnOnionSkinChanged(this, new PropertyChangedEventArgs(null));
    }

    private void OnOnionSkinChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Every one of these settings changes what the whole canvas looks like, so there is
        // no useful dirty rectangle to compute.
        _renderTarget?.InvalidateAll();
        InvalidateVisual();
    }

    private void ObserveTextEdit(TextEditSession? session)
    {
        if (ReferenceEquals(_observedTextEdit, session))
        {
            return;
        }

        if (_observedTextEdit is not null)
        {
            _observedTextEdit.Changed -= OnTextEditChanged;
        }

        _observedTextEdit = session;

        if (_observedTextEdit is not null)
        {
            _observedTextEdit.Changed += OnTextEditChanged;

            // Keystrokes only reach a focused control, and the click that started the edit may
            // have gone to a panel rather than to the canvas.
            Focus();
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Switches the cursor and finishes any typing when the artist picks a different tool.
    /// </summary>
    /// <remarks>
    /// Ending the text edit and handing over a stroke here rather than in the tools is
    /// deliberate: a tool is never told that it has been deselected. The canvas is the thing
    /// that knows.
    /// </remarks>
    private void OnActiveToolChanged(ITool? previous, ITool? tool)
    {
        _observedWorkspace?.CommitTextEdit();

        // Picking up a different tool is as clear a way of saying "I have finished moving
        // this" as pressing Enter, and leaving the artwork in mid-air while a pencil draws
        // underneath it would be indefensible.
        _observedWorkspace?.CommitTransform();

        HandOverActiveStroke(previous, tool);

        // A tool that holds something between clicks — the crop frame — is told when it is
        // picked up and put down, because nothing else knows.
        if (ToolContext is { } context)
        {
            (previous as IInteractiveTool)?.OnDeactivated(context);
            (tool as IInteractiveTool)?.OnActivated(context);
        }

        _presentation = ToolPresentations.For(tool?.Id);
        UpdateCursor();
        InvalidateVisual();
    }

    /// <summary>
    /// Ends the outgoing tool's stroke and starts the incoming tool's, when the artist
    /// switches tools with the button still held.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pressing E halfway through a pencil stroke should carry on erasing from where the
    /// pencil had got to, and that is worth having on its own. But the handover is not only a
    /// convenience: without it the outgoing tool is simply abandoned mid-stroke. Pointer
    /// events are routed to whichever tool is active <em>now</em>, so the pencil would never
    /// see its release, and its open raster edit session — which is holding every pixel's
    /// pre-stroke value for the undo stack — would sit there until the tool's next press
    /// rolled the whole thing back. That is what used to resurrect erased artwork after a bout
    /// of switching between the pencil and the eraser.
    /// </para>
    /// <para>
    /// Both tools are given the same position and button, so nothing is drawn at a place the
    /// pointer never was; the incoming tool simply begins where the outgoing one stopped.
    /// </para>
    /// </remarks>
    private void HandOverActiveStroke(ITool? outgoing, ITool? incoming)
    {
        if (outgoing is null || _activeButton == ToolPointerButton.None || ToolContext is not { } context)
        {
            return;
        }

        // Belt and braces: the press records a position, so this cannot normally happen. If it
        // ever did, dropping the stroke would still be far better than leaking its session.
        if (_pointerPosition is not { } position)
        {
            outgoing.Cancel(context);
            return;
        }

        ToolPointerEventArgs args = CreateToolArgs(position, _activeButton, _lastKeyModifiers);

        outgoing.OnPointerReleased(context, args);
        incoming?.OnPointerPressed(context, args);
    }

    /// <summary>
    /// Rebuilds the cursor when the drawing colour changes, since the cursor is carrying it.
    /// </summary>
    private void OnColorSlotsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e?.PropertyName is not (null or nameof(ColorSlots.Primary)))
        {
            return;
        }

        UpdateCursor();
        InvalidateVisual();
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DocumentWorkspace.Selection):
            case nameof(DocumentWorkspace.ActiveTransform):
                _selectionOutline = null;
                InvalidateVisual();
                break;

            case nameof(DocumentWorkspace.ActiveTextEdit):
                ObserveTextEdit(_observedWorkspace?.ActiveTextEdit);
                break;

            // Moving along the timeline changes every pixel on screen, and onion skinning
            // means the neighbouring frames change with it.
            case nameof(DocumentWorkspace.ActiveFrame):
            case nameof(DocumentWorkspace.ActiveTag):
                _renderTarget?.InvalidateAll();
                InvalidateVisual();
                break;

            default:
                break;
        }
    }

    private void OnToolSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The hover footprint is drawn from the brush size and shape, so changing either has
        // to repaint even though no pixel of the artwork moved.
        if (e.PropertyName is nameof(ToolSettings.BrushSize) or nameof(ToolSettings.BrushShape))
        {
            InvalidateVisual();
        }
    }

    private void OnOverlayChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    private void OnTextEditChanged(object? sender, EventArgs e) => InvalidateVisual();

    // ---------------------------------------------------------------------------------
    // View manipulation
    // ---------------------------------------------------------------------------------

    private void ZoomStep(bool zoomIn, Point focus)
    {
        // Every stepped zoom is a deliberate act — a menu command, a shortcut or the wheel —
        // so from here on the view is the artist's and must not be re-fitted behind them.
        _hasUserAdjustedView = true;
        ApplyTransform(_transform.ZoomTo(_transform.GetSteppedScale(zoomIn, ZoomSpeed), focus));
    }

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
        ViewTransform = transform;
        SetAndRaise(ZoomProperty, ref _zoom, transform.Scale);
        PublishViewScale();
        InvalidateVisual();
    }

    /// <summary>
    /// Tells the tools how far the artist has zoomed in.
    /// </summary>
    /// <remarks>
    /// Only one thing uses it, and only for hit-testing: how near the pointer has to come to a
    /// crop handle before it takes hold of it, which is a distance in fingertips rather than in
    /// pixels of artwork. Nothing that draws may consult it — see
    /// <see cref="ToolContext.ViewScale"/>.
    /// </remarks>
    private void PublishViewScale()
    {
        if (ToolContext is { } context)
        {
            context.ViewScale = _transform.Scale;
        }
    }

    private void UpdateHoveredPixel(Point position)
    {
        _pointerPosition = position;

        ImagePoint? previous = _hoveredPixel;

        if (Workspace is not { } workspace)
        {
            HoveredPixel = null;
        }
        else
        {
            ImagePoint point = _transform.ToImagePoint(position);
            HoveredPixel = workspace.Document.Bounds.Contains(point) ? point : null;
        }

        // The highlight follows the pointer, so it has to be repainted when the pointer moves
        // to a different pixel — and only then, or a canvas would repaint at the pointer's
        // full sample rate while drawing changes nothing. The painted colour dot is the
        // exception: it follows the pointer rather than the pixel grid, and repainting it only
        // on a change of pixel is what makes it look as though it is dragged along behind.
        if (previous != _hoveredPixel || IsPaintingColorDot)
        {
            InvalidateVisual();
        }
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
