using XPXel.Core.Documents;

namespace XPXel.Core.History;

/// <summary>
/// Switches a layer between holding one drawing for the whole timeline and holding a separate
/// drawing per frame.
/// </summary>
/// <remarks>
/// <para>
/// Turning it on collapses every frame's drawing into one, which throws the rest away — so the
/// displaced cels are kept here and put back on undo. Turning it off is lossless: each frame
/// gets a copy of the shared drawing.
/// </para>
/// <para>
/// See <see cref="LayerNode.IsContinuous"/> for what the flag means and why a white background
/// is the thing it exists for.
/// </para>
/// </remarks>
public sealed class SetLayerContinuousCommand : IUndoableCommand
{
    private readonly PixelDocument _document;
    private readonly LayerNode _layer;
    private readonly bool _continuous;
    private readonly Frame? _keepFrom;
    private readonly bool _wasContinuous;

    private IReadOnlyList<Cel> _before = [];

    /// <summary>Initialises the command.</summary>
    /// <param name="name">The action name shown in the Edit menu.</param>
    /// <param name="document">The document being edited.</param>
    /// <param name="layer">The layer to change.</param>
    /// <param name="continuous">Whether it should hold one drawing for every frame.</param>
    /// <param name="keepFrom">Whose drawing survives when frames are collapsed into one.</param>
    public SetLayerContinuousCommand(
        string name,
        PixelDocument document,
        LayerNode layer,
        bool continuous,
        Frame? keepFrom = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layer);

        Name = name;
        _document = document;
        _layer = layer;
        _continuous = continuous;
        _keepFrom = keepFrom;
        _wasContinuous = layer.IsContinuous;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>Gets a value indicating whether the command would actually change anything.</summary>
    public bool HasChanges => _continuous != _wasContinuous;

    /// <inheritdoc />
    public void Redo() => _before = _document.SetLayerContinuous(_layer, _continuous, _keepFrom);

    /// <inheritdoc />
    public void Undo() => _document.RestoreLayerContinuity(_layer, _wasContinuous, _before);
}

/// <summary>
/// Copies one frame's drawing on one layer onto every other frame, as independent copies.
/// </summary>
/// <remarks>
/// <para>
/// The one-shot answer to "I want this on every frame". Unlike
/// <see cref="SetLayerContinuousCommand"/> the frames stay independent afterwards, so the copy
/// on frame four can be changed without touching frame one — which is what an artist wants when
/// they are spreading a starting point rather than fixing a background.
/// </para>
/// <para>
/// Every displaced cel is kept, because this overwrites whatever those frames held.
/// </para>
/// </remarks>
public sealed class CopyCelToAllFramesCommand : IUndoableCommand
{
    private readonly PixelDocument _document;
    private readonly LayerNode _layer;
    private readonly Frame _source;

    private IReadOnlyList<Cel> _before = [];

    /// <summary>Initialises the command.</summary>
    /// <param name="name">The action name shown in the Edit menu.</param>
    /// <param name="document">The document being edited.</param>
    /// <param name="layer">The layer whose drawing is being spread.</param>
    /// <param name="source">The frame to copy from.</param>
    public CopyCelToAllFramesCommand(string name, PixelDocument document, LayerNode layer, Frame source)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(source);

        Name = name;
        _document = document;
        _layer = layer;
        _source = source;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>
    /// Gets a value indicating whether there is anything to copy and anywhere to copy it to.
    /// </summary>
    public bool HasChanges =>
        !_layer.IsContinuous
        && _document.Frames.Count > 1
        && _document.Cels.Find(_layer.Id, _source.Id) is not null;

    /// <inheritdoc />
    public void Redo()
    {
        if (_document.Cels.Find(_layer.Id, _source.Id) is not { } source)
        {
            return;
        }

        List<Cel> displaced = [];

        foreach (Frame frame in _document.Frames)
        {
            if (frame.Id == _source.Id)
            {
                continue;
            }

            if (_document.Cels.Find(_layer.Id, frame.Id) is { } existing)
            {
                displaced.Add(existing);
            }

            // A copy per frame rather than the same object: these are meant to be able to
            // diverge, which is the whole difference between this and a continuous layer.
            _document.Cels.Set(source.CopyTo(_layer.Id, frame.Id));
        }

        _before = displaced;
        _document.NotifyStructureChanged();
    }

    /// <inheritdoc />
    public void Undo()
    {
        foreach (Frame frame in _document.Frames)
        {
            if (frame.Id != _source.Id)
            {
                _document.Cels.Remove(_layer.Id, frame.Id);
            }
        }

        _document.Cels.RestoreRange(_before);
        _before = [];
        _document.NotifyStructureChanged();
    }
}

/// <summary>Clears one layer's drawing on one frame.</summary>
/// <remarks>
/// Removes the cel outright rather than filling it with transparency, because "this layer has
/// nothing on this frame" is a state the document already has a representation for — a missing
/// cel — and the timeline shows it as an empty box.
/// </remarks>
public sealed class ClearCelCommand : IUndoableCommand
{
    private readonly PixelDocument _document;
    private readonly LayerNode _layer;
    private readonly Frame _frame;

    private Cel? _before;

    /// <summary>Initialises the command.</summary>
    /// <param name="name">The action name shown in the Edit menu.</param>
    /// <param name="document">The document being edited.</param>
    /// <param name="layer">The layer to clear.</param>
    /// <param name="frame">The frame to clear it on.</param>
    public ClearCelCommand(string name, PixelDocument document, LayerNode layer, Frame frame)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(frame);

        Name = name;
        _document = document;
        _layer = layer;
        _frame = frame;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>Gets a value indicating whether there is anything on this frame to clear.</summary>
    public bool HasChanges => _document.Cels.Find(_layer.Id, _frame.Id) is not null;

    /// <inheritdoc />
    public void Redo()
    {
        _before = _document.Cels.Remove(_layer.Id, _frame.Id);
        _document.NotifyStructureChanged();
    }

    /// <inheritdoc />
    public void Undo()
    {
        if (_before is { } cel)
        {
            _document.Cels.Set(cel);
            _before = null;
        }

        _document.NotifyStructureChanged();
    }
}

/// <summary>Puts a copy of one layer's drawing onto another frame of the same layer.</summary>
/// <remarks>
/// What "copy this cel, go to frame four, paste it" does. The destination's previous content is
/// kept so that an undo puts it back.
/// </remarks>
public sealed class SetCelCommand : IUndoableCommand
{
    private readonly PixelDocument _document;
    private readonly LayerNode _layer;
    private readonly Frame _frame;
    private readonly Cel? _after;

    private Cel? _before;

    /// <summary>Initialises the command.</summary>
    /// <param name="name">The action name shown in the Edit menu.</param>
    /// <param name="document">The document being edited.</param>
    /// <param name="layer">The layer to write to.</param>
    /// <param name="frame">The frame to write to.</param>
    /// <param name="content">
    /// The drawing to put there, which is copied under the destination's identity, or
    /// <see langword="null"/> to leave the frame empty.
    /// </param>
    public SetCelCommand(string name, PixelDocument document, LayerNode layer, Frame frame, Cel? content)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(frame);

        Name = name;
        _document = document;
        _layer = layer;
        _frame = frame;
        _after = content?.CopyTo(layer.Id, frame.Id);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>Gets a value indicating whether there is anything to put down.</summary>
    public bool HasChanges => _after is not null;

    /// <inheritdoc />
    public void Redo()
    {
        _before = _document.Cels.Remove(_layer.Id, _frame.Id);

        if (_after is { } cel)
        {
            _document.Cels.Set(cel);
        }

        _document.NotifyStructureChanged();
    }

    /// <inheritdoc />
    public void Undo()
    {
        _document.Cels.Remove(_layer.Id, _frame.Id);

        if (_before is { } cel)
        {
            _document.Cels.Set(cel);
            _before = null;
        }

        _document.NotifyStructureChanged();
    }
}
