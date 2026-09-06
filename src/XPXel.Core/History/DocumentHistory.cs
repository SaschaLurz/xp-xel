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

    /// <summary>The smallest capacity that still leaves undo worth having.</summary>
    public const int MinimumCapacity = 1;

    /// <summary>
    /// The largest capacity that may be set.
    /// </summary>
    /// <remarks>
    /// A ceiling rather than a recommendation. Each raster entry holds a copy of the pixels it
    /// changed, so a thousand full-canvas fills on a large document is real memory; the limit
    /// stops a typed number turning into an out-of-memory crash three hours into a session.
    /// </remarks>
    public const int MaximumCapacity = 5000;

    private readonly List<IUndoableCommand> _commands = [];
    private int _capacity;
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
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, MinimumCapacity);
        _capacity = Math.Min(capacity, MaximumCapacity);
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
    /// How many entries are kept before the oldest are discarded.
    /// </summary>
    /// <remarks>
    /// Settable, because how far back an artist wants to be able to go is a judgement about
    /// their machine and their working style rather than something this class can know.
    /// Lowering it trims immediately: a limit that only applied to future entries would leave
    /// the artist wondering why the memory they were trying to reclaim had not come back.
    /// </remarks>
    public int Capacity
    {
        get => _capacity;
        set
        {
            int clamped = Math.Clamp(value, MinimumCapacity, MaximumCapacity);

            if (clamped == _capacity)
            {
                return;
            }

            _capacity = clamped;

            if (TrimToCapacity())
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// The recorded actions, oldest first. Entries before <see cref="Position"/> have been
    /// applied; entries from it onwards have been undone.
    /// </summary>
    public IReadOnlyList<IUndoableCommand> Commands => _commands;

    /// <summary>
    /// How many entries have been applied, which is where the cursor sits in
    /// <see cref="Commands"/>.
    /// </summary>
    public int Position => _cursor;

    /// <summary>
    /// Winds the document to a given position in the history, undoing or redoing as needed.
    /// </summary>
    /// <param name="position">
    /// How many entries should be applied. Zero is the state before the oldest retained entry.
    /// </param>
    /// <returns><see langword="true"/> if anything moved.</returns>
    /// <remarks>
    /// This is what makes a history panel a navigator rather than a list: clicking an entry
    /// three steps back is Ctrl+Z three times, and the artist should not have to count. The
    /// steps are replayed one at a time through the same <see cref="IUndoableCommand"/> calls
    /// as undo and redo, so nothing here can get out of step with them.
    /// </remarks>
    public bool MoveTo(int position)
    {
        int target = Math.Clamp(position, 0, _commands.Count);

        if (target == _cursor)
        {
            return false;
        }

        while (_cursor > target)
        {
            _cursor--;
            _commands[_cursor].Undo();
        }

        while (_cursor < target)
        {
            _commands[_cursor].Redo();
            _cursor++;
        }

        Revision++;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Gets a value indicating whether the document is in the same state it was in when
    /// <see cref="MarkSaved"/> was last called.
    /// </summary>
    public bool IsAtSavedState => _cursor == _savedCursor;

    /// <summary>
    /// A counter that moves whenever the history does, in either direction.
    /// </summary>
    /// <remarks>
    /// Exists for autosave, which needs to tell "nothing has happened since the last backup"
    /// from "something happened and was undone". Both leave the document looking the same, and
    /// only the first means there is nothing worth writing. Deliberately a counter rather than
    /// a hash of the document: this is read on a timer and has to be free.
    /// </remarks>
    public int Revision { get; private set; }

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
        Revision++;
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
        Revision++;
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
        Revision++;
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
        Revision++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Discards the oldest entries beyond the capacity.</summary>
    /// <returns><see langword="true"/> if anything was discarded.</returns>
    /// <remarks>
    /// Only entries that have been applied are dropped. Trimming from the front never touches
    /// anything the artist could still redo, because those sit at the far end of the list.
    /// </remarks>
    private bool TrimToCapacity()
    {
        int excess = _commands.Count - _capacity;
        if (excess <= 0)
        {
            return false;
        }

        // Never trim past the cursor: doing so would drop entries that have been undone and
        // are still waiting to be redone, and the artist would watch their redo stack vanish.
        excess = Math.Min(excess, _cursor);

        if (excess <= 0)
        {
            return false;
        }

        _commands.RemoveRange(0, excess);
        _cursor -= excess;

        // Once the saved position falls off the front we can no longer prove the document
        // matches what is on disk, so report it as modified from here on.
        _savedCursor = _savedCursor >= excess ? _savedCursor - excess : -1;
        return true;
    }
}
