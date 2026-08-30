namespace XPXel.Core.Tools;

/// <summary>
/// A canvas tool: pencil, eraser, bucket fill, colour picker, and everything added later.
/// </summary>
/// <remarks>
/// <para>
/// <b>Adding a tool.</b> Implement this interface and register the class with the dependency
/// injection container as an <see cref="ITool"/>. <see cref="ToolRegistry"/> picks it up,
/// the toolbox shows it, and the canvas routes pointer input to it. No existing file needs
/// to change — that is the whole point of the interface.
/// </para>
/// <para>
/// <b>Tools are singletons and may hold stroke state.</b> One instance serves every open
/// document, and the document being acted on arrives with each call in the
/// <see cref="ToolContext"/>. Fields are therefore fine for "am I mid-stroke and where was
/// the last point", but must never cache anything document-specific beyond the current
/// interaction — and <see cref="Cancel"/> must clear all of it.
/// </para>
/// <para>
/// <b>Input arrives already converted</b> to image coordinates; see
/// <see cref="ToolPointerEventArgs"/>.
/// </para>
/// </remarks>
public interface ITool
{
    /// <summary>
    /// A stable identifier such as <c>"xpxel.tool.pencil"</c>. It is persisted in user
    /// settings to restore the last selected tool, so it must not change once shipped.
    /// </summary>
    string Id { get; }

    /// <summary>The name shown in the toolbox and tooltips.</summary>
    string DisplayName { get; }

    /// <summary>A one-line explanation shown in the tooltip beneath the name.</summary>
    string Description { get; }

    /// <summary>
    /// Called when a pointer button goes down over the canvas. This is where a stroke
    /// begins.
    /// </summary>
    /// <param name="context">The document and settings to act on.</param>
    /// <param name="e">The interaction.</param>
    void OnPointerPressed(ToolContext context, ToolPointerEventArgs e);

    /// <summary>
    /// Called as the pointer moves. Also called while no button is held, so tools that only
    /// act during a drag must check their own state rather than assuming a stroke is in
    /// progress.
    /// </summary>
    /// <param name="context">The document and settings to act on.</param>
    /// <param name="e">The interaction.</param>
    void OnPointerMoved(ToolContext context, ToolPointerEventArgs e);

    /// <summary>
    /// Called when the pointer button is released. This is where a stroke is committed to
    /// the undo history.
    /// </summary>
    /// <param name="context">The document and settings to act on.</param>
    /// <param name="e">The interaction.</param>
    void OnPointerReleased(ToolContext context, ToolPointerEventArgs e);

    /// <summary>
    /// Abandons any interaction in progress and restores the document — for example when the
    /// artist presses Escape, the window loses focus, or the active document changes
    /// mid-stroke. Implementations must be safe to call when nothing is in progress.
    /// </summary>
    /// <param name="context">The document the interaction was acting on.</param>
    void Cancel(ToolContext context);
}
