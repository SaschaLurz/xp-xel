namespace XPXel.Core.Text;

/// <summary>
/// A bitmap font built into the application, drawn on a fixed grid of pixels.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a pixel editor ships its own font.</b> Every outline font on the machine is designed
/// to be rendered with anti-aliasing and hinted to a size far larger than a sprite. Asked for
/// seven pixels of cap height with the smoothing turned off — which is what pixel art needs —
/// they produce mush: strokes drop out, counters fill in, and two different letters end up the
/// same shape. A font whose glyphs <em>are</em> pixels has no such failure mode, because there
/// is nothing to approximate. At 1× it is exactly what the designer drew, and at any whole
/// multiple it is exactly that again, scaled by whole pixels.
/// </para>
/// <para>
/// <b>It lives in Core, and that is the point.</b> No font file, no font engine, no platform
/// font list — so text renders identically on Windows, Linux, macOS and in the browser, renders
/// in unit tests, and renders on a machine that has none of the fonts a project was authored
/// with. The system fonts are still offered for artists working at sizes where they hold up;
/// this is what the editor falls back to and what it starts with.
/// </para>
/// <para>
/// <b>The grid.</b> Glyphs are <see cref="CellWidth"/>×<see cref="CellHeight"/>, with the
/// baseline under row 6 and row 7 reserved for the descenders of <c>g j p q y</c>. Uppercase
/// occupies rows 0–6, lowercase rows 2–6. One blank column is added between characters, so a
/// character advances <see cref="Advance"/> pixels.
/// </para>
/// </remarks>
public sealed class PixelFont
{
    /// <summary>The width of a glyph cell, in pixels.</summary>
    public const int CellWidth = 5;

    /// <summary>The height of a glyph cell, in pixels, including the descender row.</summary>
    public const int CellHeight = 8;

    /// <summary>How far one character advances the pen, in pixels.</summary>
    public const int Advance = CellWidth + 1;

    /// <summary>The height of one line of text, in pixels, including the gap below it.</summary>
    public const int LineHeight = CellHeight + 1;

    /// <summary>The name of the built-in font, as it appears in the font list.</summary>
    public const string FamilyName = "XP-xel Pixel 5x7";

    /// <summary>The single instance. The glyph table is immutable, so one is enough.</summary>
    public static PixelFont Default { get; } = new();

    /// <summary>The families this class provides, for merging into a platform font list.</summary>
    public static IReadOnlyList<string> Families { get; } = [FamilyName];

    /// <summary>Determines whether a family name refers to the built-in font.</summary>
    /// <param name="fontFamily">The family name to test.</param>
    /// <returns><see langword="true"/> when the built-in font should be used.</returns>
    /// <remarks>
    /// Null and empty count as the built-in font, so that a document written before this
    /// existed, or one whose font name did not survive a round trip, still renders legibly
    /// rather than falling back to something that cannot be read at sprite sizes.
    /// </remarks>
    public static bool IsBuiltIn(string? fontFamily) =>
        string.IsNullOrWhiteSpace(fontFamily)
        || string.Equals(fontFamily, FamilyName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Rows of set pixels per glyph, indexed by character code minus <see cref="FirstChar"/>.
    /// Each entry holds <see cref="CellHeight"/> bytes whose low <see cref="CellWidth"/> bits
    /// are the pixels of one row, most significant bit leftmost.
    /// </summary>
    private readonly byte[][] _glyphs;

    private PixelFont() => _glyphs = BuildGlyphs();

    /// <summary>The first character the font covers.</summary>
    private const char FirstChar = ' ';

    /// <summary>The last character the font covers.</summary>
    private const char LastChar = '~';

    /// <summary>
    /// Returns whether a pixel of a character's glyph is set.
    /// </summary>
    /// <param name="value">The character to draw.</param>
    /// <param name="x">The column within the cell, from 0 to <see cref="CellWidth"/> - 1.</param>
    /// <param name="y">The row within the cell, from 0 to <see cref="CellHeight"/> - 1.</param>
    /// <returns><see langword="true"/> if that pixel is part of the glyph.</returns>
    /// <remarks>
    /// A character the font does not cover is drawn as the replacement box, so that unsupported
    /// text is visibly unsupported rather than silently missing.
    /// </remarks>
    public bool IsPixelSet(char value, int x, int y)
    {
        if ((uint)x >= CellWidth || (uint)y >= CellHeight)
        {
            return false;
        }

        byte[] rows = GetGlyph(value);
        return (rows[y] & (1 << (CellWidth - 1 - x))) != 0;
    }

    private byte[] GetGlyph(char value) =>
        value >= FirstChar && value <= LastChar
            ? _glyphs[value - FirstChar]
            : _glyphs['?' - FirstChar];

    private static byte[][] BuildGlyphs()
    {
        string[] source = CreateGlyphSource();

        // A missing or extra picture would silently shift every glyph after it, turning the
        // whole font into gibberish that looks like a rendering bug rather than a typo.
        if (source.Length != LastChar - FirstChar + 1)
        {
            throw new InvalidOperationException(
                $"The pixel font needs {LastChar - FirstChar + 1} glyphs but has {source.Length}.");
        }

        byte[][] glyphs = new byte[source.Length][];

        for (int i = 0; i < source.Length; i++)
        {
            glyphs[i] = ParseGlyph(source[i]);
        }

        return glyphs;
    }

    /// <summary>Turns one glyph's row strings into its packed bits.</summary>
    private static byte[] ParseGlyph(string definition)
    {
        byte[] rows = new byte[CellHeight];
        int row = 0;
        int column = 0;

        foreach (char c in definition)
        {
            if (c == '/')
            {
                row++;
                column = 0;
                continue;
            }

            if (row < CellHeight && column < CellWidth && c == '#')
            {
                rows[row] |= (byte)(1 << (CellWidth - 1 - column));
            }

            column++;
        }

        return rows;
    }

    /// <summary>
    /// The glyphs, in character order from space to tilde.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written out as pictures rather than as hexadecimal, because a font is the one kind of
    /// data where the source should look like the thing it describes. A wrong pixel is obvious
    /// here and invisible in a table of numbers.
    /// </para>
    /// <para>
    /// A method rather than a static property, and deliberately so: a property initialiser
    /// would run in declaration order alongside <see cref="Default"/>, and whichever of the two
    /// came second would find the other still null.
    /// </para>
    /// </remarks>
    private static string[] CreateGlyphSource() =>
    [
        // Space and punctuation
        "...../...../...../...../...../...../...../.....",                     // (space)
        "..#../..#../..#../..#../..#../...../..#../.....",                     // !
        ".#.#./.#.#./...../...../...../...../...../.....",                     // "
        ".#.#./.#.#./#####/.#.#./#####/.#.#./.#.#./.....",                     // #
        "..#../.####/#.#../.###./..#.#/####./..#../.....",                     // $
        "##..#/##..#/...#./..#../.#.../#..##/#..##/.....",                     // %
        ".##../#..#./#.#../.#.../#.#.#/#..#./.##.#/.....",                     // &
        "..#../..#../...../...../...../...../...../.....",                     // '
        "...#./..#../.#.../.#.../.#.../..#../...#./.....",                     // (
        ".#.../..#../...#./...#./...#./..#../.#.../.....",                     // )
        "...../#.#.#/.###./#####/.###./#.#.#/...../.....",                     // *
        "...../..#../..#../#####/..#../..#../...../.....",                     // +
        "...../...../...../...../...../..##./.#.../.....",                     // ,
        "...../...../...../#####/...../...../...../.....",                     // -
        "...../...../...../...../...../..##./..##./.....",                     // .
        "....#/....#/...#./..#../.#.../#..../#..../.....",                     // /

        // Digits
        ".###./#...#/#..##/#.#.#/##..#/#...#/.###./.....",                     // 0
        "..#../.##../..#../..#../..#../..#../.###./.....",                     // 1
        ".###./#...#/....#/...#./..#../.#.../#####/.....",                     // 2
        "#####/...#./..#../...#./....#/#...#/.###./.....",                     // 3
        "...#./..##./.#.#./#..#./#####/...#./...#./.....",                     // 4
        "#####/#..../####./....#/....#/#...#/.###./.....",                     // 5
        "..##./.#.../#..../####./#...#/#...#/.###./.....",                     // 6
        "#####/....#/...#./..#../.#.../.#.../.#.../.....",                     // 7
        ".###./#...#/#...#/.###./#...#/#...#/.###./.....",                     // 8
        ".###./#...#/#...#/.####/....#/...#./.##../.....",                     // 9

        // More punctuation
        "...../..##./..##./...../..##./..##./...../.....",                     // :
        "...../..##./..##./...../..##./..#../.#.../.....",                     // ;
        "...#./..#../.#.../#..../.#.../..#../...#./.....",                     // <
        "...../...../#####/...../#####/...../...../.....",                     // =
        ".#.../..#../...#./....#/...#./..#../.#.../.....",                     // >
        ".###./#...#/....#/...#./..#../...../..#../.....",                     // ?
        ".###./#...#/#.###/#.#.#/#.###/#..../.###./.....",                     // @

        // Uppercase
        ".###./#...#/#...#/#####/#...#/#...#/#...#/.....",                     // A
        "####./#...#/#...#/####./#...#/#...#/####./.....",                     // B
        ".###./#...#/#..../#..../#..../#...#/.###./.....",                     // C
        "####./#...#/#...#/#...#/#...#/#...#/####./.....",                     // D
        "#####/#..../#..../####./#..../#..../#####/.....",                     // E
        "#####/#..../#..../####./#..../#..../#..../.....",                     // F
        ".###./#...#/#..../#..##/#...#/#...#/.###./.....",                     // G
        "#...#/#...#/#...#/#####/#...#/#...#/#...#/.....",                     // H
        ".###./..#../..#../..#../..#../..#../.###./.....",                     // I
        "..###/...#./...#./...#./...#./#..#./.##../.....",                     // J
        "#...#/#..#./#.#../##.../#.#../#..#./#...#/.....",                     // K
        "#..../#..../#..../#..../#..../#..../#####/.....",                     // L
        "#...#/##.##/#.#.#/#.#.#/#...#/#...#/#...#/.....",                     // M
        "#...#/##..#/#.#.#/#..##/#...#/#...#/#...#/.....",                     // N
        ".###./#...#/#...#/#...#/#...#/#...#/.###./.....",                     // O
        "####./#...#/#...#/####./#..../#..../#..../.....",                     // P
        ".###./#...#/#...#/#...#/#.#.#/#..#./.##.#/.....",                     // Q
        "####./#...#/#...#/####./#.#../#..#./#...#/.....",                     // R
        ".####/#..../#..../.###./....#/....#/####./.....",                     // S
        "#####/..#../..#../..#../..#../..#../..#../.....",                     // T
        "#...#/#...#/#...#/#...#/#...#/#...#/.###./.....",                     // U
        "#...#/#...#/#...#/#...#/#...#/.#.#./..#../.....",                     // V
        "#...#/#...#/#...#/#.#.#/#.#.#/##.##/#...#/.....",                     // W
        "#...#/#...#/.#.#./..#../.#.#./#...#/#...#/.....",                     // X
        "#...#/#...#/.#.#./..#../..#../..#../..#../.....",                     // Y
        "#####/....#/...#./..#../.#.../#..../#####/.....",                     // Z

        "..###/..#../..#../..#../..#../..#../..###/.....",                     // [
        "#..../#..../.#.../..#../...#./....#/....#/.....",                     // backslash
        "###../...#./...#./...#./...#./...#./###../.....",                     // ]
        "..#../.#.#./#...#/...../...../...../...../.....",                     // ^
        "...../...../...../...../...../...../...../#####",                     // _
        ".#.../..#../...../...../...../...../...../.....",                     // `

        // Lowercase
        "...../...../.###./....#/.####/#...#/.####/.....",                     // a
        "#..../#..../####./#...#/#...#/#...#/####./.....",                     // b
        "...../...../.###./#..../#..../#..../.###./.....",                     // c
        "....#/....#/.####/#...#/#...#/#...#/.####/.....",                     // d
        "...../...../.###./#...#/#####/#..../.###./.....",                     // e
        "..##./.#.../####./.#.../.#.../.#.../.#.../.....",                    // f
        "...../...../.####/#...#/#...#/.####/....#/.###.",                     // g
        "#..../#..../####./#...#/#...#/#...#/#...#/.....",                     // h
        "..#../...../.##../..#../..#../..#../.###./.....",                     // i
        "...#./...../..##./...#./...#./...#./#..#./.##..",                     // j
        "#..../#..../#..#./#.#../##.../#.#../#..#./.....",                     // k
        ".##../..#../..#../..#../..#../..#../.###./.....",                     // l
        "...../...../##.#./#.#.#/#.#.#/#...#/#...#/.....",                     // m
        "...../...../####./#...#/#...#/#...#/#...#/.....",                     // n
        "...../...../.###./#...#/#...#/#...#/.###./.....",                     // o
        "...../...../####./#...#/#...#/####./#..../#....",                     // p
        "...../...../.####/#...#/#...#/.####/....#/....#",                     // q
        "...../...../#.##./##..#/#..../#..../#..../.....",                     // r
        "...../...../.####/#..../.###./....#/####./.....",                     // s
        ".#.../.#.../####./.#.../.#.../.#..#/..##./.....",                     // t
        "...../...../#...#/#...#/#...#/#...#/.####/.....",                     // u
        "...../...../#...#/#...#/#...#/.#.#./..#../.....",                     // v
        "...../...../#...#/#...#/#.#.#/#.#.#/.#.#./.....",                     // w
        "...../...../#...#/.#.#./..#../.#.#./#...#/.....",                     // x
        "...../...../#...#/#...#/#...#/.####/....#/.###.",                     // y
        "...../...../#####/...#./..#../.#.../#####/.....",                     // z

        "...##/..#../..#../.#.../..#../..#../...##/.....",                     // {
        "..#../..#../..#../..#../..#../..#../..#../.....",                     // |
        "##.../..#../..#../...#./..#../..#../##.../.....",                     // }
        "...../...../.#..#/#.#.#/#..#./...../...../.....",                     // ~
    ];
}
