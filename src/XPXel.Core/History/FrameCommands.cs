using XPXel.Core.Documents;

namespace XPXel.Core.History;

/// <summary>
/// Adds a frame to the timeline, and takes it out again on undo.
/// </summary>
/// <remarks>
/// The command carries the frame's cels for the same reason
/// <see cref="AddLayerCommand"/> does: duplicating a frame produces one that already has
/// artwork on every layer, and undoing then redoing the addition must bring that artwork
/// back rather than returning an empty frame.
/// </remarks>
public sealed class AddFrameCommand : IUndoableCommand
{
    private readonly PixelDocument _document;
    private readonly Frame _frame;
    private readonly int _index;

    private IReadOnlyList<Cel> _cels;

    /// <summary>Initialises the command.</summary>
    /// <param name="name">The action name shown in the Edit menu.</param>
    /// <param name="document">The document to add to.</param>
    /// <param name="frame">The frame to add.</param>
    /// <param name="index">The position in the timeline.</param>
    /// <param name="cels">
    /// Any cels belonging to the frame that should be restored alongside it. They must
    /// already have been removed from the document.
    /// </param>
    public AddFrameCommand(
        string name,
        PixelDocument document,
        Frame frame,
        int index,
        IEnumerable<Cel>? cels = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(frame);

        Name = name;
        _document = document;
        _frame = frame;
        _index = index;
        _cels = cels is null ? [] : [.. cels];
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>The frame this command adds.</summary>
    public Frame Frame => _frame;

    /// <inheritdoc />
    public void Redo()
    {
        _document.Cels.RestoreRange(_cels);
        _document.InsertFrame(_frame, _index);
    }

    /// <inheritdoc />
    public void Undo() => _cels = _document.RemoveFrame(_frame) ?? [];
}

/// <summary>
/// Removes a frame and its artwork, and puts both back on undo.
/// </summary>
public sealed class RemoveFrameCommand : IUndoableCommand
{
    private readonly PixelDocument _document;
    private readonly Frame _frame;
    private readonly int _index;

    private IReadOnlyList<Cel> _cels = [];

    /// <summary>Initialises the command.</summary>
    /// <param name="name">The action name shown in the Edit menu.</param>
    /// <param name="document">The document to remove from.</param>
    /// <param name="frame">The frame to remove.</param>
    public RemoveFrameCommand(string name, PixelDocument document, Frame frame)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(frame);

        Name = name;
        _document = document;
        _frame = frame;
        _index = document.IndexOfFrame(frame);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public void Redo() => _cels = _document.RemoveFrame(_frame) ?? [];

    /// <inheritdoc />
    public void Undo()
    {
        _document.Cels.RestoreRange(_cels);
        _document.InsertFrame(_frame, _index);
    }
}

/// <summary>Moves a frame to a different position in the timeline.</summary>
public sealed class MoveFrameCommand : IUndoableCommand
{
    private readonly PixelDocument _document;
    private readonly int _oldIndex;
    private readonly int _newIndex;

    /// <summary>Initialises the command.</summary>
    /// <param name="name">The action name shown in the Edit menu.</param>
    /// <param name="document">The document whose timeline is being reordered.</param>
    /// <param name="oldIndex">The frame's current position.</param>
    /// <param name="newIndex">Where it should end up.</param>
    public MoveFrameCommand(string name, PixelDocument document, int oldIndex, int newIndex)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(document);

        Name = name;
        _document = document;
        _oldIndex = oldIndex;
        _newIndex = newIndex;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public void Redo() => _document.MoveFrame(_oldIndex, _newIndex);

    /// <inheritdoc />
    public void Undo() => _document.MoveFrame(_newIndex, _oldIndex);
}

/// <summary>
/// Changes how long one or more frames are held.
/// </summary>
/// <remarks>
/// Several frames at once, because "set the whole animation to 12 fps" is the common case
/// and an artist expects one undo to take it back, not one per frame.
/// </remarks>
public sealed class SetFrameDurationsCommand : IUndoableCommand
{
    private readonly (Frame Frame, int OldValue, int NewValue)[] _changes;

    /// <summary>Initialises the command.</summary>
    /// <param name="name">The action name shown in the Edit menu.</param>
    /// <param name="changes">The frames to retime, with their old and new hold times.</param>
    public SetFrameDurationsCommand(string name, IEnumerable<(Frame Frame, int OldValue, int NewValue)> changes)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(changes);

        Name = name;
        _changes = [.. changes];
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>Gets a value indicating whether the command would actually change anything.</summary>
    public bool HasChanges => _changes.Any(c => c.OldValue != c.NewValue);

    /// <inheritdoc />
    public void Redo()
    {
        foreach ((Frame frame, _, int newValue) in _changes)
        {
            frame.DurationMilliseconds = newValue;
        }
    }

    /// <inheritdoc />
    public void Undo()
    {
        foreach ((Frame frame, int oldValue, _) in _changes)
        {
            frame.DurationMilliseconds = oldValue;
        }
    }
}
