using XPXel.Core.Documents;
using XPXel.Core.Primitives;
using XPXel.Core.Text;

namespace XPXel.Core.History;

/// <summary>
/// Changes what a <see cref="TextLayer"/> says, how it is styled, or where it sits, and puts
/// all three back on undo.
/// </summary>
/// <remarks>
/// <para>
/// <b>No pixels are stored.</b> Rendering a text layer is a pure function of its text, style
/// and origin, so restoring those three values and re-rendering restores the pixels exactly.
/// That makes this command a few dozen bytes regardless of how large the text is, and it
/// keeps the layer's words and its glyphs from ever drifting apart in the history — which is
/// the failure mode a pixel-storing version would eventually hit.
/// </para>
/// <para>
/// The style is copied on the way in, in both directions. Storing the live
/// <see cref="TextStyle"/> instance would mean the command's "before" value changed every
/// time the artist moved a slider afterwards.
/// </para>
/// </remarks>
public sealed class SetTextCommand : IUndoableCommand
{
    private readonly PixelDocument _document;
    private readonly TextLayer _layer;
    private readonly Frame _frame;
    private readonly ITextRasterizer _rasterizer;

    private readonly string _oldText;
    private readonly TextStyle _oldStyle;
    private readonly ImagePoint _oldOrigin;

    private readonly string _newText;
    private readonly TextStyle _newStyle;
    private readonly ImagePoint _newOrigin;

    /// <summary>Initialises the command.</summary>
    /// <param name="name">The action name shown in the Edit menu.</param>
    /// <param name="document">The document holding the layer.</param>
    /// <param name="layer">The text layer being changed.</param>
    /// <param name="frame">The frame whose cel holds the rendered glyphs.</param>
    /// <param name="rasterizer">The font engine used to re-render.</param>
    /// <param name="oldText">The text to restore on undo.</param>
    /// <param name="oldStyle">The style to restore on undo.</param>
    /// <param name="oldOrigin">The position to restore on undo.</param>
    /// <param name="newText">The text to apply.</param>
    /// <param name="newStyle">The style to apply.</param>
    /// <param name="newOrigin">The position to apply.</param>
    public SetTextCommand(
        string name,
        PixelDocument document,
        TextLayer layer,
        Frame frame,
        ITextRasterizer rasterizer,
        string oldText,
        TextStyle oldStyle,
        ImagePoint oldOrigin,
        string newText,
        TextStyle newStyle,
        ImagePoint newOrigin)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(rasterizer);
        ArgumentNullException.ThrowIfNull(oldStyle);
        ArgumentNullException.ThrowIfNull(newStyle);

        Name = name;
        _document = document;
        _layer = layer;
        _frame = frame;
        _rasterizer = rasterizer;

        _oldText = oldText ?? string.Empty;
        _oldStyle = oldStyle.Clone();
        _oldOrigin = oldOrigin;

        _newText = newText ?? string.Empty;
        _newStyle = newStyle.Clone();
        _newOrigin = newOrigin;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>The layer this command changes.</summary>
    public TextLayer Layer => _layer;

    /// <inheritdoc />
    public void Redo() => Apply(_newText, _newStyle, _newOrigin);

    /// <inheritdoc />
    public void Undo() => Apply(_oldText, _oldStyle, _oldOrigin);

    private void Apply(string text, TextStyle style, ImagePoint origin)
    {
        _layer.Text = text;
        _layer.Style.CopyFrom(style);
        _layer.Origin = origin;

        TextLayerRenderer.Render(_document, _layer, _frame, _rasterizer);
    }
}
