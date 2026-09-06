using XPXel.Core.ComponentModel;
using XPXel.Core.Primitives;

namespace XPXel.Core.Text;

/// <summary>How the lines of a text block line up with one another.</summary>
public enum TextAlignment
{
    /// <summary>Every line starts at the block's left edge.</summary>
    Left = 0,

    /// <summary>Every line is centred within the block.</summary>
    Center = 1,

    /// <summary>Every line ends at the block's right edge.</summary>
    Right = 2,
}

/// <summary>
/// The typographic settings of a piece of text: which font, how big, and how it is decorated
/// and laid out.
/// </summary>
/// <remarks>
/// <para>
/// <b>This lives in Core even though Core cannot render a glyph.</b> The style is document
/// data — it is saved into a <c>.xpxel</c> file and has to survive a reload — whereas turning
/// it into pixels needs a font engine, which is a platform concern. The split is the same one
/// the rest of the codebase makes: the model describes, an interface
/// (<see cref="ITextRasterizer"/>) renders, and the UI supplies the implementation.
/// </para>
/// <para>
/// <b><see cref="IsAntiAliased"/> defaults to off.</b> Anti-aliased text on a 32×32 sprite is
/// a grey smear rather than letters, and half-transparent pixels are exactly what a pixel
/// artist does not want to have to clean up. Artists working at larger sizes can turn it on.
/// </para>
/// </remarks>
public sealed class TextStyle : ObservableModel
{
    private string _fontFamily = DefaultFontFamily;
    private double _fontSize = 16.0;
    private bool _isBold;
    private bool _isItalic;
    private bool _isUnderline;
    private bool _isStrikethrough;
    private TextAlignment _alignment = TextAlignment.Left;
    private double _lineSpacing = 1.0;
    private double _letterSpacing;
    private bool _isAntiAliased;
    private PixelColor _color = PixelColor.Black;

    /// <summary>
    /// The font used when none has been chosen. A generic family name rather than a specific
    /// font, so that every platform resolves it to something that exists.
    /// </summary>
    public const string DefaultFontFamily = "Monospace";

    /// <summary>The font family name, for example <c>"Consolas"</c>.</summary>
    public string FontFamily
    {
        get => _fontFamily;
        set => SetProperty(ref _fontFamily, string.IsNullOrWhiteSpace(value) ? DefaultFontFamily : value);
    }

    /// <summary>The font size in pixels, clamped to a usable range.</summary>
    public double FontSize
    {
        get => _fontSize;
        set => SetProperty(ref _fontSize, Math.Clamp(value, 4.0, 512.0));
    }

    /// <summary>Whether the text is drawn in a bold weight.</summary>
    public bool IsBold
    {
        get => _isBold;
        set => SetProperty(ref _isBold, value);
    }

    /// <summary>Whether the text is drawn in an italic style.</summary>
    public bool IsItalic
    {
        get => _isItalic;
        set => SetProperty(ref _isItalic, value);
    }

    /// <summary>Whether a line is drawn under the text.</summary>
    public bool IsUnderline
    {
        get => _isUnderline;
        set => SetProperty(ref _isUnderline, value);
    }

    /// <summary>Whether a line is drawn through the text.</summary>
    public bool IsStrikethrough
    {
        get => _isStrikethrough;
        set => SetProperty(ref _isStrikethrough, value);
    }

    /// <summary>How the lines line up with one another.</summary>
    public TextAlignment Alignment
    {
        get => _alignment;
        set => SetProperty(ref _alignment, value);
    }

    /// <summary>
    /// A multiplier on the font's natural line height, where 1.0 is single spacing.
    /// </summary>
    public double LineSpacing
    {
        get => _lineSpacing;
        set => SetProperty(ref _lineSpacing, Math.Clamp(value, 0.5, 4.0));
    }

    /// <summary>Extra space inserted between characters, in pixels; may be negative.</summary>
    public double LetterSpacing
    {
        get => _letterSpacing;
        set => SetProperty(ref _letterSpacing, Math.Clamp(value, -32.0, 64.0));
    }

    /// <summary>
    /// Whether glyph edges are smoothed. Off by default; see the type remarks for why.
    /// </summary>
    public bool IsAntiAliased
    {
        get => _isAntiAliased;
        set => SetProperty(ref _isAntiAliased, value);
    }

    /// <summary>The colour the glyphs are filled with.</summary>
    public PixelColor Color
    {
        get => _color;
        set => SetProperty(ref _color, value);
    }

    /// <summary>Creates an independent copy of this style.</summary>
    /// <returns>A new style with the same values and no subscribers.</returns>
    public TextStyle Clone()
    {
        TextStyle copy = new();
        copy.CopyFrom(this);
        return copy;
    }

    /// <summary>Overwrites every value of this style with another's.</summary>
    /// <param name="other">The style to copy from.</param>
    public void CopyFrom(TextStyle other)
    {
        ArgumentNullException.ThrowIfNull(other);

        FontFamily = other.FontFamily;
        FontSize = other.FontSize;
        IsBold = other.IsBold;
        IsItalic = other.IsItalic;
        IsUnderline = other.IsUnderline;
        IsStrikethrough = other.IsStrikethrough;
        Alignment = other.Alignment;
        LineSpacing = other.LineSpacing;
        LetterSpacing = other.LetterSpacing;
        IsAntiAliased = other.IsAntiAliased;
        Color = other.Color;
    }

    /// <summary>
    /// Determines whether another style would produce identical pixels.
    /// </summary>
    /// <param name="other">The style to compare with.</param>
    /// <returns><see langword="true"/> when every visual value matches.</returns>
    /// <remarks>
    /// Used to skip re-rasterising when a panel writes back a value that has not actually
    /// changed, which happens constantly while a slider is dragged.
    /// </remarks>
    public bool VisuallyEquals(TextStyle? other) =>
        other is not null
        && string.Equals(FontFamily, other.FontFamily, StringComparison.Ordinal)
        && FontSize.Equals(other.FontSize)
        && IsBold == other.IsBold
        && IsItalic == other.IsItalic
        && IsUnderline == other.IsUnderline
        && IsStrikethrough == other.IsStrikethrough
        && Alignment == other.Alignment
        && LineSpacing.Equals(other.LineSpacing)
        && LetterSpacing.Equals(other.LetterSpacing)
        && IsAntiAliased == other.IsAntiAliased
        && Color == other.Color;
}
