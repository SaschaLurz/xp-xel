using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.History;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Rendering;
using XPXel.Core.Text;

namespace XPXel.Core.Tests;

/// <summary>
/// Tests for editable text layers.
/// </summary>
/// <remarks>
/// These run against a stub rasteriser rather than a font engine. That is the point of
/// <see cref="ITextRasterizer"/> being an interface in Core: the words, the undo behaviour and
/// the compositing can all be exercised headlessly, and only the glyph shapes need a display.
/// </remarks>
public sealed class TextLayerTests
{
    /// <summary>
    /// A rasteriser that draws each character as one opaque pixel in a single row, so a test
    /// can assert on exactly where the "glyphs" land.
    /// </summary>
    private sealed class BlockRasterizer : ITextRasterizer
    {
        public IReadOnlyList<string> FontFamilies { get; } = ["Test"];

        public TextRasterResult? Rasterize(string text, TextStyle style)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            PixelBuffer pixels = new(text.Length, 1);
            for (int x = 0; x < text.Length; x++)
            {
                pixels.SetPixel(x, 0, style.Color);
            }

            return new TextRasterResult(pixels, ImagePoint.Origin);
        }

        public ImageRect MeasureCaret(string text, TextStyle style, int caretIndex) =>
            new(caretIndex, 0, 1, 1);

        public int HitTestCaret(string text, TextStyle style, ImagePoint point) =>
            Math.Clamp(point.X, 0, text.Length);
    }

    private static (DocumentWorkspace Workspace, TextLayer Layer, BlockRasterizer Rasterizer) CreateWorkspace()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(16, 8));
        DocumentWorkspace workspace = new(document);
        BlockRasterizer rasterizer = new();

        TextLayer layer = document.CreateTextLayer("Text 1");
        layer.Origin = new ImagePoint(2, 3);
        workspace.ActiveLayer = layer;

        return (workspace, layer, rasterizer);
    }

    [Fact]
    public void RenderingWritesTheGlyphsIntoTheLayersCel()
    {
        (DocumentWorkspace workspace, TextLayer layer, BlockRasterizer rasterizer) = CreateWorkspace();
        layer.Text = "abc";

        TextLayerRenderer.Render(workspace.Document, layer, workspace.ActiveFrame, rasterizer);

        Cel cel = workspace.Document.Cels.Find(layer.Id, workspace.ActiveFrame.Id)!;
        Assert.Equal(PixelColor.Black, cel.Pixels.GetPixel(2, 3));
        Assert.Equal(PixelColor.Black, cel.Pixels.GetPixel(4, 3));
        Assert.True(cel.Pixels.GetPixel(5, 3).IsTransparent);
    }

    [Fact]
    public void RenderingClearsWhatThePreviousTextDrew()
    {
        (DocumentWorkspace workspace, TextLayer layer, BlockRasterizer rasterizer) = CreateWorkspace();

        layer.Text = "abcdef";
        TextLayerRenderer.Render(workspace.Document, layer, workspace.ActiveFrame, rasterizer);

        layer.Text = "ab";
        TextLayerRenderer.Render(workspace.Document, layer, workspace.ActiveFrame, rasterizer);

        Cel cel = workspace.Document.Cels.Find(layer.Id, workspace.ActiveFrame.Id)!;
        Assert.Equal(PixelColor.Black, cel.Pixels.GetPixel(3, 3));
        Assert.True(cel.Pixels.GetPixel(4, 3).IsTransparent);
    }

    [Fact]
    public void TheCompositorDrawsATextLayerJustLikeARasterOne()
    {
        (DocumentWorkspace workspace, TextLayer layer, BlockRasterizer rasterizer) = CreateWorkspace();
        layer.Text = "xy";
        TextLayerRenderer.Render(workspace.Document, layer, workspace.ActiveFrame, rasterizer);

        LayerCompositor compositor = new();
        PixelBuffer flattened = compositor.ComposeToNewBuffer(workspace.Document, workspace.ActiveFrame);

        Assert.Equal(PixelColor.Black, flattened.GetPixel(2, 3));
        Assert.Equal(PixelColor.Black, flattened.GetPixel(3, 3));
    }

    [Fact]
    public void AnEditSessionRecordsOneHistoryEntryForAWholeEpisodeOfTyping()
    {
        (DocumentWorkspace workspace, TextLayer layer, BlockRasterizer rasterizer) = CreateWorkspace();
        layer.Text = "start";
        TextLayerRenderer.Render(workspace.Document, layer, workspace.ActiveFrame, rasterizer);

        TextEditSession session = new(workspace, layer, rasterizer, isNewLayer: false);
        workspace.BeginTextEdit(session);

        session.InsertText("!");
        session.InsertText("?");
        session.DeleteBackward();

        Assert.True(workspace.CommitTextEdit());
        Assert.Equal("start!", layer.Text);

        // One entry, not three: the artist experienced it as one act of typing.
        Assert.True(workspace.History.CanUndo);
        workspace.History.Undo();
        Assert.Equal("start", layer.Text);
        Assert.False(workspace.History.CanUndo);
    }

    [Fact]
    public void UndoRestoresThePixelsAsWellAsTheWords()
    {
        (DocumentWorkspace workspace, TextLayer layer, BlockRasterizer rasterizer) = CreateWorkspace();
        layer.Text = "ab";
        TextLayerRenderer.Render(workspace.Document, layer, workspace.ActiveFrame, rasterizer);

        TextEditSession session = new(workspace, layer, rasterizer, isNewLayer: false);
        workspace.BeginTextEdit(session);
        session.InsertText("cd");
        workspace.CommitTextEdit();

        Cel cel = workspace.Document.Cels.Find(layer.Id, workspace.ActiveFrame.Id)!;
        Assert.Equal(PixelColor.Black, cel.Pixels.GetPixel(5, 3));

        workspace.History.Undo();

        // The pixels are regenerated from the restored text rather than stored in the command,
        // so they have to come back exactly.
        Assert.Equal("ab", layer.Text);
        Assert.True(cel.Pixels.GetPixel(5, 3).IsTransparent);
        Assert.Equal(PixelColor.Black, cel.Pixels.GetPixel(3, 3));
    }

    [Fact]
    public void ANewLayerThatWasNeverTypedIntoIsRemovedAgain()
    {
        (DocumentWorkspace workspace, TextLayer layer, BlockRasterizer rasterizer) = CreateWorkspace();

        TextEditSession session = new(workspace, layer, rasterizer, isNewLayer: true);
        workspace.BeginTextEdit(session);

        Assert.False(workspace.CommitTextEdit());
        Assert.DoesNotContain(layer, workspace.Document.EnumerateLayers());
        Assert.False(workspace.History.CanUndo);
    }

    [Fact]
    public void ANewLayerWithTextIsUndoneByRemovingTheLayer()
    {
        (DocumentWorkspace workspace, TextLayer layer, BlockRasterizer rasterizer) = CreateWorkspace();

        TextEditSession session = new(workspace, layer, rasterizer, isNewLayer: true);
        workspace.BeginTextEdit(session);
        session.InsertText("hi");

        Assert.True(workspace.CommitTextEdit());
        Assert.Contains(layer, workspace.Document.EnumerateLayers());

        workspace.History.Undo();
        Assert.DoesNotContain(layer, workspace.Document.EnumerateLayers());

        workspace.History.Redo();
        Assert.Contains(layer, workspace.Document.EnumerateLayers());
    }

    [Fact]
    public void CancellingPutsTheLayerBackExactlyAsItWasFound()
    {
        (DocumentWorkspace workspace, TextLayer layer, BlockRasterizer rasterizer) = CreateWorkspace();
        layer.Text = "keep";

        // Whole multiples of the pixel font's cell height, because that is what the default
        // font can render exactly; anything between two of them is snapped to one.
        layer.Style.FontSize = 8;
        TextLayerRenderer.Render(workspace.Document, layer, workspace.ActiveFrame, rasterizer);

        TextEditSession session = new(workspace, layer, rasterizer, isNewLayer: false);
        workspace.BeginTextEdit(session);
        session.InsertText(" more");
        layer.Style.FontSize = 40;

        workspace.CancelTextEdit();

        Assert.Equal("keep", layer.Text);
        Assert.Equal(8, layer.Style.FontSize);
        Assert.False(workspace.History.CanUndo);
    }

    [Fact]
    public void TheCaretMovesByCharacterAndByLine()
    {
        (DocumentWorkspace workspace, TextLayer layer, BlockRasterizer rasterizer) = CreateWorkspace();

        TextEditSession session = new(workspace, layer, rasterizer, isNewLayer: false);
        session.InsertText("ab\ncd");

        Assert.Equal(5, session.CaretIndex);

        session.MoveCaret(CaretMovement.Left);
        Assert.Equal(4, session.CaretIndex);

        session.MoveCaret(CaretMovement.Up);
        Assert.Equal(1, session.CaretIndex);

        session.MoveCaret(CaretMovement.LineEnd);
        Assert.Equal(2, session.CaretIndex);

        session.MoveCaret(CaretMovement.Down);
        Assert.Equal(5, session.CaretIndex);

        session.MoveCaret(CaretMovement.TextStart);
        Assert.Equal(0, session.CaretIndex);

        // Nothing before the start, so the caret stays put rather than going negative.
        session.MoveCaret(CaretMovement.Left);
        Assert.Equal(0, session.CaretIndex);
    }

    [Fact]
    public void DrawingToolsCannotEditATextLayer()
    {
        (DocumentWorkspace workspace, TextLayer layer, _) = CreateWorkspace();

        // A text layer's pixels are a cache regenerated from its words, so a stroke laid on it
        // would be thrown away by the next re-render. Refusing up front is the honest answer.
        Assert.Same(layer, workspace.ActiveLayer);
        Assert.Null(workspace.ActiveRasterLayer);
        Assert.False(workspace.CanEditActiveLayer);
        Assert.Null(workspace.GetOrCreateActiveCel());
    }

    [Fact]
    public void DuplicatingATextLayerCopiesTheWordsAndStyleButNotTheStyleInstance()
    {
        (_, TextLayer layer, _) = CreateWorkspace();
        layer.Text = "original";
        layer.Style.IsBold = true;

        TextLayer copy = (TextLayer)layer.CreateStructuralCopy(Guid.NewGuid(), "Copy");

        Assert.Equal("original", copy.Text);
        Assert.True(copy.Style.IsBold);
        Assert.NotSame(layer.Style, copy.Style);

        copy.Style.IsBold = false;
        Assert.True(layer.Style.IsBold);
    }
}
