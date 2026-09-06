using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using XPXel.Core.Primitives;
using HsvColor = XPXel.Core.Primitives.HsvColor;

namespace XPXel.UI.Controls;

/// <summary>
/// The colour spectrum: a saturation-and-value square beside a hue strip, dragged directly.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a control rather than three sliders.</b> Sliders are exact and are kept alongside
/// this, but choosing a colour is not usually an exact act — it is "a bit more orange, a bit
/// less washed out", which is one gesture here and two guesses on a pair of sliders. This is
/// the layout every image editor uses for the same reason, so it needs no explaining.
/// </para>
/// <para>
/// <b>Hue is the control's own state, not something read back from the colour.</b> Grey and
/// black have no hue — every hue produces the same grey — so a control that recovered hue from
/// its colour would swing the strip to red the moment the artist dragged saturation to zero,
/// and the way back would be gone. The hue the artist chose is therefore remembered even while
/// nothing on screen depends on it.
/// </para>
/// <para>
/// <b>The square is redrawn only when the hue changes.</b> It is a bitmap of a few thousand
/// pixels generated per hue, not per frame: dragging inside it moves the marker and touches
/// nothing else, which is what keeps a drag smooth.
/// </para>
/// </remarks>
public sealed class ColorSpectrumControl : Control, IDisposable
{
    /// <summary>The hue in degrees, from 0 to 360.</summary>
    public static readonly StyledProperty<double> HueProperty =
        AvaloniaProperty.Register<ColorSpectrumControl, double>(nameof(Hue), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>The saturation, from 0 to 1.</summary>
    public static readonly StyledProperty<double> SaturationProperty =
        AvaloniaProperty.Register<ColorSpectrumControl, double>(nameof(Saturation), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>The value, from 0 to 1.</summary>
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ColorSpectrumControl, double>(nameof(Value), defaultValue: 1.0, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>The width of the hue strip, in device-independent pixels.</summary>
    private const double StripWidth = 22.0;

    /// <summary>The gap between the square and the strip.</summary>
    private const double Gap = 8.0;

    /// <summary>The resolution the square is generated at, in each direction.</summary>
    /// <remarks>
    /// Generated small and scaled up: the gradient is smooth, so a larger bitmap carries no
    /// more information and costs a proportionally longer generation on every hue change.
    /// </remarks>
    private const int SquareResolution = 128;

    /// <summary>The resolution of the hue strip, along its length.</summary>
    private const int StripResolution = 256;

    private WriteableBitmap? _square;
    private WriteableBitmap? _strip;
    private double _squareHue = double.NaN;

    private bool _isDraggingSquare;
    private bool _isDraggingStrip;

    static ColorSpectrumControl() =>
        AffectsRender<ColorSpectrumControl>(HueProperty, SaturationProperty, ValueProperty);

    /// <summary>The hue in degrees, from 0 to 360.</summary>
    public double Hue
    {
        get => GetValue(HueProperty);
        set => SetValue(HueProperty, value);
    }

    /// <summary>The saturation, from 0 to 1.</summary>
    public double Saturation
    {
        get => GetValue(SaturationProperty);
        set => SetValue(SaturationProperty, value);
    }

    /// <summary>The value, from 0 to 1.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Rect square = SquareBounds;
        Rect strip = StripBounds;

        if (square.Width <= 0 || square.Height <= 0)
        {
            return;
        }

        EnsureSquare();
        EnsureStrip();

        if (_square is not null)
        {
            context.DrawImage(_square, new Rect(_square.Size), square);
        }

        if (_strip is not null)
        {
            context.DrawImage(_strip, new Rect(_strip.Size), strip);
        }

        DrawSquareMarker(context, square);
        DrawStripMarker(context, strip);
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerPressed(e);

        Point position = e.GetPosition(this);

        if (StripBounds.Contains(position))
        {
            _isDraggingStrip = true;
            ApplyStrip(position);
        }
        else
        {
            // Anything that is not the strip counts as the square, so a drag that starts a
            // pixel outside it still picks the colour the artist was aiming at.
            _isDraggingSquare = true;
            ApplySquare(position);
        }

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerMoved(e);

        if (!_isDraggingSquare && !_isDraggingStrip)
        {
            return;
        }

        Point position = e.GetPosition(this);

        if (_isDraggingStrip)
        {
            ApplyStrip(position);
        }
        else
        {
            ApplySquare(position);
        }

        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerReleased(e);

        _isDraggingSquare = false;
        _isDraggingStrip = false;
        e.Pointer.Capture(null);
    }

    /// <summary>
    /// Releases the generated bitmaps.
    /// </summary>
    /// <remarks>
    /// A control is not normally disposable, but this one owns two platform bitmaps. They are
    /// released when it leaves the visual tree — which for a control living in a flyout is
    /// every time the flyout closes — and regenerated on the way back in, so a picker that is
    /// opened and closed a hundred times holds one square's worth of memory rather than a
    /// hundred.
    /// </remarks>
    public void Dispose()
    {
        _square?.Dispose();
        _square = null;

        _strip?.Dispose();
        _strip = null;

        _squareHue = double.NaN;
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Dispose();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // Square plus strip. The height follows the square, so the control keeps its shape
        // however narrow the panel holding it is dragged.
        double width = double.IsInfinity(availableSize.Width) ? 220 : availableSize.Width;
        double square = Math.Max(60, width - StripWidth - Gap);

        return new Size(width, square);
    }

    private Rect SquareBounds => new(0, 0, Math.Max(0, Bounds.Width - StripWidth - Gap), Bounds.Height);

    private Rect StripBounds => new(Math.Max(0, Bounds.Width - StripWidth), 0, StripWidth, Bounds.Height);

    private void ApplySquare(Point position)
    {
        Rect square = SquareBounds;

        if (square.Width <= 0 || square.Height <= 0)
        {
            return;
        }

        Saturation = Math.Clamp((position.X - square.X) / square.Width, 0.0, 1.0);

        // Value runs bottom to top, which is the way round every editor does it and the way
        // round that puts white at the top where light belongs.
        Value = Math.Clamp(1.0 - ((position.Y - square.Y) / square.Height), 0.0, 1.0);
    }

    private void ApplyStrip(Point position)
    {
        Rect strip = StripBounds;

        if (strip.Height <= 0)
        {
            return;
        }

        Hue = Math.Clamp((position.Y - strip.Y) / strip.Height, 0.0, 1.0) * 360.0;
    }

    /// <summary>Regenerates the saturation-value square, but only when the hue has moved.</summary>
    private void EnsureSquare()
    {
        if (_square is not null && Math.Abs(_squareHue - Hue) < 0.01)
        {
            return;
        }

        _square ??= new WriteableBitmap(
            new PixelSize(SquareResolution, SquareResolution),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        _squareHue = Hue;

        using ILockedFramebuffer buffer = _square.Lock();

        byte[] row = new byte[SquareResolution * 4];

        for (int y = 0; y < SquareResolution; y++)
        {
            double value = 1.0 - (y / (double)(SquareResolution - 1));

            for (int x = 0; x < SquareResolution; x++)
            {
                double saturation = x / (double)(SquareResolution - 1);
                PixelColor color = new HsvColor(Hue, saturation, value).ToColor();

                // Opaque throughout, so premultiplied and straight are the same bytes.
                row[(x * 4) + 0] = color.B;
                row[(x * 4) + 1] = color.G;
                row[(x * 4) + 2] = color.R;
                row[(x * 4) + 3] = 255;
            }

            Marshal.Copy(row, 0, buffer.Address + (y * buffer.RowBytes), row.Length);
        }
    }

    /// <summary>Generates the hue strip once; it never changes.</summary>
    private void EnsureStrip()
    {
        if (_strip is not null)
        {
            return;
        }

        _strip = new WriteableBitmap(
            new PixelSize(1, StripResolution),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using ILockedFramebuffer buffer = _strip.Lock();

        byte[] row = new byte[4];

        for (int y = 0; y < StripResolution; y++)
        {
            PixelColor color = new HsvColor(y / (double)(StripResolution - 1) * 360.0, 1.0, 1.0).ToColor();

            row[0] = color.B;
            row[1] = color.G;
            row[2] = color.R;
            row[3] = 255;

            Marshal.Copy(row, 0, buffer.Address + (y * buffer.RowBytes), row.Length);
        }
    }

    /// <summary>
    /// Draws the ring showing where in the square the colour sits.
    /// </summary>
    /// <remarks>
    /// A ring rather than a dot, and outlined in both black and white, because it has to stay
    /// visible against every colour the square contains — including, at the two extremes,
    /// against pure white and pure black.
    /// </remarks>
    private void DrawSquareMarker(DrawingContext context, Rect square)
    {
        Point at = new(
            square.X + (Saturation * square.Width),
            square.Y + ((1.0 - Value) * square.Height));

        context.DrawEllipse(null, new Pen(Brushes.Black, 3.0), at, 6.0, 6.0);
        context.DrawEllipse(null, new Pen(Brushes.White, 1.6), at, 6.0, 6.0);
    }

    private void DrawStripMarker(DrawingContext context, Rect strip)
    {
        double y = strip.Y + (Math.Clamp(Hue, 0.0, 360.0) / 360.0 * strip.Height);
        Rect marker = new(strip.X - 2, y - 3, strip.Width + 4, 6);

        context.DrawRectangle(null, new Pen(Brushes.Black, 3.0), marker, 2, 2);
        context.DrawRectangle(null, new Pen(Brushes.White, 1.6), marker, 2, 2);
    }
}
