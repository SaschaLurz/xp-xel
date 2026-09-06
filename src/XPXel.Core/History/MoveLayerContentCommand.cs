using XPXel.Core.Documents;
using XPXel.Core.Primitives;

namespace XPXel.Core.History;

/// <summary>
/// Slides everything one layer holds on one frame to a new place on the canvas, keeping
/// whatever ends up outside it.
/// </summary>
/// <remarks>
/// <para>
/// <b>What leaves the canvas is not thrown away.</b> The cel's buffer grows to cover both the
/// canvas and its own painted pixels, so an arm pushed off the left edge is still there, simply
/// not visible — exactly as a shape dragged off a slide is still on the slide. Dragging the
/// layer back, growing the canvas or opening the crop tool all bring it back. See
/// <see cref="Cel.CreateMoved"/>, which is where that arithmetic lives.
/// </para>
/// <para>
/// <b>One frame, not the whole layer.</b> Moving a walk cycle's leg on frame four and leaving
/// frames one to three where they are is ordinary animation; a command that moved every frame at
/// once would make that impossible, while moving one frame at a time is merely repetitive.
/// </para>
/// <para>
/// <b>Text moves twice.</b> A text layer's glyphs live in a cel like anything else, but its
/// <see cref="TextLayer.Origin"/> is where the next re-render will put them — so moving only the
/// pixels would leave the text to jump back the moment its style was touched, long after the
/// move, which is the hardest kind of bug to connect to its cause.
/// </para>
/// </remarks>
public sealed class MoveLayerContentCommand : IUndoableCommand
{
    private readonly PixelDocument _document;
    private readonly LayerNode _layer;
    private readonly Frame _frame;
    private readonly ImagePoint _delta;

    private Cel? _before;

    /// <summary>Initialises the command.</summary>
    /// <param name="name">The action name shown in the Edit menu.</param>
    /// <param name="document">The document being edited.</param>
    /// <param name="layer">The layer whose content moves.</param>
    /// <param name="frame">The frame whose cel moves.</param>
    /// <param name="delta">How far to move it, in document coordinates.</param>
    public MoveLayerContentCommand(
        string name,
        PixelDocument document,
        LayerNode layer,
        Frame frame,
        ImagePoint delta)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(frame);

        Name = name;
        _document = document;
        _layer = layer;
        _frame = frame;
        _delta = delta;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>Gets a value indicating whether the command would actually change anything.</summary>
    public bool HasChanges => _delta != ImagePoint.Origin;

    /// <inheritdoc />
    public void Redo()
    {
        if (_document.Cels.Find(_layer.Id, _frame.Id) is { } cel)
        {
            _before = cel;
            _document.Cels.Set(cel.CreateMoved(_delta, _document.Bounds));
        }

        if (_layer is TextLayer text)
        {
            text.Origin = text.Origin.Translate(_delta);
        }

        _document.NotifyPixelsChanged(_document.Bounds);
    }

    /// <inheritdoc />
    public void Undo()
    {
        if (_before is { } cel)
        {
            // The original cel object is put back, not a re-shifted copy: an earlier stroke's
            // undo entry holds a reference to that exact cel, and history is linear, so by the
            // time it is undone this has already run.
            _document.Cels.Set(cel);
        }

        if (_layer is TextLayer text)
        {
            text.Origin = text.Origin.Translate(-_delta.X, -_delta.Y);
        }

        _document.NotifyPixelsChanged(_document.Bounds);
    }
}
