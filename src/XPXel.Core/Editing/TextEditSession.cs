using XPXel.Core.ComponentModel;
using XPXel.Core.Documents;
using XPXel.Core.History;
using XPXel.Core.Primitives;
using XPXel.Core.Text;

namespace XPXel.Core.Editing;

/// <summary>Where a caret movement should go.</summary>
public enum CaretMovement
{
    /// <summary>One character towards the start of the text.</summary>
    Left,

    /// <summary>One character towards the end of the text.</summary>
    Right,

    /// <summary>The same column on the previous line.</summary>
    Up,

    /// <summary>The same column on the next line.</summary>
    Down,

    /// <summary>The start of the current line.</summary>
    LineStart,

    /// <summary>The end of the current line.</summary>
    LineEnd,

    /// <summary>The start of the whole text.</summary>
    TextStart,

    /// <summary>The end of the whole text.</summary>
    TextEnd,
}

/// <summary>
/// One episode of typing into a <see cref="TextLayer"/>: the layer, where the caret is, and
/// what the layer looked like before it started.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an explicit session rather than a tool that holds state.</b> Typing is not a
/// pointer gesture. The keystrokes arrive at the canvas control, which has no business
/// knowing how text is stored, and the text tool is a singleton that must not hold
/// document-specific state between interactions. A session on the workspace is where both
/// can meet: the tool creates it, the canvas drives it, and it belongs to exactly one
/// document.
/// </para>
/// <para>
/// <b>Every keystroke re-renders the layer, and none of them touch the undo history.</b> A
/// history entry per character would make Ctrl+Z useless for text. The whole episode becomes
/// a single entry when <see cref="Commit"/> runs, which is what the caller does when the
/// artist clicks away, presses Escape, or switches tool.
/// </para>
/// <para>
/// <b>Line breaks are the only wrapping.</b> The text is never wrapped to a width, so a
/// caret's line and column can be found in the string itself and agree exactly with what the
/// rasteriser drew. A wrapped text box would have to ask the rasteriser instead.
/// </para>
/// </remarks>
public sealed class TextEditSession : ObservableModel
{
    private readonly DocumentWorkspace _workspace;
    private readonly Frame _frame;
    private readonly ITextRasterizer _rasterizer;

    private readonly string _originalText;
    private readonly TextStyle _originalStyle;
    private readonly ImagePoint _originalOrigin;

    private int _caretIndex;

    /// <summary>
    /// Where a range selection was started from, or <see langword="null"/> when there is no
    /// range and the caret is simply a caret.
    /// </summary>
    /// <remarks>
    /// The anchor rather than a start-and-length pair, because a range is made by dragging and
    /// a drag has a fixed end and a moving one. Selecting backwards through the text is then the
    /// same code as selecting forwards, with the two ends read in the other order.
    /// </remarks>
    private int? _selectionAnchor;

    /// <summary>Starts editing a text layer.</summary>
    /// <param name="workspace">The document the layer belongs to.</param>
    /// <param name="layer">The layer being typed into.</param>
    /// <param name="rasterizer">The font engine to render with.</param>
    /// <param name="isNewLayer">
    /// Whether this session created the layer. If it did and nothing is typed, the layer is
    /// removed again on commit rather than left behind as an empty, invisible layer the
    /// artist has to notice and delete.
    /// </param>
    public TextEditSession(
        DocumentWorkspace workspace,
        TextLayer layer,
        ITextRasterizer rasterizer,
        bool isNewLayer)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(rasterizer);

        _workspace = workspace;
        _frame = workspace.ActiveFrame;
        _rasterizer = rasterizer;

        Layer = layer;
        IsNewLayer = isNewLayer;
        Rasterizer = rasterizer;

        _originalText = layer.Text;
        _originalStyle = layer.Style.Clone();
        _originalOrigin = layer.Origin;
        _caretIndex = layer.Text.Length;
    }

    /// <summary>Raised whenever the rendered text or the caret position changes.</summary>
    public event EventHandler? Changed;

    /// <summary>The layer being edited.</summary>
    public TextLayer Layer { get; }

    /// <summary>Whether this session created the layer it is editing.</summary>
    public bool IsNewLayer { get; }

    /// <summary>The font engine this session measures and renders with.</summary>
    /// <remarks>
    /// Exposed so the canvas can measure the same text the same way when it draws the box and
    /// the highlight behind a selected range. Measuring it with a different engine, or with
    /// different rounding, would put the highlight a pixel away from the glyphs it is under.
    /// </remarks>
    public ITextRasterizer Rasterizer { get; }

    /// <summary>Where the caret sits, as a character index into <see cref="TextLayer.Text"/>.</summary>
    public int CaretIndex
    {
        get => _caretIndex;
        private set
        {
            int clamped = Math.Clamp(value, 0, Layer.Text.Length);
            if (SetProperty(ref _caretIndex, clamped))
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>The first character of the selected range, or the caret when nothing is selected.</summary>
    public int SelectionStart => _selectionAnchor is { } anchor ? Math.Min(anchor, CaretIndex) : CaretIndex;

    /// <summary>One past the last character of the selected range.</summary>
    public int SelectionEnd => _selectionAnchor is { } anchor ? Math.Max(anchor, CaretIndex) : CaretIndex;

    /// <summary>Gets a value indicating whether a range of characters is selected.</summary>
    public bool HasSelection => SelectionEnd > SelectionStart;

    /// <summary>The selected characters, which is the empty string when there is no range.</summary>
    public string SelectedText =>
        HasSelection ? Layer.Text[SelectionStart..SelectionEnd] : string.Empty;

    /// <summary>
    /// The rectangle the rendered text occupies, in document coordinates.
    /// </summary>
    /// <remarks>
    /// The glyphs where there are any, and the caret where there are not. Text that has just
    /// been started has no pixels at all, and a box of no size around it would leave the artist
    /// with nothing on screen saying that they are in a text field.
    /// </remarks>
    public ImageRect TextBounds
    {
        get
        {
            if (Rasterizer.Rasterize(Layer.Text, Layer.Style) is { } raster)
            {
                return new ImageRect(
                    new ImagePoint(Layer.Origin.X + raster.Origin.X, Layer.Origin.Y + raster.Origin.Y),
                    raster.Pixels.Size);
            }

            return Rasterizer
                .MeasureCaret(Layer.Text, Layer.Style, 0)
                .Translate(Layer.Origin.X, Layer.Origin.Y);
        }
    }

    /// <summary>
    /// The rectangles to paint behind the selected characters, one per line of text.
    /// </summary>
    /// <returns>The highlight rectangles, in document coordinates; empty when nothing is selected.</returns>
    /// <remarks>
    /// Built from the caret measurements at each end of each line rather than from glyph
    /// extents, so the highlight agrees exactly with where clicking would put the caret. A
    /// highlight that disagreed with the caret by even a pixel is the kind of thing that makes
    /// a text field feel broken without the artist being able to say why.
    /// </remarks>
    public IReadOnlyList<ImageRect> GetSelectionRectangles()
    {
        if (!HasSelection)
        {
            return [];
        }

        string text = Layer.Text;
        List<ImageRect> rectangles = [];

        for (int index = SelectionStart; index <= SelectionEnd;)
        {
            int lineEnd = Math.Min(GetLineEnd(text, index), SelectionEnd);

            ImageRect from = Rasterizer.MeasureCaret(text, Layer.Style, index);
            ImageRect to = Rasterizer.MeasureCaret(text, Layer.Style, lineEnd);

            // An empty line inside a selected range still gets a sliver, so that a run of blank
            // lines reads as selected rather than as a gap in the highlight.
            int width = Math.Max(to.Left - from.Left, 2);

            rectangles.Add(new ImageRect(from.Left, from.Top, width, from.Height)
                .Translate(Layer.Origin.X, Layer.Origin.Y));

            if (lineEnd >= SelectionEnd)
            {
                break;
            }

            index = SkipLineBreak(text, lineEnd);
        }

        return rectangles;
    }

    /// <summary>
    /// How far outside the glyphs the frame around them is drawn, in image pixels.
    /// </summary>
    /// <remarks>
    /// Shared with the canvas so the frame an artist can see and the band they can grab are the
    /// same thing. A frame drawn further out than the grab band reaches is a line that visibly
    /// does nothing when pressed, which is worse than no frame at all.
    /// </remarks>
    public const int BoxInset = 2;

    /// <summary>
    /// Gets a value indicating whether a point is on the border of the text box rather than
    /// inside the words.
    /// </summary>
    /// <param name="point">The point, in document coordinates.</param>
    /// <param name="tolerance">How near the border still counts, in image pixels.</param>
    /// <returns><see langword="true"/> if a press there should move the block.</returns>
    /// <remarks>
    /// <para>
    /// This is what divides the two things a drag on text can mean. Inside the words a drag
    /// selects characters, because that is what dragging in a text field does everywhere. On the
    /// border it moves the whole block, because there is nothing else the border could be for
    /// and because an artist who wants to reposition their text should not have to change tool
    /// to do it.
    /// </para>
    /// <para>
    /// <b>The band lies wholly outside the glyphs</b>, in the ring from their edge out past the
    /// dashed frame drawn around them. A band that reached inwards as well would be reasonable on a
    /// paragraph and a disaster on a short word at 100% zoom, where six pixels of tolerance is
    /// the entire height of the text: every attempt to put the caret in it would move it
    /// instead. Outside-only means the words always belong to the caret and the frame always
    /// belongs to the drag, at every size and every zoom.
    /// </para>
    /// </remarks>
    public bool IsOnBoxEdge(ImagePoint point, int tolerance)
    {
        ImageRect box = TextBounds;

        return box.Inflate(BoxInset + Math.Max(1, tolerance)).Contains(point) && !box.Contains(point);
    }

    /// <summary>Selects every character, which is what Ctrl+A does inside text.</summary>
    public void SelectAll()
    {
        if (Layer.Text.Length == 0)
        {
            return;
        }

        _selectionAnchor = 0;
        CaretIndex = Layer.Text.Length;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drops any selected range, leaving the caret where it is.</summary>
    public void ClearSelection()
    {
        if (_selectionAnchor is null)
        {
            return;
        }

        _selectionAnchor = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes the selected characters, if any.</summary>
    /// <returns><see langword="true"/> if anything was removed.</returns>
    public bool DeleteSelection()
    {
        if (!HasSelection)
        {
            return false;
        }

        int start = SelectionStart;

        Layer.Text = Layer.Text.Remove(start, SelectionEnd - start);
        _selectionAnchor = null;
        CaretIndex = start;
        Refresh();
        return true;
    }

    /// <summary>
    /// The caret's rectangle in document coordinates, for the canvas to draw.
    /// </summary>
    public ImageRect CaretBounds
    {
        get
        {
            ImageRect local = _rasterizer.MeasureCaret(Layer.Text, Layer.Style, CaretIndex);
            return local.Translate(Layer.Origin.X, Layer.Origin.Y);
        }
    }

    /// <summary>Inserts text at the caret and moves the caret past it.</summary>
    /// <param name="text">The text to insert; may be several characters, as from a paste.</param>
    public void InsertText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        // Typing over a selected range replaces it, which is what a text field does everywhere
        // else on the machine and the whole reason for being able to select a range at all.
        int start = HasSelection ? SelectionStart : CaretIndex;

        if (HasSelection)
        {
            Layer.Text = Layer.Text.Remove(start, SelectionEnd - start);
            _selectionAnchor = null;
            CaretIndex = start;
        }

        Layer.Text = Layer.Text.Insert(start, text);
        CaretIndex = start + text.Length;
        Refresh();
    }

    /// <summary>Deletes the selected range, or the character before the caret.</summary>
    public void DeleteBackward()
    {
        if (DeleteSelection())
        {
            return;
        }

        if (CaretIndex <= 0)
        {
            return;
        }

        // A "\r\n" is one line break to the artist, so backspacing over it removes both.
        int removed = CaretIndex >= 2 && Layer.Text[CaretIndex - 2] == '\r' && Layer.Text[CaretIndex - 1] == '\n'
            ? 2
            : 1;

        Layer.Text = Layer.Text.Remove(CaretIndex - removed, removed);
        CaretIndex -= removed;
        Refresh();
    }

    /// <summary>Deletes the selected range, or the character after the caret.</summary>
    public void DeleteForward()
    {
        if (DeleteSelection())
        {
            return;
        }

        if (CaretIndex >= Layer.Text.Length)
        {
            return;
        }

        int removed = CaretIndex + 1 < Layer.Text.Length
            && Layer.Text[CaretIndex] == '\r'
            && Layer.Text[CaretIndex + 1] == '\n'
                ? 2
                : 1;

        Layer.Text = Layer.Text.Remove(CaretIndex, removed);
        Refresh();
    }

    /// <summary>Moves the caret.</summary>
    /// <param name="movement">Where to move it to.</param>
    /// <param name="extend">
    /// <see langword="true"/> to drag the selection along with it, which is what holding Shift
    /// does; <see langword="false"/> to drop any selected range and just move.
    /// </param>
    public void MoveCaret(CaretMovement movement, bool extend = false)
    {
        string text = Layer.Text;

        // Moving without Shift while a range is selected collapses to the end the caret is
        // travelling towards, rather than landing a character past it. That is the rule every
        // text field follows, and the one that makes arrowing out of a selection feel right.
        if (!extend && HasSelection)
        {
            bool backwards = movement
                is CaretMovement.Left or CaretMovement.Up
                or CaretMovement.LineStart or CaretMovement.TextStart;

            int collapsed = backwards ? SelectionStart : SelectionEnd;
            _selectionAnchor = null;
            CaretIndex = collapsed;

            if (movement is CaretMovement.Left or CaretMovement.Right)
            {
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }
        }

        BeginMove(extend);

        switch (movement)
        {
            case CaretMovement.Left:
                CaretIndex--;
                break;

            case CaretMovement.Right:
                CaretIndex++;
                break;

            case CaretMovement.LineStart:
                CaretIndex = GetLineStart(text, CaretIndex);
                break;

            case CaretMovement.LineEnd:
                CaretIndex = GetLineEnd(text, CaretIndex);
                break;

            case CaretMovement.TextStart:
                CaretIndex = 0;
                break;

            case CaretMovement.TextEnd:
                CaretIndex = text.Length;
                break;

            case CaretMovement.Up:
            case CaretMovement.Down:
                MoveCaretByLine(text, movement == CaretMovement.Down);
                break;

            default:
                break;
        }
    }

    /// <summary>Puts the caret nearest a point on the canvas.</summary>
    /// <param name="documentPoint">The point in document coordinates.</param>
    /// <param name="extend">
    /// <see langword="true"/> to select from wherever the caret already was, which is what
    /// dragging across the glyphs does.
    /// </param>
    public void MoveCaretTo(ImagePoint documentPoint, bool extend = false)
    {
        BeginMove(extend);

        ImagePoint local = new(documentPoint.X - Layer.Origin.X, documentPoint.Y - Layer.Origin.Y);
        CaretIndex = _rasterizer.HitTestCaret(Layer.Text, Layer.Style, local);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Prepares the anchor for a caret movement: dropped when the move replaces the selection,
    /// pinned to where the caret was when the move extends it.
    /// </summary>
    private void BeginMove(bool extend)
    {
        if (!extend)
        {
            _selectionAnchor = null;
            return;
        }

        _selectionAnchor ??= CaretIndex;
    }

    /// <summary>Moves the whole text block.</summary>
    /// <param name="origin">The new position of its top-left corner.</param>
    public void MoveOrigin(ImagePoint origin)
    {
        if (Layer.Origin == origin)
        {
            return;
        }

        Layer.Origin = origin;
        Refresh();
    }

    /// <summary>
    /// Re-renders the layer after something about it changed. Call this after editing the
    /// layer's <see cref="TextLayer.Style"/> directly, which is what the text panel does.
    /// </summary>
    public void Refresh()
    {
        TextLayerRenderer.Render(_workspace.Document, Layer, _frame, _rasterizer);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Ends the session, recording the whole episode as one undo entry.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if anything was recorded. An episode that left the layer
    /// exactly as it found it records nothing, which keeps clicking on a text layer and
    /// clicking away again out of the history.
    /// </returns>
    public bool Commit()
    {
        bool textChanged = !string.Equals(_originalText, Layer.Text, StringComparison.Ordinal);
        bool styleChanged = !_originalStyle.VisuallyEquals(Layer.Style);
        bool moved = _originalOrigin != Layer.Origin;

        // An empty new layer is a click that changed nothing. Removing it rather than
        // leaving it is the difference between a stray click costing nothing and it costing
        // an invisible layer the artist finds three days later.
        if (IsNewLayer && Layer.Text.Length == 0)
        {
            _workspace.Document.RemoveLayer(Layer);

            if (ReferenceEquals(_workspace.ActiveLayer, Layer))
            {
                _workspace.ActiveLayer = _workspace.Document.Root.Children.Count > 0
                    ? _workspace.Document.Root.Children[^1]
                    : null;
            }

            return false;
        }

        if (IsNewLayer)
        {
            // The layer and its glyphs are already in the document — the artist watched them
            // appear — so the command is recorded rather than executed.
            _workspace.History.Push(new AddLayerCommand(
                "Text",
                _workspace.Document,
                Layer,
                Layer.Parent ?? _workspace.Document.Root,
                Layer.Parent?.IndexOf(Layer) ?? -1));

            return true;
        }

        if (!textChanged && !styleChanged && !moved)
        {
            return false;
        }

        _workspace.History.Push(new SetTextCommand(
            "Edit Text",
            _workspace.Document,
            Layer,
            _frame,
            _rasterizer,
            _originalText,
            _originalStyle,
            _originalOrigin,
            Layer.Text,
            Layer.Style,
            Layer.Origin));

        return true;
    }

    /// <summary>
    /// Ends the session and puts the layer back exactly as it was found, removing it
    /// entirely if this session created it. This is what Escape does.
    /// </summary>
    public void Cancel()
    {
        if (IsNewLayer)
        {
            _workspace.Document.RemoveLayer(Layer);

            if (ReferenceEquals(_workspace.ActiveLayer, Layer))
            {
                _workspace.ActiveLayer = _workspace.Document.Root.Children.Count > 0
                    ? _workspace.Document.Root.Children[^1]
                    : null;
            }

            return;
        }

        Layer.Text = _originalText;
        Layer.Style.CopyFrom(_originalStyle);
        Layer.Origin = _originalOrigin;
        TextLayerRenderer.Render(_workspace.Document, Layer, _frame, _rasterizer);
    }

    private void MoveCaretByLine(string text, bool down)
    {
        int lineStart = GetLineStart(text, CaretIndex);
        int column = CaretIndex - lineStart;

        if (down)
        {
            int lineEnd = GetLineEnd(text, CaretIndex);
            if (lineEnd >= text.Length)
            {
                CaretIndex = text.Length;
                return;
            }

            int nextStart = SkipLineBreak(text, lineEnd);
            CaretIndex = Math.Min(nextStart + column, GetLineEnd(text, nextStart));
            return;
        }

        if (lineStart == 0)
        {
            CaretIndex = 0;
            return;
        }

        int previousEnd = lineStart - 1;
        if (previousEnd > 0 && text[previousEnd] == '\n' && text[previousEnd - 1] == '\r')
        {
            previousEnd--;
        }

        int previousStart = GetLineStart(text, previousEnd);
        CaretIndex = Math.Min(previousStart + column, previousEnd);
    }

    private static int GetLineStart(string text, int index)
    {
        for (int i = Math.Min(index, text.Length) - 1; i >= 0; i--)
        {
            if (text[i] is '\n' or '\r')
            {
                return i + 1;
            }
        }

        return 0;
    }

    private static int GetLineEnd(string text, int index)
    {
        for (int i = Math.Max(0, index); i < text.Length; i++)
        {
            if (text[i] is '\n' or '\r')
            {
                return i;
            }
        }

        return text.Length;
    }

    private static int SkipLineBreak(string text, int index)
    {
        if (index < text.Length && text[index] == '\r')
        {
            index++;
        }

        if (index < text.Length && text[index] == '\n')
        {
            index++;
        }

        return index;
    }
}
