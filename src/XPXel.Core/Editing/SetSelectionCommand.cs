using XPXel.Core.History;
using XPXel.Core.Selection;

namespace XPXel.Core.Editing;

/// <summary>
/// Replaces the active selection, so that a change to it can be undone along with whatever
/// else the same action did.
/// </summary>
/// <remarks>
/// <para>
/// Selecting on its own is deliberately <em>not</em> recorded in the history: an artist who
/// draws a marquee, changes their mind and presses Ctrl+Z expects their last brush stroke back,
/// not their previous marquee. This command exists for the case where the selection moves as
/// part of an edit that <em>is</em> undoable — moving the selected pixels, above all — where
/// leaving the marquee behind after an undo would strand it away from the artwork it belongs
/// to.
/// </para>
/// <para>
/// It is therefore always a child of a <see cref="CompositeCommand"/> rather than a history
/// entry in its own right.
/// </para>
/// </remarks>
public sealed class SetSelectionCommand : IUndoableCommand
{
    private readonly DocumentWorkspace _workspace;
    private readonly SelectionMask? _before;
    private readonly SelectionMask? _after;

    /// <summary>Initialises the command.</summary>
    /// <param name="workspace">The document whose selection changes.</param>
    /// <param name="before">The selection to restore on undo.</param>
    /// <param name="after">The selection to apply.</param>
    /// <param name="name">The action name for the Edit menu.</param>
    public SetSelectionCommand(
        DocumentWorkspace workspace,
        SelectionMask? before,
        SelectionMask? after,
        string name = "Change Selection")
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrEmpty(name);

        _workspace = workspace;
        _before = before;
        _after = after;
        Name = name;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public void Redo() => _workspace.Selection = _after;

    /// <inheritdoc />
    public void Undo() => _workspace.Selection = _before;
}
