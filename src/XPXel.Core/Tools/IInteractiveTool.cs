namespace XPXel.Core.Tools;

/// <summary>The keys a tool may be given, beyond the pointer.</summary>
/// <remarks>
/// Deliberately two. A tool that wanted the whole keyboard would be a text editor, and there
/// is already a session type for that; these are the two keys that mean "yes" and "no" to a
/// gesture that is waiting for an answer.
/// </remarks>
public enum ToolKey
{
    /// <summary>Commit whatever the tool is holding.</summary>
    Enter,

    /// <summary>Abandon it.</summary>
    Escape,
}

/// <summary>
/// Implemented by tools that are doing something between clicks, and so need to know when they
/// are picked up and put down, and when the artist says yes or no.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a separate interface.</b> Nearly every tool is a pure function of the pointer
/// events it is handed, holds nothing between them, and needs none of this — which is what
/// makes <see cref="ITool"/> as small as it is. A crop frame is the exception: it exists from
/// the moment the tool is chosen, survives any number of adjustments, and is applied only when
/// the artist says so. Rather than widen <see cref="ITool"/> for the one tool that needs more,
/// the extra is opt-in and the canvas asks whether a tool wants it.
/// </para>
/// <para>
/// The canvas is what calls these, because a tool is never otherwise told that it has been
/// selected or deselected, and keystrokes arrive at the canvas rather than at the tool.
/// </para>
/// </remarks>
public interface IInteractiveTool
{
    /// <summary>Called when the artist picks this tool.</summary>
    /// <param name="context">The document it will act on.</param>
    void OnActivated(ToolContext context);

    /// <summary>
    /// Called when the artist picks a different tool, or the document goes away.
    /// </summary>
    /// <param name="context">The document it was acting on.</param>
    /// <remarks>
    /// Anything the tool was holding must be dropped here. A crop frame left behind would be
    /// drawn over artwork the pencil is now being used on.
    /// </remarks>
    void OnDeactivated(ToolContext context);

    /// <summary>Offers the tool a key.</summary>
    /// <param name="context">The document being acted on.</param>
    /// <param name="key">The key that was pressed.</param>
    /// <returns><see langword="true"/> if the tool acted on it.</returns>
    bool OnKeyDown(ToolContext context, ToolKey key);
}
