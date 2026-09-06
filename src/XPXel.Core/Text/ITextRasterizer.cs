using XPXel.Core.Imaging;
using XPXel.Core.Primitives;

namespace XPXel.Core.Text;

/// <summary>
/// The pixels a piece of text was rendered into, together with where they sit relative to
/// the text block's anchor point.
/// </summary>
/// <param name="Pixels">The rendered glyphs, in straight (non-premultiplied) alpha.</param>
/// <param name="Origin">
/// The offset of <paramref name="Pixels"/>' top-left corner from the block's anchor. It is
/// usually zero or slightly negative: a glyph such as a lower-case "j" can overhang to the
/// left of the pen position, and italic text overhangs on both sides.
/// </param>
public sealed record TextRasterResult(PixelBuffer Pixels, ImagePoint Origin);

/// <summary>
/// Turns text and a <see cref="TextStyle"/> into pixels, and answers the geometry questions
/// a caret needs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is an interface in Core.</b> <c>XPXel.Core</c> references nothing but the
/// BCL, and there is no font rasteriser in the BCL that works identically on Windows, Linux,
/// macOS and WebAssembly. Text is nevertheless document data, so the model owns the text and
/// the style while rendering is delegated here and implemented in <c>XPXel.UI</c> on top of
/// Avalonia's text stack — the same shape as every other platform concern in the codebase.
/// </para>
/// <para>
/// <b>Coordinates.</b> Every position this interface deals in is relative to the text
/// block's anchor point, which is the top-left corner of its first line. The
/// <see cref="Documents.TextLayer.Origin"/> is what places that anchor in the document.
/// </para>
/// <para>
/// Implementations must tolerate empty text by returning <see langword="null"/> from
/// <see cref="Rasterize"/> rather than a zero-sized buffer, because a zero-sized
/// <see cref="PixelBuffer"/> is not a thing the rest of the pipeline expects to receive.
/// </para>
/// </remarks>
public interface ITextRasterizer
{
    /// <summary>The font families available on this platform, in display order.</summary>
    IReadOnlyList<string> FontFamilies { get; }

    /// <summary>Renders text into a pixel buffer.</summary>
    /// <param name="text">The text to render; may contain line breaks.</param>
    /// <param name="style">How to render it.</param>
    /// <returns>
    /// The rendered pixels, or <see langword="null"/> when the text is empty or produced no
    /// visible glyphs.
    /// </returns>
    TextRasterResult? Rasterize(string text, TextStyle style);

    /// <summary>
    /// Returns the caret rectangle for a position in the text, relative to the block anchor.
    /// </summary>
    /// <param name="text">The text being edited.</param>
    /// <param name="style">How the text is rendered.</param>
    /// <param name="caretIndex">The character index the caret sits before.</param>
    /// <returns>A one-pixel-wide rectangle as tall as the line the caret is on.</returns>
    ImageRect MeasureCaret(string text, TextStyle style, int caretIndex);

    /// <summary>
    /// Returns the character index nearest a point, so that clicking inside a text block
    /// puts the caret where the artist aimed.
    /// </summary>
    /// <param name="text">The text being edited.</param>
    /// <param name="style">How the text is rendered.</param>
    /// <param name="point">The point, relative to the block anchor.</param>
    /// <returns>A character index between 0 and the text's length.</returns>
    int HitTestCaret(string text, TextStyle style, ImagePoint point);
}

/// <summary>
/// A rasteriser that renders nothing, used where no font engine is available.
/// </summary>
/// <remarks>
/// Core's unit tests and any headless use of the document model run with this. It means a
/// project containing text layers still loads, edits and saves without a UI present — the
/// text simply has no pixels until something that can draw glyphs opens it.
/// </remarks>
public sealed class NullTextRasterizer : ITextRasterizer
{
    /// <summary>The single shared instance.</summary>
    public static NullTextRasterizer Instance { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<string> FontFamilies { get; } = [TextStyle.DefaultFontFamily];

    /// <inheritdoc />
    public TextRasterResult? Rasterize(string text, TextStyle style) => null;

    /// <inheritdoc />
    public ImageRect MeasureCaret(string text, TextStyle style, int caretIndex) =>
        new(0, 0, 1, (int)Math.Max(1.0, style?.FontSize ?? 1.0));

    /// <inheritdoc />
    public int HitTestCaret(string text, TextStyle style, ImagePoint point) => text?.Length ?? 0;
}
