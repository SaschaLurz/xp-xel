namespace XPXel.Core.History;

/// <summary>
/// Groups several commands so that they are undone and redone as a single history entry.
/// </summary>
/// <remarks>
/// <para>
/// Use this whenever one user action decomposes into several model changes — for example
/// "merge down", which erases one layer, writes pixels into another and deletes the first.
/// The artist pressing Ctrl+Z once must get all of it back.
/// </para>
/// <para>
/// Undo runs the children in reverse order, which is required for correctness: a later
/// command may depend on state a earlier one established.
/// </para>
/// </remarks>
public sealed class CompositeCommand : IUndoableCommand
{
    private readonly IUndoableCommand[] _commands;

    /// <summary>Initialises a composite from a sequence of commands.</summary>
    /// <param name="name">The name shown in the Edit menu for the combined action.</param>
    /// <param name="commands">
    /// The child commands, in the order they should be applied.
    /// </param>
    public CompositeCommand(string name, IEnumerable<IUndoableCommand> commands)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(commands);

        Name = name;
        _commands = [.. commands];
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>The child commands, in application order.</summary>
    public IReadOnlyList<IUndoableCommand> Commands => _commands;

    /// <inheritdoc />
    public void Redo()
    {
        foreach (IUndoableCommand command in _commands)
        {
            command.Redo();
        }
    }

    /// <inheritdoc />
    public void Undo()
    {
        for (int i = _commands.Length - 1; i >= 0; i--)
        {
            _commands[i].Undo();
        }
    }
}
