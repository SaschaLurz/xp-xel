using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.Primitives;
using XPXel.Core.Text;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for selecting characters inside a text layer: what dragging across the words, holding
/// Shift with the arrow keys, and then typing or deleting actually do to the text.
/// </summary>
public sealed class TextSelectionTests
{
    [Fact]
    public void ThereIsNoRangeUntilOneIsMade()
    {
        Fixture fixture = new("abcdef");

        Assert.False(fixture.Session.HasSelection);
        Assert.Equal(string.Empty, fixture.Session.SelectedText);
        Assert.Empty(fixture.Session.GetSelectionRectangles());
    }

    [Fact]
    public void ShiftAndAnArrowKeyExtendsTheRange()
    {
        Fixture fixture = new("abcdef");

        fixture.Session.MoveCaret(CaretMovement.TextStart);
        fixture.Session.MoveCaret(CaretMovement.Right, extend: true);
        fixture.Session.MoveCaret(CaretMovement.Right, extend: true);

        Assert.True(fixture.Session.HasSelection);
        Assert.Equal("ab", fixture.Session.SelectedText);
    }

    [Fact]
    public void ARangeCanBeMadeBackwardsAsWellAsForwards()
    {
        Fixture fixture = new("abcdef");

        // The caret starts at the end of the text, so this selects towards the start.
        fixture.Session.MoveCaret(CaretMovement.Left, extend: true);
        fixture.Session.MoveCaret(CaretMovement.Left, extend: true);

        Assert.Equal("ef", fixture.Session.SelectedText);
        Assert.Equal(4, fixture.Session.SelectionStart);
        Assert.Equal(6, fixture.Session.SelectionEnd);
    }

    [Fact]
    public void MovingWithoutShiftCollapsesTheRangeRatherThanJumpingPastIt()
    {
        Fixture fixture = new("abcdef");

        fixture.Session.MoveCaret(CaretMovement.TextStart);
        fixture.Session.MoveCaret(CaretMovement.Right, extend: true);
        fixture.Session.MoveCaret(CaretMovement.Right, extend: true);

        fixture.Session.MoveCaret(CaretMovement.Right);

        Assert.False(fixture.Session.HasSelection);
        Assert.Equal(2, fixture.Session.CaretIndex);
    }

    [Fact]
    public void TypingReplacesTheSelectedCharacters()
    {
        Fixture fixture = new("abcdef");

        fixture.Session.SelectAll();
        fixture.Session.InsertText("z");

        Assert.Equal("z", fixture.Session.Layer.Text);
        Assert.Equal(1, fixture.Session.CaretIndex);
        Assert.False(fixture.Session.HasSelection);
    }

    [Fact]
    public void BackspaceRemovesTheWholeRangeRatherThanOneCharacter()
    {
        Fixture fixture = new("abcdef");

        fixture.Session.MoveCaret(CaretMovement.TextStart);
        fixture.Session.MoveCaret(CaretMovement.Right, extend: true);
        fixture.Session.MoveCaret(CaretMovement.Right, extend: true);
        fixture.Session.MoveCaret(CaretMovement.Right, extend: true);

        fixture.Session.DeleteBackward();

        Assert.Equal("def", fixture.Session.Layer.Text);
        Assert.Equal(0, fixture.Session.CaretIndex);
    }

    [Fact]
    public void DeleteRemovesTheWholeRangeToo()
    {
        Fixture fixture = new("abcdef");

        fixture.Session.SelectAll();
        fixture.Session.DeleteForward();

        Assert.Equal(string.Empty, fixture.Session.Layer.Text);
    }

    [Fact]
    public void SelectingAllOfEmptyTextSelectsNothing()
    {
        Fixture fixture = new(string.Empty);

        fixture.Session.SelectAll();

        Assert.False(fixture.Session.HasSelection);
    }

    [Fact]
    public void ARangeSpanningLinesIsHighlightedOneRectanglePerLine()
    {
        Fixture fixture = new("one\ntwo\nthree");

        fixture.Session.SelectAll();

        Assert.Equal(3, fixture.Session.GetSelectionRectangles().Count);
    }

    [Fact]
    public void TheHighlightSitsWhereTheWordsAre()
    {
        Fixture fixture = new("abc");
        fixture.Session.Layer.Origin = new ImagePoint(5, 7);

        fixture.Session.SelectAll();

        ImageRect highlight = fixture.Session.GetSelectionRectangles()[0];

        // Measured from the block origin like everything else about a text layer, so the
        // highlight cannot drift away from the glyphs it is under.
        Assert.Equal(5, highlight.Left);
        Assert.True(highlight.Width > 0);
    }

    [Fact]
    public void DraggingAcrossTheWordsSelectsWhatWasCrossed()
    {
        Fixture fixture = new("abcdef");

        // Where the press lands, then where the pointer has been dragged to: the second call
        // extends from the first, which is what the tool does on every pointer move.
        ImageRect start = fixture.Rasterizer.MeasureCaret("abcdef", fixture.Session.Layer.Style, 1);
        ImageRect end = fixture.Rasterizer.MeasureCaret("abcdef", fixture.Session.Layer.Style, 4);

        fixture.Session.MoveCaretTo(new ImagePoint(start.Left, start.Top));
        fixture.Session.MoveCaretTo(new ImagePoint(end.Left, end.Top), extend: true);

        Assert.Equal("bcd", fixture.Session.SelectedText);
    }

    [Fact]
    public void TheBoxBorderIsOutsideTheWordsSoTheCaretKeepsTheWordsThemselves()
    {
        Fixture fixture = new("abcdef");

        ImageRect box = fixture.Session.TextBounds;

        // A point in the middle of the glyphs belongs to the caret, whatever the tolerance.
        Assert.False(fixture.Session.IsOnBoxEdge(
            new ImagePoint(box.Left + (box.Width / 2), box.Top + (box.Height / 2)), tolerance: 6));

        // A point just outside them is the frame, which is what a drag takes hold of.
        Assert.True(fixture.Session.IsOnBoxEdge(new ImagePoint(box.Left - 2, box.Top - 2), tolerance: 6));

        // Far outside is neither: that is empty canvas, and a click there starts new text.
        Assert.False(fixture.Session.IsOnBoxEdge(new ImagePoint(box.Left - 40, box.Top - 40), tolerance: 6));
    }

    /// <summary>A document with one text layer, already open for editing.</summary>
    private sealed class Fixture
    {
        public Fixture(string text)
        {
            PixelDocument document = PixelDocument.CreateNew(new ImageSize(96, 32));
            DocumentWorkspace workspace = new(document);

            Rasterizer = PixelFontRasterizer.Instance;

            TextLayer layer = document.CreateTextLayer("Text 1");
            layer.Text = text;

            Session = new TextEditSession(workspace, layer, Rasterizer, isNewLayer: false);
            workspace.ActiveLayer = layer;
            workspace.BeginTextEdit(Session);
        }

        public TextEditSession Session { get; }

        public PixelFontRasterizer Rasterizer { get; }
    }
}
