namespace XPXel.Core.History;

/// <summary>
/// The undo and redo history for one document.
/// </summary>
/// <remarks>
/// <para>
/// <b>Structure.</b> Rather than two stacks, the history is one list plus a cursor. Entries
/// before the cursor have been applied; entries at or after it have been undone and can be
/// redone. Performing a new action while entries sit ahead of the cursor discards them,
/// which is the behaviour every editor has.
/// </para>
/// <para>
/// <b>Two ways to record a command.</b> <see cref="Execute"/> applies the command and then
/// records it — the right choice for menu actions. <see cref="Push"/> records a command
/// whose effect has <em>already</em> happened, which is what drawing tools need: the artist
/// watches a stroke appear as they draw it, and only at mouse-up does the completed stroke
/// become a history entry.
/// </para>
/// <para>
/// <b>Saved-state tracking.</b> The stack remembers which position corresponds to the last
/// save, so "are there unsaved changes?" stays correct even when the artist undoes back to
/// exactly what is on disk. If that position is trimmed away by the capacity limit, the
/// document is conservatively reported as modified.
/// </para>
/// </remarks>
public sealed class DocumentHistory
{
    /// <summary>The number of history entries retained by default.</summary>
    public const int DefaultCapacity = 250;

    private readonly List<IUndoableCommand> _commands = [];
    private readonly int _capacity;
    private int _cursor;
    private int _savedCursor;

    /// <summary>Initialises an empty history.</summary>
    /// <param name="capacity">
    /// The maximum number of entries to keep. Older entries are discarded once the limit is
    /// reached; the limit exists because each raster edit retains a copy of the pixels it
    /// changed, so an unbounded history would grow without limit during a long session.
    /// </param>
    public DocumentHistory(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    /// <summary>
    /// Raised after any change to the history: a command was recorded, undone, redone, or
    /// the history was cleared. The UI uses this to refresh menu state and repaint.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>Gets a value indicating whether there is anything to undo.</summary>
    public bool CanUndo => _cursor > 0;

    /// <summary>Gets a value indicating whether there is anything to redo.</summary>
    public bool CanRedo => _cursor < _commands.Count;

    /// <summary>The name of the action that <see cref="Undo"/> would reverse, if any.</summary>
    public string? UndoName => CanUndo ? _commands[_cursor - 1].Name : null;

    /// <summary>The name of the action that <see cref="Redo"/> would reapply, if any.</summary>
    public string? RedoName => CanRedo ? _commands[_cursor].Name : null;

    /// <summary>The number of entries currently retained, applied and undone together.</summary>
    public int Count => _commands.Count;

    /// <summary>
    /// Gets a value indicating whether the document is in the same state it was in when
    /// <see cref="MarkSaved"/> was last called.
    /// </summary>
    public bool IsAtSavedState => _cursor == _savedCursor;

    /// <summary>Applies a command and records it in the history.</summary>
    /// <param name="command">The command to apply.</param>
    public void Execute(IUndoableCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        command.Redo();
        Push(command);
    }

    /// <summary>
    /// Records a command whose effect has already been applied, without invoking it.
    /// </summary>
    /// <param name="command">The already-applied command.</param>
    public void Push(IUndoableCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Anything the artist had undone is now unreachable and is dropped.
        if (_cursor < _commands.Count)
        {
            _commands.RemoveRange(_cursor, _commands.Count - _cursor);

            // The saved position can only be ahead of the cursor if it was in the discarded
            // range, in which case that exact document state is no longer reachable.
            if (_savedCursor > _cursor)
            {
                _savedCursor = -1;
            }
        }

        _commands.Add(command);
        _cursor = _commands.Count;

        TrimToCapacity();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reverses the most recent command, if there is one.</summary>
    /// <returns><see langword="true"/> if a command was undone.</returns>
    public bool Undo()
    {
        if (!CanUndo)
        {
            return false;
        }

        _cursor--;
        _commands[_cursor].Undo();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Reapplies the most recently undone command, if there is one.</summary>
    /// <returns><see langword="true"/> if a command was redone.</returns>
    public bool Redo()
    {
        if (!CanRedo)
        {
            return false;
        }

        _commands[_cursor].Redo();
        _cursor++;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Records that the document has just been saved, so that returning to this exact
    /// position counts as having no unsaved changes.
    /// </summary>
    public void MarkSaved()
    {
        _savedCursor = _cursor;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Discards the entire history. Used when a document is loaded or closed.</summary>
    public void Clear()
    {
        _commands.Clear();
        _cursor = 0;
        _savedCursor = 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void TrimToCapacity()
    {
        int excess = _commands.Count - _capacity;
        if (excess <= 0)
        {
            return;
        }

        _commands.RemoveRange(0, excess);
        _cursor -= excess;

        // Once the saved position falls off the front we can no longer prove the document
        // matches what is on disk, so report it as modified from here on.
        _savedCursor = _savedCursor >= excess ? _savedCursor - excess : -1;
    }
}
