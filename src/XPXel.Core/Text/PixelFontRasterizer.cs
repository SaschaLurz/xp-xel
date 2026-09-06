using XPXel.Core.Imaging;
using XPXel.Core.Primitives;

namespace XPXel.Core.Text;

/// <summary>
/// Draws text with the built-in <see cref="PixelFont"/>, one document pixel at a time.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no font engine here and no anti-aliasing anywhere.</b> A glyph is a rectangle
/// of set and unset pixels, so rendering is a copy, scaling is whole-pixel replication, and
/// every pixel written is either the artist's colour at full alpha or nothing at all. That is
/// the entire reason this exists: the result cannot be blurry, cannot be half-transparent, and
/// cannot differ between a Windows machine, a browser and a unit test.
/// </para>
/// <para>
/// <b>Size means whole multiples.</b> The requested point size is rounded to the nearest whole
/// multiple of the font's cell height, because a bitmap font at 1.5× would have to drop or
/// double single rows of pixels and would come out visibly lopsided. See
/// <see cref="GetScale(double)"/>.
/// </para>
/// <para>
/// <b>Emphasis is done the way a bitmap font does it.</b> Bold is the glyph drawn again one
/// pixel to the right, italic is a shear of one pixel every two rows, and underline and
/// strikethrough are rules drawn across the advance width. None of that needs a second font
/// file, and all of it stays on the pixel grid.
/// </para>
/// </remarks>
public sealed class PixelFontRasterizer : ITextRasterizer
{
    /// <summary>The shared instance. Holds no state between calls.</summary>
    public static PixelFontRasterizer Instance { get; } = new();

    private readonly PixelFont _font = PixelFont.Default;

    /// <inheritdoc />
    public IReadOnlyList<string> FontFamilies => PixelFont.Families;

    /// <summary>
    /// Returns the whole-pixel scale a requested size corresponds to.
    /// </summary>
    /// <param name="fontSize">The size asked for, in pixels of cap-to-descender height.</param>
    /// <returns>A scale of at least 1.</returns>
    public static int GetScale(double fontSize) =>
        (int)Math.Max(1.0, Math.Round(fontSize / PixelFont.CellHeight, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Returns the sizes at which the built-in font renders exactly, for a size chooser.
    /// </summary>
    /// <param name="maximumScale">The largest whole multiple to offer.</param>
    /// <returns>The exact sizes, smallest first.</returns>
    /// <remarks>
    /// Offering these rather than an arbitrary number is what stops an artist choosing 13 and
    /// getting 16 without being told why.
    /// </remarks>
    public static IReadOnlyList<double> GetExactSizes(int maximumScale = 8)
    {
        List<double> sizes = [];

        for (int scale = 1; scale <= Math.Max(1, maximumScale); scale++)
        {
            sizes.Add(scale * (double)PixelFont.CellHeight);
        }

        return sizes;
    }

    /// <inheritdoc />
    public TextRasterResult? Rasterize(string text, TextStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        Layout layout = Layout.Measure(text, style);

        if (layout.Width <= 0 || layout.Height <= 0)
        {
            return null;
        }

        PixelBuffer pixels = new(layout.Width, layout.Height);
        bool any = false;

        for (int line = 0; line < layout.Lines.Count; line++)
        {
            any |= DrawLine(pixels, layout, line, style);
        }

        return any ? new TextRasterResult(pixels, new ImagePoint(0, 0)) : null;
    }

    /// <inheritdoc />
    public ImageRect MeasureCaret(string text, TextStyle style, int caretIndex)
    {
        ArgumentNullException.ThrowIfNull(style);

        text ??= string.Empty;
        Layout layout = Layout.Measure(text, style);

        (int line, int column) = layout.Locate(Math.Clamp(caretIndex, 0, text.Length));
        TextLine current = layout.Lines[line];

        return new ImageRect(
            current.Left + (column * layout.Advance),
            line * layout.LineHeight,
            Math.Max(1, layout.Scale),
            PixelFont.CellHeight * layout.Scale);
    }

    /// <inheritdoc />
    public int HitTestCaret(string text, TextStyle style, ImagePoint point)
    {
        ArgumentNullException.ThrowIfNull(style);

        text ??= string.Empty;
        if (text.Length == 0)
        {
            return 0;
        }

        Layout layout = Layout.Measure(text, style);

        int line = Math.Clamp(point.Y / Math.Max(1, layout.LineHeight), 0, layout.Lines.Count - 1);
        TextLine current = layout.Lines[line];

        // Rounded rather than truncated, so clicking the right-hand half of a character puts
        // the caret after it — which is what puts the caret at the end of a word.
        int column = (int)Math.Round(
            (point.X - current.Left) / (double)Math.Max(1, layout.Advance),
            MidpointRounding.AwayFromZero);

        return current.Start + Math.Clamp(column, 0, current.Text.Length);
    }

    /// <summary>Draws one line of text, and reports whether it put anything down.</summary>
    private bool DrawLine(PixelBuffer pixels, Layout layout, int lineIndex, TextStyle style)
    {
        TextLine line = layout.Lines[lineIndex];
        PixelColor color = style.Color;

        if (color.A == 0 || line.Text.Length == 0)
        {
            return DrawRules(pixels, layout, lineIndex, style) && color.A != 0;
        }

        int scale = layout.Scale;
        int top = lineIndex * layout.LineHeight;
        bool any = false;

        for (int index = 0; index < line.Text.Length; index++)
        {
            char glyph = line.Text[index];
            int penX = line.Left + (index * layout.Advance);

            for (int row = 0; row < PixelFont.CellHeight; row++)
            {
                // Italic leans the top of the glyph to the right: one pixel every two rows,
                // measured from the baseline so the foot of the letter stays where it was.
                int slant = style.IsItalic ? (PixelFont.CellHeight - 2 - row) / 2 : 0;

                for (int column = 0; column < PixelFont.CellWidth; column++)
                {
                    if (!_font.IsPixelSet(glyph, column, row))
                    {
                        continue;
                    }

                    int x = penX + ((column + slant) * scale);
                    int y = top + (row * scale);

                    any |= FillBlock(pixels, x, y, scale, color);

                    // Bold is the same glyph again, one pixel to the right. Anything cleverer
                    // would need a second set of glyphs.
                    if (style.IsBold)
                    {
                        any |= FillBlock(pixels, x + scale, y, scale, color);
                    }
                }
            }
        }

        any |= DrawRules(pixels, layout, lineIndex, style);
        return any;
    }

    /// <summary>Draws the underline and strikethrough rules across one line.</summary>
    private static bool DrawRules(PixelBuffer pixels, Layout layout, int lineIndex, TextStyle style)
    {
        if (!style.IsUnderline && !style.IsStrikethrough)
        {
            return false;
        }

        TextLine line = layout.Lines[lineIndex];

        if (line.Text.Length == 0)
        {
            return false;
        }

        int scale = layout.Scale;
        int top = lineIndex * layout.LineHeight;
        int width = line.Text.Length * layout.Advance;
        bool any = false;

        if (style.IsUnderline)
        {
            any |= FillRow(pixels, line.Left, top + (7 * scale), width, scale, style.Color);
        }

        if (style.IsStrikethrough)
        {
            any |= FillRow(pixels, line.Left, top + (4 * scale), width, scale, style.Color);
        }

        return any;
    }

    /// <summary>Writes one scaled pixel: a solid square of side <paramref name="scale"/>.</summary>
    private static bool FillBlock(PixelBuffer pixels, int x, int y, int scale, PixelColor color)
    {
        bool any = false;

        for (int dy = 0; dy < scale; dy++)
        {
            for (int dx = 0; dx < scale; dx++)
            {
                if (pixels.Size.Contains(x + dx, y + dy))
                {
                    pixels.SetPixel(x + dx, y + dy, color);
                    any = true;
                }
            }
        }

        return any;
    }

    private static bool FillRow(PixelBuffer pixels, int left, int top, int width, int scale, PixelColor color)
    {
        bool any = false;

        for (int x = 0; x < width; x++)
        {
            any |= FillBlock(pixels, left + x, top, scale, color);
        }

        return any;
    }

    /// <summary>One laid-out line: its characters, where it starts in the text, and its indent.</summary>
    /// <param name="Text">The characters on the line, without the line break.</param>
    /// <param name="Start">The index in the whole text at which the line begins.</param>
    /// <param name="Left">The line's left edge in the rendered image, after alignment.</param>
    private sealed record TextLine(string Text, int Start, int Left);

    /// <summary>
    /// Everything about where a piece of text sits, worked out once and used by rendering,
    /// caret measurement and hit testing alike, so the three cannot disagree.
    /// </summary>
    private sealed class Layout
    {
        private Layout(IReadOnlyList<TextLine> lines, int scale, int advance, int lineHeight, int width, int height)
        {
            Lines = lines;
            Scale = scale;
            Advance = advance;
            LineHeight = lineHeight;
            Width = width;
            Height = height;
        }

        public IReadOnlyList<TextLine> Lines { get; }

        public int Scale { get; }

        public int Advance { get; }

        public int LineHeight { get; }

        public int Width { get; }

        public int Height { get; }

        public static Layout Measure(string text, TextStyle style)
        {
            int scale = GetScale(style.FontSize);

            // Letter spacing is in document pixels and may be negative, but characters may
            // never overlap into nonsense, so one pixel of advance is the floor.
            int advance = Math.Max(1, (int)Math.Round((PixelFont.Advance * scale) + style.LetterSpacing));
            int lineHeight = Math.Max(1, (int)Math.Round(PixelFont.LineHeight * scale * style.LineSpacing));

            List<string> raw = SplitLines(text);
            int widest = 0;

            foreach (string line in raw)
            {
                widest = Math.Max(widest, line.Length);
            }

            // The bold pass writes one pixel to the right of the last column, and italic leans
            // the top row further right still; both have to fit inside the buffer.
            int extra = (style.IsBold ? scale : 0)
                + (style.IsItalic ? ((PixelFont.CellHeight - 2) / 2) * scale : 0);

            int width = widest == 0 ? 0 : ((widest - 1) * advance) + (PixelFont.CellWidth * scale) + extra;
            int height = raw.Count * lineHeight;

            List<TextLine> lines = [];
            int start = 0;

            foreach (string line in raw)
            {
                int lineWidth = line.Length == 0
                    ? 0
                    : ((line.Length - 1) * advance) + (PixelFont.CellWidth * scale);

                int left = style.Alignment switch
                {
                    TextAlignment.Center => Math.Max(0, (width - extra - lineWidth) / 2),
                    TextAlignment.Right => Math.Max(0, width - extra - lineWidth),
                    _ => 0,
                };

                lines.Add(new TextLine(line, start, left));
                start += line.Length + 1;
            }

            return new Layout(lines, scale, advance, lineHeight, width, height);
        }

        /// <summary>Finds the line and column a character index falls on.</summary>
        public (int Line, int Column) Locate(int caretIndex)
        {
            for (int i = 0; i < Lines.Count; i++)
            {
                TextLine line = Lines[i];

                if (caretIndex <= line.Start + line.Text.Length)
                {
                    return (i, Math.Max(0, caretIndex - line.Start));
                }
            }

            TextLine last = Lines[^1];
            return (Lines.Count - 1, last.Text.Length);
        }

        /// <summary>
        /// Splits on line breaks, treating a carriage-return-newline pair as one break.
        /// </summary>
        /// <remarks>
        /// The index arithmetic in <see cref="Locate"/> assumes one character per break, which
        /// holds because <c>TextEditSession</c> inserts a bare newline. A pasted "\r\n" is
        /// normalised on the way in for the same reason.
        /// </remarks>
        private static List<string> SplitLines(string text)
        {
            List<string> lines = [];
            int start = 0;

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] is not ('\n' or '\r'))
                {
                    continue;
                }

                lines.Add(text[start..i]);

                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                start = i + 1;
            }

            lines.Add(text[start..]);
            return lines;
        }
    }
}
