namespace XPXel.Core.Tools;

/// <summary>
/// The set of tools available in the application, in toolbox order.
/// </summary>
/// <remarks>
/// <para>
/// The registry is constructed from whatever <see cref="ITool"/> implementations the
/// dependency injection container knows about, so registering a tool is the only step
/// needed to make it appear in the UI. Ordering follows registration order, which is the
/// order the composition root lists them in — keeping the toolbox layout a deliberate
/// decision in one readable place rather than an accident of reflection.
/// </para>
/// </remarks>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, ITool> _byId;
    private readonly ITool[] _tools;

    /// <summary>Initialises the registry.</summary>
    /// <param name="tools">The available tools, in the order they should be listed.</param>
    /// <exception cref="ArgumentException">Two tools share an identifier.</exception>
    public ToolRegistry(IEnumerable<ITool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        _tools = [.. tools];
        _byId = new Dictionary<string, ITool>(_tools.Length, StringComparer.Ordinal);

        foreach (ITool tool in _tools)
        {
            if (!_byId.TryAdd(tool.Id, tool))
            {
                throw new ArgumentException($"More than one tool uses the identifier '{tool.Id}'.", nameof(tools));
            }
        }
    }

    /// <summary>The available tools, in toolbox order.</summary>
    public IReadOnlyList<ITool> Tools => _tools;

    /// <summary>Looks up a tool by its stable identifier.</summary>
    /// <param name="id">The identifier, for example <c>"xpxel.tool.pencil"</c>.</param>
    /// <returns>
    /// The tool, or <see langword="null"/> if no tool with that identifier is registered —
    /// which happens legitimately when restoring a setting written by a build that had a
    /// tool this one does not.
    /// </returns>
    public ITool? Find(string id) => _byId.GetValueOrDefault(id);
}
