using System.Text;
using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Text;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for the built-in pixel font and the text pipeline that draws with it.
/// </summary>
/// <remarks>
/// These are possible at all because the font and its renderer live in Core with no font
/// engine behind them. That is the same property that makes the font render identically on
/// every platform: there is nothing here that could behave differently somewhere else.
/// </remarks>
public sealed class PixelFontTests
{
    private static readonly PixelColor Black = new(0, 0, 0);

    [Fact]
    public void EveryPrintableCharacterExceptSpaceDrawsSomething()
    {
        for (char c = '!'; c <= '~'; c++)
        {
            Assert.True(HasAnyPixel(c), $"'{c}' renders as nothing, so it cannot be read.");
        }

        Assert.False(HasAnyPixel(' '));
    }

    [Fact]
    public void NoTwoCharactersLookTheSame()
    {
        // The strongest guard there is against a typo in the glyph table: two characters
        // sharing a picture is exactly what a mistyped row produces, and it is the one font
        // bug that is invisible until an artist writes a word containing both.
        Dictionary<string, char> seen = [];

        for (char c = '!'; c <= '~'; c++)
        {
            string shape = Describe(c);

            Assert.False(
                seen.TryGetValue(shape, out char other),
                $"'{c}' and '{other}' are drawn identically.");

            seen[shape] = c;
        }
    }

    [Fact]
    public void TextIsDrawnWithFullyOpaquePixelsAndNothingInBetween()
    {
        TextStyle style = new() { Color = Black };

        TextRasterResult raster = Assert.IsType<TextRasterResult>(
            PixelFontRasterizer.Instance.Rasterize("Hello", style));

        bool any = false;

        for (int y = 0; y < raster.Pixels.Height; y++)
        {
            for (int x = 0; x < raster.Pixels.Width; x++)
            {
                PixelColor pixel = raster.Pixels.GetPixel(x, y);

                // The whole point of a pixel font: no half-covered edge pixels to clean up.
                Assert.True(pixel.A is 0 or 255);
                any |= pixel.A == 255;
            }
        }

        Assert.True(any);
    }

    [Fact]
    public void ACapitalLetterMatchesItsGlyphExactly()
    {
        TextStyle style = new() { Color = Black, FontSize = PixelFont.CellHeight };

        TextRasterResult raster = Assert.IsType<TextRasterResult>(
            PixelFontRasterizer.Instance.Rasterize("T", style));

        for (int y = 0; y < PixelFont.CellHeight; y++)
        {
            for (int x = 0; x < PixelFont.CellWidth; x++)
            {
                bool expected = PixelFont.Default.IsPixelSet('T', x, y);
                bool actual = raster.Pixels.GetPixel(x, y).A == 255;

                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    public void DoublingTheSizeDoublesEveryPixelExactly()
    {
        TextStyle style = new() { Color = Black, FontSize = PixelFont.CellHeight * 2 };

        TextRasterResult raster = Assert.IsType<TextRasterResult>(
            PixelFontRasterizer.Instance.Rasterize("E", style));

        for (int y = 0; y < PixelFont.CellHeight * 2; y++)
        {
            for (int x = 0; x < PixelFont.CellWidth * 2; x++)
            {
                bool expected = PixelFont.Default.IsPixelSet('E', x / 2, y / 2);
                Assert.Equal(expected, raster.Pixels.GetPixel(x, y).A == 255);
            }
        }
    }

    [Fact]
    public void ASizeBetweenTwoWholeMultiplesIsSnappedToOne()
    {
        TextStyle style = new() { FontSize = 13 };

        // A bitmap font at 1.6x would have to drop or double single rows of pixels, which is
        // visibly lopsided. The size is snapped rather than honoured approximately.
        Assert.Equal(16.0, style.FontSize);
        Assert.True(style.UsesPixelFont);
    }

    [Fact]
    public void ASystemFontIsNeverSetToASizeItCannotBeReadAt()
    {
        TextStyle style = new() { FontFamily = "Consolas", FontSize = 4 };

        Assert.Equal(TextStyle.MinimumSystemFontSize, style.FontSize);
    }

    [Fact]
    public void SwitchingFromThePixelFontToASystemFontRaisesAnUnreadableSize()
    {
        TextStyle style = new() { FontSize = PixelFont.CellHeight };
        Assert.Equal(8.0, style.FontSize);

        style.FontFamily = "Consolas";

        Assert.Equal(TextStyle.MinimumSystemFontSize, style.FontSize);
    }

    [Fact]
    public void NewTextUsesTheBuiltInFont()
    {
        TextStyle style = new();

        Assert.True(style.UsesPixelFont);
        Assert.Equal(PixelFont.FamilyName, style.FontFamily);
    }

    [Fact]
    public void EachLineOfATextBlockIsDrawnBelowTheLast()
    {
        TextStyle style = new() { Color = Black, FontSize = PixelFont.CellHeight };

        TextRasterResult one = Assert.IsType<TextRasterResult>(
            PixelFontRasterizer.Instance.Rasterize("A", style));

        TextRasterResult two = Assert.IsType<TextRasterResult>(
            PixelFontRasterizer.Instance.Rasterize("A\nA", style));

        Assert.Equal(one.Pixels.Width, two.Pixels.Width);
        Assert.True(two.Pixels.Height > one.Pixels.Height);
    }

    [Fact]
    public void ClickingWhereACharacterWasDrawnPutsTheCaretOnIt()
    {
        TextStyle style = new() { Color = Black, FontSize = PixelFont.CellHeight };
        PixelFontRasterizer rasterizer = PixelFontRasterizer.Instance;

        for (int index = 0; index <= 5; index++)
        {
            ImageRect caret = rasterizer.MeasureCaret("abcde", style, index);
            int round = rasterizer.HitTestCaret("abcde", style, new ImagePoint(caret.X, caret.Y));

            Assert.Equal(index, round);
        }
    }

    [Fact]
    public void TypingOnTheCanvasPutsPixelsIntoTheLayer()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(64, 32));
        DocumentWorkspace workspace = new(document);

        TextStyle style = new() { Color = Black };
        TextLayer layer = document.CreateTextLayer("Text 1", style);
        layer.Origin = new ImagePoint(2, 2);

        workspace.ActiveLayer = layer;

        TextEditSession session = new(workspace, layer, PixelFontRasterizer.Instance, isNewLayer: true);
        workspace.BeginTextEdit(session);

        session.InsertText("Hi");

        Assert.Equal("Hi", layer.Text);
        Assert.True(CountOpaque(document.GetOrCreateCel(layer, document.Frames[0]).Pixels) > 0);

        // The whole episode of typing is one history entry, not one per letter.
        Assert.True(workspace.CommitTextEdit());
        Assert.Equal(1, workspace.History.Count);
    }

    [Fact]
    public void DeletingEveryCharacterLeavesNoGlyphsBehind()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(64, 32));
        DocumentWorkspace workspace = new(document);

        TextLayer layer = document.CreateTextLayer("Text 1", new TextStyle { Color = Black });
        workspace.ActiveLayer = layer;

        TextEditSession session = new(workspace, layer, PixelFontRasterizer.Instance, isNewLayer: false);
        workspace.BeginTextEdit(session);

        session.InsertText("Hi");
        session.DeleteBackward();
        session.DeleteBackward();

        // The cel is redrawn in full rather than patched, so nothing may survive the deletion.
        Assert.Equal(string.Empty, layer.Text);
        Assert.Equal(0, CountOpaque(document.GetOrCreateCel(layer, document.Frames[0]).Pixels));
    }

    [Fact]
    public void ChangingTheStyleRedrawsTheText()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(128, 64));
        DocumentWorkspace workspace = new(document);

        TextLayer layer = document.CreateTextLayer("Text 1", new TextStyle { Color = Black });
        workspace.ActiveLayer = layer;

        TextEditSession session = new(workspace, layer, PixelFontRasterizer.Instance, isNewLayer: false);
        workspace.BeginTextEdit(session);
        session.InsertText("Wide");

        int before = CountOpaque(document.GetOrCreateCel(layer, document.Frames[0]).Pixels);

        layer.Style.FontSize = PixelFont.CellHeight * 3;
        session.Refresh();

        int after = CountOpaque(document.GetOrCreateCel(layer, document.Frames[0]).Pixels);

        Assert.True(after > before, "Tripling the size drew no more pixels than before.");
    }

    private static bool HasAnyPixel(char value)
    {
        for (int y = 0; y < PixelFont.CellHeight; y++)
        {
            for (int x = 0; x < PixelFont.CellWidth; x++)
            {
                if (PixelFont.Default.IsPixelSet(value, x, y))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string Describe(char value)
    {
        StringBuilder builder = new();

        for (int y = 0; y < PixelFont.CellHeight; y++)
        {
            for (int x = 0; x < PixelFont.CellWidth; x++)
            {
                builder.Append(PixelFont.Default.IsPixelSet(value, x, y) ? '#' : '.');
            }
        }

        return builder.ToString();
    }

    private static int CountOpaque(PixelBuffer pixels)
    {
        int count = 0;

        for (int y = 0; y < pixels.Height; y++)
        {
            for (int x = 0; x < pixels.Width; x++)
            {
                if (pixels.GetPixel(x, y).A > 0)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
