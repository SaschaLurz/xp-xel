using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Text;
using CoreTextAlignment = XPXel.Core.Text.TextAlignment;

namespace XPXel.UI.Services;

/// <summary>
/// Renders text with Avalonia's text stack, which is the font engine every platform head
/// already has.
/// </summary>
/// <remarks>
/// <para>
/// This is the implementation behind <see cref="ITextRasterizer"/>, and the only place in
/// XP-xel that knows what a font is. Keeping it here rather than in <c>XPXel.Core</c> is what
/// lets the document model describe text without depending on a UI framework — and what lets
/// the model be unit-tested with <see cref="NullTextRasterizer"/> instead.
/// </para>
/// <para>
/// <b>Text is drawn white and re-coloured afterwards.</b> Drawing directly in the artist's
/// colour would mean recovering that colour from premultiplied pixels, and the round trip
/// loses precision exactly where the artist can least afford it — a colour picked as
/// <c>#3366CC</c> would come back as <c>#3365CB</c>. Drawing in opaque white instead makes
/// the rendered alpha a pure coverage mask, and the exact colour is applied to it directly.
/// </para>
/// <para>
/// <b>Anti-aliasing is thresholded, not disabled.</b> Avalonia has no portable "render this
/// glyph with no smoothing" switch, so aliased text is produced by rounding coverage to fully
/// on or fully off. The result is what a pixel artist wants — every pixel is either the
/// chosen colour or nothing — and it behaves identically on every platform, which a renderer
/// hint would not.
/// </para>
/// <para>
/// <b>The built-in pixel font does not come through here at all.</b> When the style names it,
/// every call is handed straight to <see cref="PixelFontRasterizer"/>, which draws glyphs as
/// pixels with no font engine involved. That is what makes the default font render identically
/// on every platform, in the browser, and in unit tests with no display attached.
/// </para>
/// <para>
/// <b>Threading.</b> Every method must be called on the UI thread, because
/// <see cref="RenderTargetBitmap"/> is a platform resource. In practice they are: text is
/// rasterised in response to a keystroke, a style change or a file load, all of which arrive
/// there.
/// </para>
/// </remarks>
public sealed class AvaloniaTextRasterizer : ITextRasterizer
{
    /// <summary>
    /// Transparent margin left around the rendered text, in pixels.
    /// </summary>
    /// <remarks>
    /// Italic glyphs lean out past the layout box and some decorative faces overhang it in
    /// every direction. Without a margin those overhangs are clipped, which shows up as the
    /// tail of an italic "f" being sliced off.
    /// </remarks>
    private const int Padding = 4;

    /// <summary>The largest text block that will be rasterised, in pixels per side.</summary>
    /// <remarks>
    /// A guard rather than a limit anyone should reach: font size is clamped to 512 and the
    /// text is a few lines, so this only catches a pathological case before it tries to
    /// allocate a bitmap that would fail anyway.
    /// </remarks>
    private const int MaximumSide = 8192;

    private IReadOnlyList<string>? _fontFamilies;

    /// <summary>The renderer for the fonts built into the application.</summary>
    private static PixelFontRasterizer BuiltIn => PixelFontRasterizer.Instance;

    /// <inheritdoc />
    /// <remarks>
    /// Enumerated once and cached. The installed font list does not change while the
    /// application is running, and asking the platform for it is not cheap enough to do every
    /// time a panel opens.
    /// </remarks>
    public IReadOnlyList<string> FontFamilies => _fontFamilies ??= BuildFontFamilies();

    /// <inheritdoc />
    public TextRasterResult? Rasterize(string text, TextStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        if (PixelFont.IsBuiltIn(style.FontFamily))
        {
            return BuiltIn.Rasterize(text, style);
        }

        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        TextLayout layout = CreateLayout(text, style);

        int width = (int)Math.Ceiling(layout.WidthIncludingTrailingWhitespace) + (Padding * 2);
        int height = (int)Math.Ceiling(layout.Height) + (Padding * 2);

        if (width <= 0 || height <= 0 || width > MaximumSide || height > MaximumSide)
        {
            return null;
        }

        byte[] premultiplied = RenderToBytes(layout, width, height);
        PixelBuffer pixels = new(width, height);

        if (!Recolor(premultiplied, pixels, style))
        {
            // Whitespace, or a font that had nothing to draw for these characters.
            return null;
        }

        return new TextRasterResult(pixels, new ImagePoint(-Padding, -Padding));
    }

    /// <inheritdoc />
    public ImageRect MeasureCaret(string text, TextStyle style, int caretIndex)
    {
        ArgumentNullException.ThrowIfNull(style);

        if (PixelFont.IsBuiltIn(style.FontFamily))
        {
            return BuiltIn.MeasureCaret(text, style, caretIndex);
        }

        text ??= string.Empty;

        TextLayout layout = CreateLayout(text, style);
        Rect caret = layout.HitTestTextPosition(Math.Clamp(caretIndex, 0, text.Length));

        // Empty text has no glyph to measure against, and some fonts report a zero-height
        // caret at the end of a line. Falling back to the laid-out height keeps the caret the
        // size of the line it sits on rather than collapsing it to nothing.
        double height = caret.Height > 0 ? caret.Height : layout.Height;

        return new ImageRect(
            (int)Math.Round(caret.X),
            (int)Math.Round(caret.Y),
            1,
            Math.Max(1, (int)Math.Round(height)));
    }

    /// <inheritdoc />
    public int HitTestCaret(string text, TextStyle style, ImagePoint point)
    {
        ArgumentNullException.ThrowIfNull(style);

        if (PixelFont.IsBuiltIn(style.FontFamily))
        {
            return BuiltIn.HitTestCaret(text, style, point);
        }

        text ??= string.Empty;
        if (text.Length == 0)
        {
            return 0;
        }

        TextLayout layout = CreateLayout(text, style);
        TextHitTestResult hit = layout.HitTestPoint(new Point(point.X, point.Y));

        // A click past the middle of a character means "after it", which is what puts the
        // caret at the end of a word when the artist clicks its right-hand edge.
        int index = hit.TextPosition + (hit.IsTrailing ? 1 : 0);
        return Math.Clamp(index, 0, text.Length);
    }

    private static TextLayout CreateLayout(string text, TextStyle style)
    {
        Typeface typeface = new(
            new FontFamily(style.FontFamily),
            style.IsItalic ? FontStyle.Italic : FontStyle.Normal,
            style.IsBold ? FontWeight.Bold : FontWeight.Normal);

        TextDecorationCollection? decorations = BuildDecorations(style);

        // The line height is the font's natural height scaled by the artist's setting.
        // NaN means "use the font's own", which is what a multiplier of exactly 1 should do.
        double lineHeight = Math.Abs(style.LineSpacing - 1.0) < 0.001
            ? double.NaN
            : style.FontSize * 1.2 * style.LineSpacing;

        return new TextLayout(
            text,
            typeface,
            style.FontSize,
            Brushes.White,
            MapAlignment(style.Alignment),
            TextWrapping.NoWrap,
            textTrimming: null,
            textDecorations: decorations,
            flowDirection: FlowDirection.LeftToRight,
            lineHeight: lineHeight);
    }

    private static TextDecorationCollection? BuildDecorations(TextStyle style)
    {
        if (!style.IsUnderline && !style.IsStrikethrough)
        {
            return null;
        }

        TextDecorationCollection decorations = [];

        if (style.IsUnderline)
        {
            decorations.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
        }

        if (style.IsStrikethrough)
        {
            decorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
        }

        return decorations;
    }

    private static Avalonia.Media.TextAlignment MapAlignment(CoreTextAlignment alignment) => alignment switch
    {
        CoreTextAlignment.Center => Avalonia.Media.TextAlignment.Center,
        CoreTextAlignment.Right => Avalonia.Media.TextAlignment.Right,
        _ => Avalonia.Media.TextAlignment.Left,
    };

    private static byte[] RenderToBytes(TextLayout layout, int width, int height)
    {
        // 96 DPI, deliberately: the bitmap is measured in image pixels, not screen pixels, so
        // a high-density display must not make the same text render larger.
        using RenderTargetBitmap target = new(new PixelSize(width, height), new Vector(96, 96));

        using (DrawingContext context = target.CreateDrawingContext())
        {
            layout.Draw(context, new Point(Padding, Padding));
        }

        byte[] buffer = new byte[width * height * 4];
        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

        try
        {
            target.CopyPixels(
                new PixelRect(0, 0, width, height),
                handle.AddrOfPinnedObject(),
                buffer.Length,
                width * 4);
        }
        finally
        {
            handle.Free();
        }

        return buffer;
    }

    /// <summary>
    /// Turns the white-on-transparent render into the artist's colour, and returns whether
    /// anything was actually drawn.
    /// </summary>
    private static bool Recolor(byte[] premultiplied, PixelBuffer target, TextStyle style)
    {
        PixelColor color = style.Color;
        bool antiAliased = style.IsAntiAliased;
        bool any = false;

        Span<byte> destination = target.AsSpan();

        for (int i = 0; i < destination.Length; i += 4)
        {
            // The render target is premultiplied BGRA, and the text was drawn in opaque
            // white, so the alpha channel is exactly the glyph coverage.
            byte coverage = premultiplied[i + 3];

            if (coverage == 0)
            {
                continue;
            }

            byte alpha = antiAliased
                ? (byte)((coverage * color.A) / 255)
                : coverage >= 128 ? color.A : (byte)0;

            if (alpha == 0)
            {
                continue;
            }

            destination[i] = color.R;
            destination[i + 1] = color.G;
            destination[i + 2] = color.B;
            destination[i + 3] = alpha;
            any = true;
        }

        return any;
    }

    private static List<string> BuildFontFamilies()
    {
        List<string> names = [];

        try
        {
            foreach (FontFamily family in FontManager.Current.SystemFonts)
            {
                names.Add(family.Name);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A platform that cannot enumerate its fonts — a locked-down browser, most
            // likely — still gets the generic families below, which always resolve.
        }

        names.Sort(StringComparer.CurrentCultureIgnoreCase);

        // The built-in pixel font goes first because it is the only one in the list that is
        // guaranteed to be legible at sprite sizes, and it is what new text starts with. The
        // generic families follow, being the ones certain to resolve on every platform, and the
        // machine's own faces come after them.
        List<string> result = [.. PixelFont.Families, "Monospace", "Sans Serif", "Serif"];
        result.AddRange(names.Where(n => !result.Contains(n, StringComparer.OrdinalIgnoreCase)));

        return result;
    }

}
