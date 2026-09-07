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
/// <summary>What the pointer is over that can be taken hold of and dragged.</summary>
/// <remarks>
/// The canvas has three draggable things that are not the artwork — the edge of a selection,
/// the crop frame, and whichever of those is currently being held — and each of them wants to
/// say so through the cursor. Naming the states keeps that decision in one place rather than
/// spread through the pointer handlers as a set of overlapping booleans.
/// </remarks>
internal enum CanvasGrab
{
    /// <summary>An edge or corner of a free transform box, which resizes it.</summary>
    BoxEdge = 100,

    /// <summary>The body of a free transform box, which moves it.</summary>
    BoxBody = 101,

    /// <summary>The border of the text block being edited, which moves the words.</summary>
    TextEdge = 102,

    /// <summary>Nothing; the active tool's own cursor applies.</summary>
    None,

    /// <summary>The marching ants, or artwork already lifted: a press moves the marked pixels.</summary>
    Marks,

    /// <summary>The crop frame, which a press would take hold of.</summary>
    Frame,

    /// <summary>Something is being dragged right now.</summary>
    Held,
}

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

    /// <summary>Whether the grey checkerboard is drawn behind the artwork to mean "nothing here".</summary>
    /// <remarks>
    /// On by default, because a transparent pixel and a white one are otherwise the same
    /// picture and an artist needs to be able to tell them apart. It can be switched off
    /// because the checks are also visual noise: fine detail drawn over them is read against a
    /// moving pattern rather than against a flat ground, and at that point the honest answer is
    /// to let the page background show through instead.
    /// </remarks>
    /// <summary>
    /// Whether the check pattern is also laid over the artwork itself.
    /// </summary>
    /// <remarks>
    /// A second, separate pattern from the one behind the picture, and off by default. It is
    /// masked to the pixels that actually carry paint, so empty canvas is left to the
    /// background pattern alone, and it is drawn faintly enough to read as a texture over the
    /// colours rather than instead of them.
    /// </remarks>
    public static readonly StyledProperty<bool> ShowArtworkCheckerProperty =
        AvaloniaProperty.Register<PixelCanvasControl, bool>(nameof(ShowArtworkChecker));

    public static readonly StyledProperty<bool> ShowTransparencyCheckerProperty =
        AvaloniaProperty.Register<PixelCanvasControl, bool>(nameof(ShowTransparencyChecker), defaultValue: true);

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

    /// <summary>
    /// How strongly the artwork check pattern is laid over the picture.
    /// </summary>
    /// <remarks>
    /// Far short of opaque. The pattern is meant to sit on the artwork as a texture, and at
    /// full strength it would simply replace the colours it covers - which is a pattern, not a
    /// drawing with a pattern on it.
    /// </remarks>
    private const double ArtworkCheckerOpacity = 0.4;

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
    /// The wash behind selected characters. Translucent, because it goes over glyphs that are
    /// already drawn and has to leave them readable.
    /// </summary>
    private static readonly IBrush TextSelectionFill = new ImmutableSolidColorBrush(Color.FromArgb(90, 90, 150, 255));

    /// <summary>
    /// The size of a crop handle in screen pixels, matched to the distance at which the tool
    /// treats a press as grabbing one.
    /// </summary>
    private const double CropHandleSize = CropTool.GripScreenDistance;

    /// <summary>
    /// How near the marching ants the pointer must come, in screen pixels, before a press
    /// there picks the marked pixels up rather than doing what the current tool would do.
    /// </summary>
    /// <remarks>
    /// Measured on screen rather than in image pixels, for the same reason the crop grips are:
    /// it is a question about how accurately a person can aim a mouse, and the answer does not
    /// change with the zoom. Deliberately a little tighter than the crop grip, because the edge
    /// of a selection runs through the middle of the artwork and every pixel it does not claim
    /// is a pixel the artist can still draw on.
    /// </remarks>
    private const double SelectionGrabDistance = 5.0;

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

    /// <summary>What a press at the pointer's current position would take hold of.</summary>
    /// <remarks>
    /// Recomputed on every pointer move and reflected in the cursor, so that the answer to
    /// "can I drag this?" is given before the artist commits to finding out.
    /// </remarks>
    private CanvasGrab _grab = CanvasGrab.None;

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
    /// Which part of the crop frame or the transform box the pointer is over, kept beside
    /// <see cref="_grab"/> because the cursor for an edge depends on <em>which</em> edge.
    /// </summary>
    private CropGrip _cropGrip = CropGrip.None;
    private TransformGrip _boxGrip = TransformGrip.None;

    /// <summary>
    /// The transform box drag in progress: which grip is held, and where the pointer went down.
    /// </summary>
    private TransformGrip _draggingBoxGrip = TransformGrip.None;
    private ImagePoint _boxGrabPoint;
    private bool _isDraggingBox;

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

    /// <summary>Whether the grey checkerboard is drawn behind the artwork.</summary>
    public bool ShowTransparencyChecker
    {
        get => GetValue(ShowTransparencyCheckerProperty);
        set => SetValue(ShowTransparencyCheckerProperty, value);
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
    /// <summary>Whether the check pattern is also laid over the drawn pixels.</summary>
    public bool ShowArtworkChecker
    {
        get => GetValue(ShowArtworkCheckerProperty);
        set => SetValue(ShowArtworkCheckerProperty, value);
    }

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
    /// Raised when something on the canvas needs a different tool in hand.
    /// </summary>
    /// <remarks>
    /// Double-clicking text is the only thing that raises it so far. The canvas cannot pick a
    /// tool itself - it is given one, and knows nothing about the toolbox - so it says which
    /// tool the gesture needs and the document view, which knows both, does the picking.
    /// </remarks>
    public event EventHandler<string>? ToolRequested;

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

        // While cropping, the ground is ruled right across the window rather than stopping at
        // the frame. Every pixel out there is somewhere the frame could be dragged to, and a
        // frame edge that runs into blank shell background looks like the end of the world
        // rather than like the edge of a choice. The shade drawn over it a moment later is what
        // makes it read as "outside", which is the arrangement Photoshop and the rest settled on.
        ImageRect ground = cropFrame is not null ? GetViewportBounds().Union(extent) : extent;

        DrawCheckerboard(context, _transform.GetPixelRect(ground));
        DrawOverscan(context, workspace, extent);

        WriteableBitmap bitmap = _renderTarget.GetBitmap(
            workspace.Document,
            workspace.ActiveFrame,
            OnionSkin,
            workspace.ActiveTag);
        context.DrawImage(bitmap, new Rect(bitmap.Size), documentRect);
        DrawArtworkChecker(context, bitmap, documentRect);

        DrawPixelGrid(context, ground, _transform.GetPixelRect(ground), viewport);
        DrawDocumentBorder(context, documentRect);

        // Overlays go on top of the artwork and of each other in the order an artist reads
        // them: what is already selected, what is being selected right now, and last of all
        // where the pointer is about to act.
        DrawSelection(context, workspace.Selection);
        DrawMarquee(context);
        DrawTransformBox(context);
        DrawCropFrame(context, cropFrame, viewport);
        DrawHoverHighlight(context);
        DrawTextBox(context);
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
        else if (change.Property == ShowArtworkCheckerProperty)
        {
            InvalidateVisual();
        }
        else if (change.Property == CheckerboardLightProperty || change.Property == CheckerboardDarkProperty)
        {
            _checkerboardBrush = null;
            InvalidateVisual();
        }
        else if (change.Property == ShowPixelGridProperty || change.Property == ShowTransparencyCheckerProperty)
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

        // Ctrl+A inside text selects the words, not the canvas. It is caught before the
        // shell's key binding can fire, which is the only way to have one shortcut mean two
        // things depending on where the artist is working.
        if (_observedWorkspace?.ActiveTextEdit is { } typing
            && e.Key == Key.A
            && e.KeyModifiers == KeyModifiers.Control)
        {
            typing.SelectAll();
            e.Handled = true;
            return;
        }

        // Typing wins over every bare shortcut. Marking the key handled is what stops it
        // reaching the shell's key bindings, where "x" swaps the drawing colours — which is
        // not what an artist typing the word "next" is asking for.
        if (_observedWorkspace?.ActiveTextEdit is { } editing && HandleTextEditKey(editing, e))
        {
            e.Handled = true;
            return;
        }

        if (_observedWorkspace?.ActiveFreeTransform is not null && HandleFreeTransformKey(e))
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

    /// <summary>
    /// Applies a keystroke to the transform box.
    /// </summary>
    /// <returns><see langword="true"/> if the key belonged to the box.</returns>
    /// <remarks>
    /// The letters are bare rather than modified, and they shadow the tool shortcuts for as long
    /// as the box is up. That is the right trade: while a transform is in progress the artist is
    /// not choosing tools, and R for rotate and H and V for the two flips are what every editor
    /// that has a transform box uses. A key held with Control never belongs here, so Ctrl+Z
    /// still undoes and Ctrl+T still applies the box.
    /// </remarks>
    private bool HandleFreeTransformKey(KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            return false;
        }

        if (_observedWorkspace is not { ActiveFreeTransform: { } box } workspace)
        {
            return false;
        }

        switch (e.Key)
        {
            case Key.Escape:
                workspace.CancelFreeTransform();
                return true;

            case Key.Enter:
                workspace.CommitFreeTransform();
                return true;

            case Key.Left:
                box.MoveBy(-1, 0);
                return true;

            case Key.Right:
                box.MoveBy(1, 0);
                return true;

            case Key.Up:
                box.MoveBy(0, -1);
                return true;

            case Key.Down:
                box.MoveBy(0, 1);
                return true;

            case Key.R:
                box.Rotate(!e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                return true;

            case Key.H:
                box.Flip(horizontally: true);
                return true;

            case Key.V:
                box.Flip(horizontally: false);
                return true;

            default:
                return false;
        }
    }

    /// <summary>Takes hold of the transform box, so a drag moves or resizes it.</summary>
    private void BeginBoxDrag(FreeTransform box, TransformGrip grip, Point position)
    {
        _isDraggingBox = true;
        _draggingBoxGrip = grip;
        _boxGrabPoint = _transform.ToImagePoint(position);
        _pointerPosition = position;

        box.BeginDrag();
    }

    /// <summary>Moves or resizes the transform box to follow the pointer.</summary>
    /// <remarks>
    /// Measured from the box as it was when the press landed rather than from the last pointer
    /// sample, which is what lets a handle be dragged past its opposite edge and back again
    /// without the artwork flipping once per pointer sample on the way.
    /// </remarks>
    private void DragBox(Point position)
    {
        if (_observedWorkspace?.ActiveFreeTransform is not { } box)
        {
            _isDraggingBox = false;
            return;
        }

        ImagePoint now = _transform.ToImagePoint(position);
        ImageRect start = box.DragBounds;

        int dx = now.X - _boxGrabPoint.X;
        int dy = now.Y - _boxGrabPoint.Y;

        if (_draggingBoxGrip == TransformGrip.Inside)
        {
            box.DragEdges(start.Left + dx, start.Top + dy, start.Right + dx, start.Bottom + dy);
            return;
        }

        int left = start.Left + (_draggingBoxGrip.HasFlag(TransformGrip.Left) ? dx : 0);
        int right = start.Right + (_draggingBoxGrip.HasFlag(TransformGrip.Right) ? dx : 0);
        int top = start.Top + (_draggingBoxGrip.HasFlag(TransformGrip.Top) ? dy : 0);
        int bottom = start.Bottom + (_draggingBoxGrip.HasFlag(TransformGrip.Bottom) ? dy : 0);

        (left, top, right, bottom) = ConstrainProportions(start, left, top, right, bottom);

        box.DragEdges(left, top, right, bottom);
    }

    /// <summary>
    /// Holds a corner drag to the box's original proportions while Shift is down.
    /// </summary>
    /// <remarks>
    /// Only corners, because an edge drag has one axis and nothing to be proportional to. The
    /// larger of the two scales wins, so the box follows whichever way the pointer has moved
    /// further, and each axis keeps its own sign so that dragging a corner right across the box
    /// still mirrors the artwork.
    /// </remarks>
    private (int Left, int Top, int Right, int Bottom) ConstrainProportions(
        ImageRect start, int left, int top, int right, int bottom)
    {
        bool corner = (_draggingBoxGrip & (TransformGrip.Left | TransformGrip.Right)) != 0
            && (_draggingBoxGrip & (TransformGrip.Top | TransformGrip.Bottom)) != 0;

        if (!corner || !_modifiers.HasFlag(KeyModifiers.Shift) || start.Width <= 0 || start.Height <= 0)
        {
            return (left, top, right, bottom);
        }

        double scaleX = (double)(right - left) / start.Width;
        double scaleY = (double)(bottom - top) / start.Height;
        double scale = Math.Max(Math.Abs(scaleX), Math.Abs(scaleY));

        int width = (int)Math.Round(start.Width * scale * (scaleX < 0 ? -1 : 1));
        int height = (int)Math.Round(start.Height * scale * (scaleY < 0 ? -1 : 1));

        if (_draggingBoxGrip.HasFlag(TransformGrip.Left))
        {
            left = right - width;
        }
        else
        {
            right = left + width;
        }

        if (_draggingBoxGrip.HasFlag(TransformGrip.Top))
        {
            top = bottom - height;
        }
        else
        {
            bottom = top + height;
        }

        return (left, top, right, bottom);
    }

    /// <summary>
    /// Opens the text layer under a point for editing, whatever tool is in hand.
    /// </summary>
    /// <returns><see langword="true"/> if there was text there and it is now being edited.</returns>
    private bool TryBeginTextEditAt(Point position)
    {
        if (_observedWorkspace is not { } workspace || ToolContext is not { } context)
        {
            return false;
        }

        ImagePoint point = _transform.ToImagePoint(position);

        if (TextTool.FindTextLayerAt(workspace.Document, context.TextRasterizer, point) is not { } layer)
        {
            return false;
        }

        // Already in this text: the double-click was two clicks of the caret, and re-opening
        // the session would throw away everything typed since it started.
        if (ReferenceEquals(_observedTextEdit?.Layer, layer))
        {
            return false;
        }

        workspace.CommitTextEdit();
        workspace.ActiveLayer = layer;

        TextEditSession session = new(workspace, layer, context.TextRasterizer, isNewLayer: false);
        workspace.BeginTextEdit(session);
        session.MoveCaretTo(point);

        // The text tool has to come with it: presses go to whichever tool is active, and a
        // pencil would answer the next click by drawing over the words being edited.
        ToolRequested?.Invoke(this, TextTool.ToolId);
        return true;
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
                editing.MoveCaret(CaretMovement.Left, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                return true;

            case Key.Right:
                editing.MoveCaret(CaretMovement.Right, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
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

        // Something grabbable under the pointer outranks the tool's own cursor, because it
        // outranks the tool: a press there will drag that thing rather than draw. An open hand
        // over the crop frame, the four-way arrow over marked pixels and over anything actually
        // being held — which is as close to "grabbing" as the platforms agree on.
        switch (_grab)
        {
            case CanvasGrab.Frame:
                // An edge says which way it moves, so the pointer promises the axis before the
                // press: left and right edges point across, top and bottom point up and down,
                // and a corner points along its own diagonal. Inside the frame the whole thing
                // moves, which is the four-way arrow.
                Cursor = new Cursor(EdgeCursor(_cropGrip));
                return;

            case CanvasGrab.BoxEdge:
                Cursor = new Cursor(BoxEdgeCursor(_boxGrip));
                return;

            case CanvasGrab.BoxBody:
            case CanvasGrab.TextEdge:
            case CanvasGrab.Marks:
            case CanvasGrab.Held:
                Cursor = new Cursor(StandardCursorType.SizeAll);
                return;

            default:
                break;
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
    /// Works out what a press at a point would take hold of, and repaints the cursor if the
    /// answer has changed.
    /// </summary>
    /// <param name="position">Where the pointer is, in view space, or <see langword="null"/> if it has left.</param>
    /// <remarks>
    /// Nothing is grabbable while a drag is already under way, apart from the drag itself:
    /// asking again mid-gesture would flicker the cursor between what is being held and
    /// whatever it happens to be passing over.
    /// </remarks>
    private void UpdateGrab(Point? position)
    {
        CropGrip crop = _cropGrip;
        TransformGrip box = _boxGrip;

        _cropGrip = CropGrip.None;
        _boxGrip = TransformGrip.None;

        CanvasGrab grab = GetGrab(position);

        if (_grab == grab && _cropGrip == crop && _boxGrip == box)
        {
            return;
        }

        _grab = grab;
        UpdateCursor();
    }

    /// <summary>The pointer for a crop frame grip: an axis arrow, or the four-way arrow.</summary>
    private static StandardCursorType EdgeCursor(CropGrip grip) => grip switch
    {
        CropGrip.Left or CropGrip.Right => StandardCursorType.SizeWestEast,
        CropGrip.Top or CropGrip.Bottom => StandardCursorType.SizeNorthSouth,
        CropGrip.Left | CropGrip.Top => StandardCursorType.TopLeftCorner,
        CropGrip.Right | CropGrip.Bottom => StandardCursorType.BottomRightCorner,
        CropGrip.Right | CropGrip.Top => StandardCursorType.TopRightCorner,
        CropGrip.Left | CropGrip.Bottom => StandardCursorType.BottomLeftCorner,
        _ => StandardCursorType.SizeAll,
    };

    /// <summary>The pointer for a transform box grip, which reads the same as a crop grip.</summary>
    private static StandardCursorType BoxEdgeCursor(TransformGrip grip) => grip switch
    {
        TransformGrip.Left or TransformGrip.Right => StandardCursorType.SizeWestEast,
        TransformGrip.Top or TransformGrip.Bottom => StandardCursorType.SizeNorthSouth,
        TransformGrip.Left | TransformGrip.Top => StandardCursorType.TopLeftCorner,
        TransformGrip.Right | TransformGrip.Bottom => StandardCursorType.BottomRightCorner,
        TransformGrip.Right | TransformGrip.Top => StandardCursorType.TopRightCorner,
        TransformGrip.Left | TransformGrip.Bottom => StandardCursorType.BottomLeftCorner,
        _ => StandardCursorType.SizeAll,
    };

    /// <summary>How near something has to be to count as grabbable, in image pixels.</summary>
    private int GetGripTolerance(double screenDistance) =>
        Math.Max(1, (int)Math.Round(screenDistance / Math.Max(_transform.Scale, 0.01)));

    /// <summary>Answers what is under the pointer, without changing anything.</summary>
    private CanvasGrab GetGrab(Point? position)
    {
        if (_isDraggingBox)
        {
            _boxGrip = _draggingBoxGrip;
            return _draggingBoxGrip == TransformGrip.Inside ? CanvasGrab.BoxBody : CanvasGrab.BoxEdge;
        }

        // A crop frame being dragged keeps the arrow of the edge it took hold of, so the
        // pointer goes on saying which way the edge is travelling for as long as it travels.
        if (ActiveTool is CropTool { IsDragging: true } held)
        {
            _cropGrip = held.CurrentGrip;
            return CanvasGrab.Frame;
        }

        if (_isDraggingTransform)
        {
            return CanvasGrab.Held;
        }

        if (position is not { } point || _isPanning || _activeButton != ToolPointerButton.None)
        {
            return CanvasGrab.None;
        }

        // A transform box owns the canvas while it is up: nothing else can be grabbed, because
        // every press either adjusts the box or puts it down.
        if (_observedWorkspace?.ActiveFreeTransform is { } box)
        {
            _boxGrip = box.HitTest(
                _transform.ToImagePoint(point),
                GetGripTolerance(CropTool.GripScreenDistance));

            return _boxGrip switch
            {
                TransformGrip.None => CanvasGrab.None,
                TransformGrip.Inside => CanvasGrab.BoxBody,
                _ => CanvasGrab.BoxEdge,
            };
        }

        // The border of text being edited moves the words; inside them a drag selects
        // characters, and there the tool's own I-beam is the right promise.
        if (_observedTextEdit is { } editing
            && editing.IsOnBoxEdge(
                _transform.ToImagePoint(point),
                GetGripTolerance(TextTool.BoxGripScreenDistance)))
        {
            return CanvasGrab.TextEdge;
        }

        // Artwork already lifted is in the artist's hand wherever the pointer is, so the whole
        // canvas is a place to put it down or pick the drag up again.
        if (_observedWorkspace?.ActiveTransform is not null)
        {
            return CanvasGrab.Marks;
        }

        if (ActiveTool is CropTool crop && ToolContext is { } context && _observedOverlay?.CropFrame is not null)
        {
            _cropGrip = crop.HitTest(context, _transform.ToImagePoint(point));
            return _cropGrip == CropGrip.None ? CanvasGrab.None : CanvasGrab.Frame;
        }

        // A crop frame in play owns the canvas; the branch above has already answered for it.
        return _observedOverlay?.CropFrame is null && IsOnSelectionEdge(point)
            ? CanvasGrab.Marks
            : CanvasGrab.None;
    }

    /// <summary>
    /// Gets a value indicating whether a point is close enough to the marching ants to take
    /// hold of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The traced outline, not the bounding box. A selection made with the wand or the pixel
    /// tool is whatever shape the artist marked, and offering a grab handle along a rectangle
    /// they never drew would be a promise about the wrong edge.
    /// </para>
    /// <para>
    /// Holding Shift or Control means the artist is editing the selection rather than moving
    /// it — those are the add and subtract modifiers on every selection tool — so the edge stops
    /// being a handle for as long as one of them is down.
    /// </para>
    /// </remarks>
    private bool IsOnSelectionEdge(Point point)
    {
        if (_observedWorkspace is not { Selection: { IsEmpty: false } selection }
            || _modifiers.HasFlag(KeyModifiers.Shift)
            || _modifiers.HasFlag(KeyModifiers.Control))
        {
            return false;
        }

        _selectionOutline ??= SelectionOutline.Trace(selection);

        foreach (SelectionOutline.Segment segment in _selectionOutline)
        {
            Point start = ToViewPoint(segment.Start, ImagePoint.Origin);
            Point end = ToViewPoint(segment.End, ImagePoint.Origin);

            if (DistanceToSegment(point, start, end) <= SelectionGrabDistance)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The shortest distance from a point to a line segment, in view space.</summary>
    private static double DistanceToSegment(Point point, Point start, Point end)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double lengthSquared = (dx * dx) + (dy * dy);

        // A degenerate segment is a point, and the projection below would divide by zero.
        double t = lengthSquared <= double.Epsilon
            ? 0.0
            : Math.Clamp((((point.X - start.X) * dx) + ((point.Y - start.Y) * dy)) / lengthSquared, 0.0, 1.0);

        double nearestX = start.X + (t * dx);
        double nearestY = start.Y + (t * dy);

        return Math.Sqrt(((point.X - nearestX) * (point.X - nearestX))
            + ((point.Y - nearestY) * (point.Y - nearestY)));
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

        // Shift and Control also turn the selection edge from a handle back into an edge, so
        // the grab has to be asked again whichever way the modifiers moved.
        UpdateGrab(_pointerPosition);
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

        // A transform box owns the canvas while it is up. Every press either takes hold of the
        // box or puts it down, and a tool given one would draw over artwork that is still being
        // arranged.
        if (_observedWorkspace is { ActiveFreeTransform: { } box } transforming)
        {
            TransformGrip grip = box.HitTest(
                _transform.ToImagePoint(position),
                GetGripTolerance(CropTool.GripScreenDistance));

            if (grip == TransformGrip.None)
            {
                // Clicking away puts it down, the way clicking away from anything else does.
                transforming.CommitFreeTransform();
            }
            else
            {
                BeginBoxDrag(box, grip, position);
                e.Pointer.Capture(this);
            }

            e.Handled = true;
            UpdateGrab(position);
            return;
        }

        // Double-clicking words opens them for editing whatever tool is in hand, the way
        // double-clicking type does in every layout program. Without it, editing text you drew
        // an hour ago means first remembering that the text tool exists.
        if (e.ClickCount == 2 && TryBeginTextEditAt(position))
        {
            e.Handled = true;
            UpdateGrab(position);
            return;
        }

        // A free transform owns the canvas while it lasts: the pixels are off the layer and
        // in the artist's hand, and a tool given a press now would draw into the hole.
        if (_observedWorkspace?.ActiveTransform is not null)
        {
            BeginTransformDrag(position);
            e.Pointer.Capture(this);
            e.Handled = true;
            UpdateGrab(position);
            return;
        }

        // A press on the marching ants picks up what they mark, whichever tool is in hand. The
        // cursor has been promising this for as long as the pointer has been on the edge, and
        // the alternative — reaching for the move tool first — puts a trip to the toolbox in
        // the middle of a gesture the artist has already begun. Only the edge does it: the
        // inside of a selection is where the drawing happens, and a pencil that refused to
        // draw there would be a far worse trade.
        if (_observedOverlay?.CropFrame is null
            && IsOnSelectionEdge(position)
            && _observedWorkspace is { } marked
            && marked.BeginTransform())
        {
            BeginTransformDrag(position);
            e.Pointer.Capture(this);
            e.Handled = true;
            UpdateGrab(position);
            return;
        }

        // Clicking the empty space beside the picture puts the selection down, the way clicking
        // away from something deselects it everywhere else. It is the gesture artists reach for
        // first and, until now, the one thing that did not work — leaving the marching ants on
        // screen with no obvious way to be rid of them. Skipped while a crop frame is up, where
        // a press outside the picture is how a new frame is drawn.
        if (_observedOverlay?.CropFrame is null
            && _observedWorkspace is { Selection: { IsEmpty: false } } outside
            && !outside.Document.Bounds.Contains(_transform.ToImagePoint(position)))
        {
            outside.Selection = null;
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

        UpdateGrab(position);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        Point position = e.GetPosition(this);
        UpdateHoveredPixel(position);
        TrackModifiers(e.KeyModifiers);
        UpdateGrab(position);

        if (_isPanning)
        {
            _hasUserAdjustedView = true;
            ApplyTransform(_transform with { Offset = _panStartOffset + (position - _panOrigin) });
            e.Handled = true;
            return;
        }

        if (_isDraggingBox)
        {
            DragBox(position);
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

        // The drag ends, the box does not: the artist is free to grab another handle, turn it,
        // flip it, or press Enter to apply the lot.
        if (_isDraggingBox)
        {
            _isDraggingBox = false;
            _draggingBoxGrip = TransformGrip.None;
            e.Pointer.Capture(null);
            e.Handled = true;
            UpdateGrab(e.GetPosition(this));
            return;
        }

        // The drag ends, the transform does not: the artist is free to pick the artwork up
        // again, nudge it with the arrow keys, or press Enter to put it down.
        if (_isDraggingTransform)
        {
            _isDraggingTransform = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            UpdateGrab(e.GetPosition(this));
            return;
        }

        if (_activeButton != ToolPointerButton.None && ToolContext is { } context && ActiveTool is { } tool)
        {
            tool.OnPointerReleased(context, CreateToolArgs(e.GetPosition(this), _activeButton, e.KeyModifiers));
            e.Handled = true;
        }

        _activeButton = ToolPointerButton.None;
        e.Pointer.Capture(null);

        // The hand opens again here: the tool has just let go of whatever it was holding, and
        // the pointer is very likely still over it.
        UpdateGrab(e.GetPosition(this));
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
        _isDraggingBox = false;
        _draggingBoxGrip = TransformGrip.None;

        UpdateGrab(_pointerPosition);
        UpdateCursor();
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        UpdateGrab(null);

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
        // Switched off, the canvas background shows through instead. Nothing is drawn in its
        // place: a flat fill of some other colour would be a claim that those pixels are that
        // colour, which is the one thing the checkerboard exists to deny.
        if (!ShowTransparencyChecker)
        {
            return;
        }

        _checkerboardBrush ??= CreateCheckerboardBrush();
        context.FillRectangle(_checkerboardBrush, area);
    }

    /// <summary>
    /// Lays the check pattern over the drawn pixels, where the artist has asked for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Masked by the composited artwork itself, so the pattern appears exactly where there is
    /// paint and nowhere else - and fades out with the alpha, so a half-transparent pixel gets
    /// half as much of it. Empty canvas is left to the pattern <em>behind</em> the picture,
    /// which is a different setting and answers a different question.
    /// </para>
    /// <para>
    /// The same tiled brush as the background, so the two patterns line up exactly and read as
    /// one grid: the artwork looks like it is sitting on the board rather than carrying a
    /// board of its own.
    /// </para>
    /// </remarks>
    private void DrawArtworkChecker(DrawingContext context, WriteableBitmap artwork, Rect documentRect)
    {
        if (!ShowArtworkChecker || documentRect.Width <= 0 || documentRect.Height <= 0)
        {
            return;
        }

        _checkerboardBrush ??= CreateCheckerboardBrush();

        ImageBrush mask = new(artwork) { Stretch = Stretch.Fill };

        using (context.PushOpacityMask(mask, documentRect))
        using (context.PushOpacity(ArtworkCheckerOpacity))
        {
            context.FillRectangle(_checkerboardBrush, documentRect);
        }
    }

    /// <summary>
    /// Draws the free transform box: the outline, and the eight handles that resize it.
    /// </summary>
    /// <remarks>
    /// The same outline and handles as the crop frame, deliberately. They are the same gesture
    /// on two different things, and an artist who has learnt one has learnt the other. What is
    /// missing is the shading outside, because a transform changes the artwork rather than the
    /// canvas, and there is nothing out there to be excluded.
    /// </remarks>
    private void DrawTransformBox(DrawingContext context)
    {
        if (_observedWorkspace?.ActiveFreeTransform is not { } box
            || HighlightBrush is not { } bright
            || HighlightShadowBrush is not { } dark)
        {
            return;
        }

        Rect rect = _transform.GetPixelRect(box.Bounds);

        DrawOutlinedRect(context, rect, dark, bright);

        foreach (Point handle in GetCropHandles(rect))
        {
            Rect handleBox = new(
                handle.X - (CropHandleSize / 2.0),
                handle.Y - (CropHandleSize / 2.0),
                CropHandleSize,
                CropHandleSize);

            context.FillRectangle(dark, handleBox.Inflate(1));
            context.FillRectangle(bright, handleBox);
        }
    }

    /// <summary>
    /// Draws the frame around the text being edited, and the highlight behind any selected
    /// characters.
    /// </summary>
    /// <remarks>
    /// The frame is what says "you are in a text field": words on a canvas otherwise look
    /// exactly like words that have been painted there, and there is nothing else on screen to
    /// tell an artist that the next keystroke will be typed rather than treated as a shortcut.
    /// It is also the handle the block is dragged by, which is why it is dashed rather than
    /// hairline - it has to be findable.
    /// </remarks>
    private void DrawTextBox(DrawingContext context)
    {
        if (_observedTextEdit is not { } editing
            || HighlightBrush is not { } bright
            || HighlightShadowBrush is not { } dark)
        {
            return;
        }

        foreach (ImageRect highlight in editing.GetSelectionRectangles())
        {
            context.FillRectangle(TextSelectionFill, _transform.GetPixelRect(highlight));
        }

        Rect box = _transform.GetPixelRect(editing.TextBounds.Inflate(TextEditSession.BoxInset));

        context.DrawRectangle(new Pen(dark, 1.0) { DashStyle = new ImmutableDashStyle([3, 3], 0) }, box.Inflate(0.5));
        context.DrawRectangle(new Pen(bright, 1.0) { DashStyle = new ImmutableDashStyle([3, 3], 3) }, box.Inflate(0.5));
    }

    /// <summary>The part of the image plane the viewport currently covers, in whole pixels.</summary>
    /// <remarks>
    /// Rounded outwards, so the region it describes reaches the window edges rather than
    /// stopping a fraction of a pixel short of them and leaving a hairline of bare background.
    /// </remarks>
    private ImageRect GetViewportBounds()
    {
        ImagePoint topLeft = _transform.ToImagePoint(new Point(0, 0));
        ImagePoint bottomRight = _transform.ToImagePoint(new Point(_viewport.Width, _viewport.Height));

        return ImageRect.FromEdges(topLeft.X, topLeft.Y, bottomRight.X + 1, bottomRight.Y + 1);
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
    /// The area is the document, or the whole window while a crop frame is up. Ruling the grid
    /// across the empty space the frame can be dragged into is what says that the space is made
    /// of pixels and can be kept — without it, dragging a frame outwards looks like dragging it
    /// into nothing. Only the lines that fall inside the viewport are ever emitted, so the
    /// larger area costs nothing.
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
        if (HighlightBrush is not { } bright || HighlightShadowBrush is not { } dark)
        {
            return;
        }

        // A clip in the air is outlined by itself, and whatever was marked before it arrived is
        // not drawn at all. Nothing else would say the pasted pixels are still in the artist's
        // hand, and the old marquee is about to be replaced by this one anyway — showing it in
        // the meantime points at the wrong pixels.
        // A transform of the artwork leaves the marquee where it was until the box is put
        // down, so drawing it would put the ants around the space the artwork has just left.
        // A transform of the marquee itself is the exact opposite: there the ants are the thing
        // being changed, and they are already following the box.
        if (_observedWorkspace?.ActiveFreeTransform is { Scope: FreeTransformScope.Pixels })
        {
            return;
        }

        if (_observedWorkspace?.ActiveTransform is { MovesTheSelection: false } floating)
        {
            DrawMarchingAnts(context, _transform.GetPixelRect(floating.Bounds), dark, bright);
            return;
        }

        if (selection is null or { IsEmpty: true })
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
        _observedWorkspace?.CommitFreeTransform();

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
        _observedWorkspace?.CommitFreeTransform();

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

            case nameof(DocumentWorkspace.ActiveFreeTransform):
                _selectionOutline = null;
                _isDraggingBox = false;
                _draggingBoxGrip = TransformGrip.None;
                UpdateGrab(_pointerPosition);
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
