using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using XPXel.Core.Primitives;
using XPXel.UI.Rendering;

namespace XPXel.UI.Controls;

/// <summary>
/// A ruler drawn along the top or the left edge of the canvas, numbering the document's
/// pixels.
/// </summary>
/// <remarks>
/// <para>
/// <b>Numbering starts at 1.</b> The document, every tool and every file format count pixels
/// from zero, and they must keep doing so. But a ruler is read the way a tape measure is
/// read: the first pixel is the first pixel. The conversion happens here and in the status
/// bar's coordinate readout — the two places an artist reads a number — and nowhere else, so
/// no code that computes with coordinates ever sees the offset.
/// </para>
/// <para>
/// <b>Why a separate control rather than part of the canvas.</b> The canvas is a viewport
/// with its own pan and zoom; the rulers must sit outside it so that they are never scrolled
/// away, yet share its transform exactly. Binding this control to
/// <see cref="PixelCanvasControl.ViewTransform"/> is what keeps the two in step: the ruler
/// does no arithmetic of its own about where the document is.
/// </para>
/// <para>
/// The label interval is chosen from the zoom so the numbers never collide: at 1:1 a 32-wide
/// sprite is labelled every 10 pixels, and zoomed to 16× every pixel gets a number.
/// </para>
/// </remarks>
public sealed class CanvasRulerControl : Control
{
    /// <summary>Whether this ruler runs along the top or down the left edge.</summary>
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<CanvasRulerControl, Orientation>(
            nameof(Orientation), Orientation.Horizontal);

    /// <summary>The canvas's zoom and pan, which the ruler must match exactly.</summary>
    public static readonly StyledProperty<CanvasViewTransform> ViewTransformProperty =
        AvaloniaProperty.Register<CanvasRulerControl, CanvasViewTransform>(
            nameof(ViewTransform), CanvasViewTransform.Identity);

    /// <summary>The document's dimensions, which bound the numbered range.</summary>
    public static readonly StyledProperty<ImageSize> DocumentSizeProperty =
        AvaloniaProperty.Register<CanvasRulerControl, ImageSize>(nameof(DocumentSize));

    /// <summary>The pixel under the pointer, whose row or column the ruler highlights.</summary>
    public static readonly StyledProperty<ImagePoint?> HoveredPixelProperty =
        AvaloniaProperty.Register<CanvasRulerControl, ImagePoint?>(nameof(HoveredPixel));

    /// <summary>The strip's own background.</summary>
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<CanvasRulerControl, IBrush?>(nameof(Background));

    /// <summary>The colour of the ticks and the numbers.</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<CanvasRulerControl, IBrush?>(nameof(Foreground));

    /// <summary>The line separating the ruler from the canvas.</summary>
    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        AvaloniaProperty.Register<CanvasRulerControl, IBrush?>(nameof(BorderBrush));

    /// <summary>The band marking the row or column under the pointer.</summary>
    public static readonly StyledProperty<IBrush?> HighlightBrushProperty =
        AvaloniaProperty.Register<CanvasRulerControl, IBrush?>(nameof(HighlightBrush));

    /// <summary>How thick the strip is, across the axis it measures.</summary>
    public const double Thickness = 20.0;

    /// <summary>The size of the numbers. Small: a ruler is read in glances, not studied.</summary>
    private const double LabelFontSize = 9.5;

    /// <summary>
    /// The label intervals the ruler will choose between, in pixels.
    /// </summary>
    /// <remarks>
    /// Ones, twos and fives and their multiples of ten — the intervals every ruler ever made
    /// uses, because they are the ones people can do arithmetic in without thinking.
    /// </remarks>
    private static readonly int[] Steps = [1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000];

    static CanvasRulerControl() =>
        AffectsRender<CanvasRulerControl>(
            OrientationProperty,
            ViewTransformProperty,
            DocumentSizeProperty,
            HoveredPixelProperty,
            BackgroundProperty,
            ForegroundProperty,
            BorderBrushProperty,
            HighlightBrushProperty);

    /// <summary>Whether this ruler runs along the top or down the left edge.</summary>
    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>The canvas's zoom and pan.</summary>
    public CanvasViewTransform ViewTransform
    {
        get => GetValue(ViewTransformProperty);
        set => SetValue(ViewTransformProperty, value);
    }

    /// <summary>The document's dimensions.</summary>
    public ImageSize DocumentSize
    {
        get => GetValue(DocumentSizeProperty);
        set => SetValue(DocumentSizeProperty, value);
    }

    /// <summary>The pixel under the pointer.</summary>
    public ImagePoint? HoveredPixel
    {
        get => GetValue(HoveredPixelProperty);
        set => SetValue(HoveredPixelProperty, value);
    }

    /// <summary>The strip's own background.</summary>
    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>The colour of the ticks and the numbers.</summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>The line separating the ruler from the canvas.</summary>
    public IBrush? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>The band marking the row or column under the pointer.</summary>
    public IBrush? HighlightBrush
    {
        get => GetValue(HighlightBrushProperty);
        set => SetValue(HighlightBrushProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Rect bounds = new(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        if (Background is { } background)
        {
            context.FillRectangle(background, bounds);
        }

        bool horizontal = Orientation == Orientation.Horizontal;

        DrawInnerEdge(context, bounds, horizontal);

        CanvasViewTransform transform = ViewTransform;
        ImageSize size = DocumentSize;

        int count = horizontal ? size.Width : size.Height;
        if (transform.Scale <= 0 || count <= 0)
        {
            return;
        }

        double origin = horizontal ? transform.Offset.X : transform.Offset.Y;
        double extent = horizontal ? bounds.Width : bounds.Height;
        double scale = transform.Scale;

        DrawHoverBand(context, bounds, horizontal, origin, scale, count);
        DrawDocumentEdges(context, bounds, horizontal, origin, scale, count);

        // Only what is actually on screen is drawn. At the minimum zoom a large document
        // spans tens of thousands of pixels, and iterating all of them every frame would cost
        // far more than the handful of ticks that end up visible.
        int first = Math.Max(0, (int)Math.Floor(-origin / scale));
        int last = Math.Min(count - 1, (int)Math.Ceiling((extent - origin) / scale));
        if (last < first)
        {
            return;
        }

        int step = ChooseStep(scale, horizontal ? 40.0 : 30.0);

        // The first pixel is always labelled — a ruler whose first number is 10 leaves the
        // artist counting backwards to find the origin.
        if (first == 0)
        {
            DrawTick(context, bounds, horizontal, origin, scale, 0);
        }

        // Then every round number: with a step of five that is 5, 10, 15 — the labels read
        // 1, 5, 10 rather than 1, 6, 11, because the label is the pixel's one-based number.
        int firstMultiple = Math.Max(1, (int)Math.Ceiling((first + 1.0) / step));

        for (int index = (firstMultiple * step) - 1; index <= last; index += step)
        {
            DrawTick(context, bounds, horizontal, origin, scale, index);
        }
    }

    /// <summary>Draws the hairline that separates the ruler from the canvas.</summary>
    private void DrawInnerEdge(DrawingContext context, Rect bounds, bool horizontal)
    {
        if (BorderBrush is not { } border)
        {
            return;
        }

        Pen pen = new(border, 1.0);

        // Half-pixel offsets: a one-pixel line drawn on an integer coordinate straddles two
        // device pixels and renders as a two-pixel blur.
        (Point start, Point end) = horizontal
            ? (new Point(0, bounds.Height - 0.5), new Point(bounds.Width, bounds.Height - 0.5))
            : (new Point(bounds.Width - 0.5, 0), new Point(bounds.Width - 0.5, bounds.Height));

        context.DrawLine(pen, start, end);
    }

    /// <summary>Marks the row or column the pointer is currently over.</summary>
    private void DrawHoverBand(
        DrawingContext context,
        Rect bounds,
        bool horizontal,
        double origin,
        double scale,
        int count)
    {
        if (HighlightBrush is not { } highlight || HoveredPixel is not { } hovered)
        {
            return;
        }

        int index = horizontal ? hovered.X : hovered.Y;
        if (index < 0 || index >= count)
        {
            return;
        }

        double start = origin + (index * scale);

        // At low zoom a pixel is a fraction of a screen pixel wide, so the band is widened to
        // stay visible; the point of it is to say roughly where you are, not to be exact.
        double thickness = Math.Max(scale, 2.0);

        context.FillRectangle(
            highlight,
            horizontal
                ? new Rect(start, 0, thickness, bounds.Height)
                : new Rect(0, start, bounds.Width, thickness));
    }

    /// <summary>Marks where the artwork begins and ends inside the ruler.</summary>
    private void DrawDocumentEdges(
        DrawingContext context,
        Rect bounds,
        bool horizontal,
        double origin,
        double scale,
        int count)
    {
        if (Foreground is not { } foreground)
        {
            return;
        }

        Pen pen = new(foreground, 1.0);

        foreach (double edge in (ReadOnlySpan<double>)[origin, origin + (count * scale)])
        {
            double position = Math.Round(edge) + 0.5;

            (Point start, Point end) = horizontal
                ? (new Point(position, 0), new Point(position, bounds.Height))
                : (new Point(0, position), new Point(bounds.Width, position));

            context.DrawLine(pen, start, end);
        }
    }

    /// <summary>Draws one tick and the number beside it.</summary>
    private void DrawTick(
        DrawingContext context,
        Rect bounds,
        bool horizontal,
        double origin,
        double scale,
        int index)
    {
        if (Foreground is not { } foreground)
        {
            return;
        }

        double position = Math.Round(origin + (index * scale)) + 0.5;
        Pen pen = new(foreground, 1.0);

        const double tickLength = 5.0;

        (Point start, Point end) = horizontal
            ? (new Point(position, bounds.Height - tickLength), new Point(position, bounds.Height))
            : (new Point(bounds.Width - tickLength, position), new Point(bounds.Width, position));

        context.DrawLine(pen, start, end);

        FormattedText label = new(
            (index + 1).ToString(CultureInfo.CurrentCulture),
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            LabelFontSize,
            foreground);

        // The number sits just past its tick, in the direction the ruler is read, so it
        // labels the pixel that begins there rather than appearing to straddle two.
        Point textOrigin = horizontal
            ? new Point(position + 2, 1)
            : new Point(1, position + 1);

        // A label that would spill out of the strip is dropped rather than clipped: half a
        // number is worse than no number.
        if (horizontal ? textOrigin.X + label.Width > bounds.Width : textOrigin.Y + label.Height > bounds.Height)
        {
            return;
        }

        context.DrawText(label, textOrigin);
    }

    /// <summary>Picks the smallest round interval whose labels will not collide.</summary>
    /// <param name="scale">Screen pixels per image pixel.</param>
    /// <param name="minimumSpacing">The closest two labels may be, in screen pixels.</param>
    /// <returns>The interval, in image pixels.</returns>
    private static int ChooseStep(double scale, double minimumSpacing)
    {
        foreach (int step in Steps)
        {
            if (step * scale >= minimumSpacing)
            {
                return step;
            }
        }

        return Steps[^1];
    }
}
