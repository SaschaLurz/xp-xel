using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using XPXel.Core.Primitives;

namespace XPXel.UI.Media;

/// <summary>
/// Builds and caches the mouse cursors the canvas uses, drawing them from the same geometry
/// as the toolbox icons.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an icon cursor is drawn twice.</b> A dark cursor disappears over dark artwork and a
/// light one disappears over light artwork, and a pixel editor's canvas is by definition
/// covered in both. Every icon cursor is therefore stroked first with a thick light pen and
/// then with a thin dark one, so it always carries its own contrast — the same trick the
/// system arrow uses.
/// </para>
/// <para>
/// <b>Why the brush does not get a bitmap cursor.</b> A brush cursor should be the outline of
/// the brush at the size it will actually paint, which changes with both the brush size and
/// the zoom. Baking that into a cursor bitmap would mean rebuilding it on every zoom step and
/// would break down entirely past the roughly 128-pixel limit the platforms impose. The canvas
/// draws the footprint itself instead, in document space where it is exact at any
/// magnification, and the pointer stays a crosshair underneath it.
/// </para>
/// <para>
/// <b>Why the colour dot is in the cursor rather than on the canvas.</b> Anything the canvas
/// paints can only move when the canvas repaints, so a marker drawn beside the pointer steps
/// from pixel to pixel a frame behind it — which is exactly as unsettled as it sounds. Baked
/// into the cursor bitmap the dot is moved by the window system along with the crosshair it is
/// attached to, and is simply part of the pointer.
/// </para>
/// <para>
/// Cursors are cached because a pointer-move handler may ask for one many times a second, and
/// each construction is a bitmap render plus a platform resource. The coloured ones are the
/// exception and are owned by their caller; see
/// <see cref="CreateColorDotCrosshair(PixelColor)"/>.
/// </para>
/// </remarks>
public static class ToolCursors
{
    /// <summary>The side length of a rendered cursor bitmap, in pixels.</summary>
    /// <remarks>
    /// Matches the icons' own 24×24 grid. Larger would be clipped or scaled down by the
    /// window system on some platforms, and the extra detail is not visible at cursor size.
    /// </remarks>
    private const int Size = 24;

    /// <summary>The middle of a cursor bitmap, which is where a crosshair's hotspot goes.</summary>
    private const int Center = 12;

    /// <summary>
    /// The side of the bitmap used for the crosshair that carries a colour dot.
    /// </summary>
    /// <remarks>
    /// Larger than the icon cursors because the dot is held well away from the hotspot, and
    /// the whole thing — crosshair, gap and dot — has to fit inside one bitmap.
    /// </remarks>
    private const int DottedSize = 40;

    /// <summary>Where the hotspot sits in that larger bitmap.</summary>
    private const int DottedCenter = 14;

    private static readonly Dictionary<string, Cursor> IconCursors = new(StringComparer.Ordinal);

    private static Cursor? _crosshair;
    private static Cursor? _text;

    /// <summary>Returns the cursor a tool should use.</summary>
    /// <param name="presentation">How the tool is presented.</param>
    /// <returns>A cached cursor.</returns>
    public static Cursor For(ToolPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        return presentation.Cursor switch
        {
            ToolCursorKind.Text => _text ??= new Cursor(StandardCursorType.Ibeam),
            ToolCursorKind.Icon => GetIconCursor(presentation),
            _ => Crosshair,
        };
    }

    /// <summary>
    /// The thin crosshair used by every tool that places a pixel exactly.
    /// </summary>
    public static Cursor Crosshair => _crosshair ??= new Cursor(StandardCursorType.Cross);

    /// <summary>
    /// Builds a crosshair carrying a round dot of the colour the tool is about to lay down.
    /// </summary>
    /// <param name="color">The colour to show.</param>
    /// <returns>
    /// A cursor the caller owns and must dispose, or <see langword="null"/> if this platform
    /// will not make a bitmap cursor — some browser configurations will not.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The crosshair is drawn here rather than borrowed from the system, because a system
    /// cursor is a single opaque resource with nothing to add to. It is deliberately the same
    /// shape: a thin cross with a gap at the middle, so the pixel being aimed at is never
    /// covered by the thing pointing at it. The dot sits down and to the right, where a
    /// pointer's own shadow falls and so the corner least likely to hold the artwork the
    /// artist is looking at.
    /// </para>
    /// <para>
    /// Unlike every other cursor here this one is not cached, and the caller owns it. The
    /// colour changes with the artist rather than with the frame, so keeping one live cursor
    /// and rebuilding it on a change costs a bitmap render per colour pick; caching would
    /// instead hold a platform cursor resource for every colour ever picked.
    /// </para>
    /// </remarks>
    public static Cursor? CreateColorDotCrosshair(PixelColor color)
    {
        try
        {
            using RenderTargetBitmap bitmap = new(new PixelSize(DottedSize, DottedSize), new Vector(96, 96));

            using (DrawingContext context = bitmap.CreateDrawingContext())
            {
                DrawCrosshair(context);
                DrawColorDot(context, color);
            }

            return new Cursor(bitmap, new PixelPoint(DottedCenter, DottedCenter));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>Draws the cross itself, haloed the same way the icon cursors are.</summary>
    private static void DrawCrosshair(DrawingContext context)
    {
        // Half a pixel off the hotspot, so a one-pixel line covers one row of pixels
        // completely rather than two of them faintly.
        const double middle = DottedCenter - 0.5;
        const double gap = 2.5;
        const double arm = 9.0;

        StreamGeometry geometry = new();

        using (StreamGeometryContext path = geometry.Open())
        {
            AddArm(path, new Point(middle - arm, middle), new Point(middle - gap, middle));
            AddArm(path, new Point(middle + gap, middle), new Point(middle + arm, middle));
            AddArm(path, new Point(middle, middle - arm), new Point(middle, middle - gap));
            AddArm(path, new Point(middle, middle + gap), new Point(middle, middle + arm));
        }

        context.DrawGeometry(null, new Pen(Brushes.White, 3.0), geometry);
        context.DrawGeometry(null, new Pen(Brushes.Black, 1.0), geometry);

        static void AddArm(StreamGeometryContext path, Point from, Point to)
        {
            path.BeginFigure(from, isFilled: false);
            path.LineTo(to);
            path.EndFigure(isClosed: false);
        }
    }

    /// <summary>Draws the colour itself, as a bare round dot up and to the right of the cross.</summary>
    /// <remarks>
    /// <para>
    /// Up and to the right, and far enough out that it is clear of the crosshair's arms
    /// entirely. Held close it competes with the pixel being aimed at; held out here it reads
    /// as a label attached to the pointer, which is what it is.
    /// </para>
    /// <para>
    /// <b>No outline, by request.</b> The trade is real and worth stating: a bare dot is the
    /// colour and nothing else, which is exactly what makes it unmistakable — and which also
    /// means it disappears against artwork of its own colour. The white backing is kept, so a
    /// partly transparent colour still reads as a pale version of itself rather than as
    /// whatever lies behind the pointer.
    /// </para>
    /// </remarks>
    private static void DrawColorDot(DrawingContext context, PixelColor color)
    {
        Point middle = new(DottedCenter + 13.0, DottedCenter - 11.0);
        const double radius = 2.75;

        context.DrawEllipse(Brushes.White, null, middle, radius, radius);
        context.DrawEllipse(new SolidColorBrush(color.ToAvalonia()), null, middle, radius, radius);
    }

    private static Cursor GetIconCursor(ToolPresentation presentation)
    {
        if (IconCursors.TryGetValue(presentation.Icon, out Cursor? cached))
        {
            return cached;
        }

        Cursor cursor;

        try
        {
            using RenderTargetBitmap bitmap = RenderIcon(presentation.Icon);

            cursor = new Cursor(
                bitmap,
                new PixelPoint(
                    (int)Math.Round(presentation.CursorHotspotX),
                    (int)Math.Round(presentation.CursorHotspotY)));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A platform that cannot make a bitmap cursor — some browser configurations —
            // still gets a usable pointer rather than an exception mid-pointer-move.
            cursor = Crosshair;
        }

        IconCursors[presentation.Icon] = cursor;
        return cursor;
    }

    private static RenderTargetBitmap RenderIcon(string pathData)
    {
        Geometry geometry = ToolIcons.Get(pathData);

        RenderTargetBitmap bitmap = new(new PixelSize(Size, Size), new Vector(96, 96));

        using (DrawingContext context = bitmap.CreateDrawingContext())
        {
            // The light pen goes down first and is wider, so what remains of it after the
            // dark pen is drawn on top is a halo the width of the difference.
            context.DrawGeometry(null, new Pen(Brushes.White, 3.5) { LineJoin = PenLineJoin.Round }, geometry);
            context.DrawGeometry(null, new Pen(Brushes.Black, 1.5) { LineJoin = PenLineJoin.Round }, geometry);
        }

        return bitmap;
    }
}
