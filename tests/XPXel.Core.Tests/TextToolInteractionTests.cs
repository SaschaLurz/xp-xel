using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.Primitives;
using XPXel.Core.Text;
using XPXel.Core.Tools;

namespace XPXel.Core.Tests;

/// <summary>
/// Drives the text tool the way the canvas does, on a document the size an artist actually
/// starts with.
/// </summary>
/// <remarks>
/// These exist because the text tool was reported as throwing in use while every unit test
/// around it passed. The difference was the sequence: a real session is click, type, click
/// away, click back, undo — and the failures live in the joins between those, not inside any
/// one of them.
/// </remarks>
public sealed class TextToolInteractionTests
{
    private static readonly PixelColor Black = new(0, 0, 0);

    [Fact]
    public void TypingTextWiderThanTheCanvasDoesNotThrow()
    {
        Fixture fixture = new(32, 32);

        fixture.Click(2, 2);
        fixture.Type("A very long line indeed");

        // The glyphs run far past a 32-pixel canvas. Nothing may throw, and what fits must
        // still be drawn.
        Assert.NotNull(fixture.Workspace.ActiveTextEdit);
        Assert.True(fixture.OpaqueCount() > 0);
    }

    [Fact]
    public void TypingOutsideTheCanvasEntirelyDoesNotThrow()
    {
        Fixture fixture = new(32, 32);

        // Clicking beyond the edge is legal: the canvas hands tools whatever the pointer was
        // over, and a text block may legitimately start off-canvas and run back on.
        fixture.Click(60, 60);
        fixture.Type("Hello");

        fixture.Click(-20, -20);
        fixture.Type("Also hello");

        Assert.True(fixture.Document.EnumerateLayers().OfType<TextLayer>().Any());
    }

    [Fact]
    public void ClickingAwayCommitsAndClickingBackReopensTheSameLayer()
    {
        Fixture fixture = new(64, 64);

        fixture.Click(4, 4);
        fixture.Type("Hi");

        TextLayer first = fixture.Workspace.ActiveTextEdit!.Layer;

        // Clicking empty canvas commits the first block and starts a second.
        fixture.Click(40, 40);
        Assert.NotSame(first, fixture.Workspace.ActiveTextEdit!.Layer);

        fixture.Workspace.CancelTextEdit();

        // Clicking back on the first block's glyphs reopens it rather than making a third.
        fixture.Click(5, 8);
        Assert.Same(first, fixture.Workspace.ActiveTextEdit!.Layer);
    }

    [Fact]
    public void AStrayClickLeavesNoEmptyLayerBehind()
    {
        Fixture fixture = new(64, 64);

        int before = fixture.Document.EnumerateLayers().Count();

        fixture.Click(10, 10);
        fixture.Workspace.CommitTextEdit();

        Assert.Equal(before, fixture.Document.EnumerateLayers().Count());
    }

    [Fact]
    public void UndoingTypedTextAndRedoingItDoesNotThrow()
    {
        Fixture fixture = new(64, 64);

        fixture.Click(4, 4);
        fixture.Type("Undo me");
        fixture.Workspace.CommitTextEdit();

        Assert.True(fixture.Workspace.History.Undo());
        Assert.True(fixture.Workspace.History.Redo());

        Assert.True(fixture.OpaqueCount() > 0);
    }

    [Fact]
    public void EditingAnExistingBlockRecordsOneEntryAndUndoesCleanly()
    {
        Fixture fixture = new(64, 64);

        fixture.Click(4, 4);
        fixture.Type("one");
        fixture.Workspace.CommitTextEdit();

        int afterFirst = fixture.Workspace.History.Count;

        fixture.Click(5, 8);
        fixture.Type(" two");
        fixture.Workspace.CommitTextEdit();

        Assert.Equal(afterFirst + 1, fixture.Workspace.History.Count);

        fixture.Workspace.History.Undo();
        Assert.Equal("one", fixture.Document.EnumerateLayers().OfType<TextLayer>().First().Text);
    }

    [Fact]
    public void ClickingIntoTheMiddleOfExistingTextMovesTheCaretThere()
    {
        Fixture fixture = new(96, 32);

        fixture.Click(2, 2);
        fixture.Type("abcdef");

        TextEditSession session = fixture.Workspace.ActiveTextEdit!;

        // Straight through the hit test and back, which is where an off-by-one in the layout
        // would show up as the caret landing on the wrong character.
        ImageRect caret = fixture.Rasterizer.MeasureCaret("abcdef", session.Layer.Style, 3);
        fixture.Click(session.Layer.Origin.X + caret.X, session.Layer.Origin.Y + caret.Y);

        Assert.Equal(3, session.CaretIndex);
    }

    [Fact]
    public void EveryStyleTheTextPanelCanSetRendersWithoutThrowing()
    {
        Fixture fixture = new(64, 64);

        fixture.Click(4, 4);
        fixture.Type("Style");

        TextEditSession session = fixture.Workspace.ActiveTextEdit!;
        TextStyle style = session.Layer.Style;

        foreach (bool bold in (bool[])[false, true])
        {
            foreach (bool italic in (bool[])[false, true])
            {
                foreach (TextAlignment alignment in Enum.GetValues<TextAlignment>())
                {
                    style.IsBold = bold;
                    style.IsItalic = italic;
                    style.IsUnderline = bold;
                    style.IsStrikethrough = italic;
                    style.Alignment = alignment;
                    style.LineSpacing = italic ? 2.0 : 1.0;
                    style.LetterSpacing = bold ? 2 : -1;

                    session.Refresh();
                }
            }
        }

        Assert.True(fixture.OpaqueCount() > 0);
    }

    [Fact]
    public void MultipleLinesRenderAndTheCaretFollowsThem()
    {
        Fixture fixture = new(96, 96);

        fixture.Click(2, 2);
        fixture.Type("one\ntwo\nthree");

        TextEditSession session = fixture.Workspace.ActiveTextEdit!;

        session.MoveCaret(CaretMovement.TextStart);
        session.MoveCaret(CaretMovement.Down);
        session.MoveCaret(CaretMovement.LineEnd);

        Assert.Equal(7, session.CaretIndex);
        Assert.True(fixture.OpaqueCount() > 0);
    }

    /// <summary>A document, a workspace and a text tool, wired as the canvas wires them.</summary>
    private sealed class Fixture
    {
        private readonly TextTool _tool;

        public Fixture(int width, int height)
        {
            Document = PixelDocument.CreateNew(new ImageSize(width, height));
            Workspace = new DocumentWorkspace(Document);
            Rasterizer = PixelFontRasterizer.Instance;

            ToolSettings settings = new();
            settings.TextDefaults.Color = Black;

            Context = new ToolContext(Workspace, settings, new ColorSlots { Primary = Black });
            _tool = new TextTool(Rasterizer);
        }

        public PixelDocument Document { get; }

        public DocumentWorkspace Workspace { get; }

        public ToolContext Context { get; }

        public PixelFontRasterizer Rasterizer { get; }

        public void Click(int x, int y)
        {
            ToolPointerEventArgs args = new(new ImagePoint(x, y), ToolPointerButton.Left);

            _tool.OnPointerPressed(Context, args);
            _tool.OnPointerReleased(Context, args);

            // The canvas cancels after every click, because releasing drops the pointer
            // capture. The text tool must survive that without ending the edit.
            _tool.Cancel(Context);
        }

        public void Type(string text)
        {
            foreach (char c in text)
            {
                Workspace.ActiveTextEdit?.InsertText(c.ToString());
            }
        }

        public int OpaqueCount()
        {
            int count = 0;

            foreach (LayerNode node in Document.EnumerateLayers())
            {
                if (node is not TextLayer text)
                {
                    continue;
                }

                Cel cel = Document.GetOrCreateCel(text, Document.Frames[0]);

                for (int y = 0; y < cel.Pixels.Height; y++)
                {
                    for (int x = 0; x < cel.Pixels.Width; x++)
                    {
                        if (cel.Pixels.GetPixel(x, y).A > 0)
                        {
                            count++;
                        }
                    }
                }
            }

            return count;
        }
    }
}
