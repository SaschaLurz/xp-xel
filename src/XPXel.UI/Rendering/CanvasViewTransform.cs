using Avalonia;
using XPXel.Core.Primitives;

namespace XPXel.UI.Rendering;

/// <summary>
/// The mapping between <em>image space</em> (the document's pixel grid) and <em>view
/// space</em> (device-independent pixels on screen), consisting of a uniform zoom and a pan
/// offset.
/// </summary>
/// <remarks>
/// <para>
/// This is the only place in XP-xel that knows about zoom and scroll position. The canvas
/// control converts pointer input through it once, so every tool receives plain image
/// coordinates and never has to think about the view at all.
/// </para>
/// <para>
/// <b>Rounding matters here.</b> Converting a view point to a pixel uses <c>Math.Floor</c>,
/// never truncation: at negative coordinates — which occur whenever the artist drags off the
/// top-left of the canvas — truncation rounds towards zero and would make the pixel at -0.5
/// and the pixel at +0.5 both map to 0, producing a visibly doubled column at the canvas
/// edge.
/// </para>
/// </remarks>
/// <param name="Scale">Screen pixels per image pixel. Always greater than zero.</param>
/// <param name="Offset">
/// Where the document's top-left corner sits in view space.
/// </param>
public readonly record struct CanvasViewTransform(double Scale, Vector Offset)
{
    /// <summary>The smallest zoom the canvas allows.</summary>
    public const double MinimumScale = 0.05;

    /// <summary>The largest zoom the canvas allows.</summary>
    public const double MaximumScale = 64.0;

    /// <summary>
    /// The zoom at and above which individual pixels are large enough for a grid overlay to
    /// help rather than clutter.
    /// </summary>
    public const double GridVisibilityScale = 6.0;

    /// <summary>An identity transform: no zoom, no pan.</summary>
    public static CanvasViewTransform Identity => new(1.0, default);

    /// <summary>The on-screen rectangle a document of the given size occupies.</summary>
    /// <param name="size">The document dimensions.</param>
    /// <returns>The document's bounds in view space.</returns>
    public Rect GetDocumentRect(ImageSize size) =>
        new(Offset.X, Offset.Y, size.Width * Scale, size.Height * Scale);

    /// <summary>Converts a view-space point to the image pixel it falls on.</summary>
    /// <param name="point">The point in view space.</param>
    /// <returns>The image coordinate, which may lie outside the document.</returns>
    public ImagePoint ToImagePoint(Point point) => new(
        (int)Math.Floor((point.X - Offset.X) / Scale),
        (int)Math.Floor((point.Y - Offset.Y) / Scale));

    /// <summary>Converts an image coordinate to the view-space position of its top-left corner.</summary>
    /// <param name="point">The image coordinate.</param>
    /// <returns>The corresponding point in view space.</returns>
    public Point ToViewPoint(ImagePoint point) =>
        new((point.X * Scale) + Offset.X, (point.Y * Scale) + Offset.Y);

    /// <summary>The on-screen rectangle a rectangle of image pixels occupies.</summary>
    /// <param name="rect">The region in image coordinates.</param>
    /// <returns>The region's bounds in view space.</returns>
    /// <remarks>
    /// Used to outline things measured in pixels — the pixel under the pointer, the brush
    /// footprint, the selection — so that every such overlay lines up exactly with the
    /// artwork at any zoom instead of being computed slightly differently each time.
    /// </remarks>
    public Rect GetPixelRect(ImageRect rect) =>
        new(ToViewPoint(rect.Origin), new Size(rect.Width * Scale, rect.Height * Scale));

    /// <summary>Returns a transform panned by the given view-space delta.</summary>
    /// <param name="delta">How far to move the document.</param>
    /// <returns>The panned transform.</returns>
    public CanvasViewTransform Pan(Vector delta) => this with { Offset = Offset + delta };

    /// <summary>
    /// Returns a transform zoomed to a new scale while keeping whatever is under
    /// <paramref name="focus"/> stationary.
    /// </summary>
    /// <param name="scale">The desired scale; clamped to the supported range.</param>
    /// <param name="focus">The view-space point to hold fixed, normally the cursor.</param>
    /// <returns>The zoomed transform.</returns>
    /// <remarks>
    /// Anchoring the zoom to the cursor rather than to the centre of the viewport is what
    /// makes wheel zooming feel like the canvas is being pulled towards the pointer, which is
    /// the behaviour of every editor artists will have used before this one.
    /// </remarks>
    public CanvasViewTransform ZoomTo(double scale, Point focus)
    {
        double clamped = Math.Clamp(scale, MinimumScale, MaximumScale);
        if (Math.Abs(clamped - Scale) < double.Epsilon)
        {
            return this;
        }

        // Work out where the focus point sits in image space, then choose an offset that
        // puts that same image position back under the focus point at the new scale.
        double imageX = (focus.X - Offset.X) / Scale;
        double imageY = (focus.Y - Offset.Y) / Scale;

        return new CanvasViewTransform(
            clamped,
            new Vector(focus.X - (imageX * clamped), focus.Y - (imageY * clamped)));
    }

    /// <summary>
    /// Returns a transform that centres the document in a viewport at the given scale.
    /// </summary>
    /// <param name="viewport">The available area in view space.</param>
    /// <param name="size">The document dimensions.</param>
    /// <param name="scale">The scale to use.</param>
    /// <returns>The centred transform.</returns>
    public static CanvasViewTransform Centered(Size viewport, ImageSize size, double scale)
    {
        double clamped = Math.Clamp(scale, MinimumScale, MaximumScale);

        // Rounding the offset to whole view pixels keeps the document's edges aligned to the
        // display grid, which stops a one-pixel seam appearing along the canvas border.
        double x = Math.Round((viewport.Width - (size.Width * clamped)) / 2.0);
        double y = Math.Round((viewport.Height - (size.Height * clamped)) / 2.0);

        return new CanvasViewTransform(clamped, new Vector(x, y));
    }

    /// <summary>
    /// Returns the largest scale at which the whole document fits inside the viewport, with
    /// a small margin.
    /// </summary>
    /// <param name="viewport">The available area in view space.</param>
    /// <param name="size">The document dimensions.</param>
    /// <returns>The fitting scale.</returns>
    public static double GetFitScale(Size viewport, ImageSize size)
    {
        if (size.IsEmpty || viewport.Width <= 0 || viewport.Height <= 0)
        {
            return 1.0;
        }

        const double margin = 32.0;
        double scaleX = Math.Max(viewport.Width - margin, 1.0) / size.Width;
        double scaleY = Math.Max(viewport.Height - margin, 1.0) / size.Height;
        double fit = Math.Min(scaleX, scaleY);

        // Above 1:1, snap to whole numbers so that a "fit" view still shows crisp,
        // equally sized pixel blocks rather than an uneven mixture.
        if (fit >= 1.0)
        {
            fit = Math.Floor(fit);
        }

        return Math.Clamp(fit, MinimumScale, MaximumScale);
    }

    /// <summary>The slowest zoom the speed setting allows.</summary>
    public const double MinimumZoomSpeed = 0.25;

    /// <summary>The fastest zoom the speed setting allows.</summary>
    public const double MaximumZoomSpeed = 3.0;

    /// <summary>The zoom speed used when the artist has not chosen one.</summary>
    public const double DefaultZoomSpeed = 1.0;

    /// <summary>Returns the next zoom step above or below the current scale.</summary>
    /// <param name="zoomIn"><see langword="true"/> to zoom in, <see langword="false"/> to zoom out.</param>
    /// <param name="speed">
    /// How far one step travels, where 1 is the default. Clamped to
    /// <see cref="MinimumZoomSpeed"/> and <see cref="MaximumZoomSpeed"/>.
    /// </param>
    /// <returns>The next scale.</returns>
    /// <remarks>
    /// <para>
    /// <b>Above 1:1 the sequence is whole numbers, not doublings.</b> Doubling is what most
    /// image editors do and it is far too coarse here — 8× to 16× is the difference between
    /// seeing a sprite and seeing four of its pixels. Whole numbers are the finest sequence
    /// that still draws every document pixel as an identically sized block, which is
    /// non-negotiable in a pixel editor: at 3.5× some pixels would be three screen pixels wide
    /// and others four, and the artwork would appear to have a texture it does not have.
    /// </para>
    /// <para>
    /// The step grows with the scale, so the sequence is fine where the artist is working
    /// pixel by pixel and coarse up at 40×, where one whole number is a rounding error.
    /// </para>
    /// <para>
    /// <b>Below 1:1 there is nothing to keep even</b> — the image is already being sampled
    /// down — so the ramp is geometric, which is what feels uniform when zooming out. Zooming
    /// in through that range stops at exactly 1:1 rather than stepping over it, because 1:1 is
    /// somewhere artists go deliberately.
    /// </para>
    /// </remarks>
    public double GetSteppedScale(bool zoomIn, double speed = DefaultZoomSpeed)
    {
        double factor = Math.Clamp(speed, MinimumZoomSpeed, MaximumZoomSpeed);

        if (Scale >= 1.0)
        {
            int current = (int)Math.Round(Scale);
            int step = Math.Max(1, (int)Math.Round(current * 0.2 * factor));
            int next = zoomIn ? current + step : current - step;

            if (next >= 1)
            {
                return Math.Clamp(next, MinimumScale, MaximumScale);
            }

            // Zooming out below 1:1 continues in the geometric range below.
        }

        double octave = Math.Pow(2.0, 0.35 * factor);
        double stepped = zoomIn ? Scale * octave : Scale / octave;

        if (zoomIn && stepped > 1.0)
        {
            return 1.0;
        }

        return Math.Clamp(stepped, MinimumScale, MaximumScale);
    }
}
