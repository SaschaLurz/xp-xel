namespace XPXel.Core.History;

/// <summary>
/// A single reversible change to a document — one entry in the undo history.
/// </summary>
/// <remarks>
/// <para>
/// <b>One user action, one command.</b> A pencil stroke from mouse-down to mouse-up is a
/// single command, not one per pixel. If an operation genuinely consists of several
/// distinct changes, wrap them in a <see cref="CompositeCommand"/> so the artist still
/// undoes it in one keystroke.
/// </para>
/// <para>
/// <b>Implementations must be idempotent under alternation.</b> The history stack may call
/// <see cref="Undo"/> and <see cref="Redo"/> any number of times in strict alternation, so a
/// command has to hold enough state to restore both directions, and must not assume it runs
/// only once.
/// </para>
/// <para>
/// <b>Commands must not raise change notifications themselves</b> beyond what the document
/// methods they call already raise. The history stack is what the UI listens to for
/// "something was undone"; duplicating that here leads to double redraws.
/// </para>
/// </remarks>
public interface IUndoableCommand
{
    /// <summary>
    /// A short human-readable description of the action, shown in the Edit menu as
    /// "Undo <em>Name</em>". Use sentence case and name the action, for example
    /// "Pencil", "Delete Layer" or "Change Opacity".
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Applies the change. Called by <see cref="DocumentHistory.Execute"/> the first time and
    /// again on every redo.
    /// </summary>
    void Redo();

    /// <summary>Reverses the change, restoring the state from before <see cref="Redo"/>.</summary>
    void Undo();
}
