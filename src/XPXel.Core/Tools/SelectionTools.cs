using XPXel.Core.Primitives;

namespace XPXel.Core.Tools;

/// <summary>
/// The behaviour every tool that marks pixels shares.
/// </summary>
/// <remarks>
/// <b>Pressing inside a selection picks it up.</b> Having marked something, the next thing an
/// artist wants is almost always to move it, and reaching for a menu to do it breaks the
/// gesture in half. Every marking tool therefore hands a press that lands on marked pixels to
/// <see cref="Editing.SelectionTransform"/> rather than starting a new mark — which is what
/// makes "mark it, then drag it" work the same way whichever tool did the marking.
/// </remarks>
public static class SelectionTools
{
    /// <summary>
    /// Picks the selected pixels up if a press landed on them.
    /// </summary>
    /// <param name="context">The document being acted on.</param>
    /// <param name="position">Where the pointer went down, in image coordinates.</param>
    /// <returns>
    /// <see langword="true"/> if the pixels are now in the artist's hand and the tool should
    /// do nothing further with this press.
    /// </returns>
    public static bool TryBeginMove(ToolContext context, ImagePoint position)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Workspace.Selection is not { IsEmpty: false } selection
            || !selection.IsSelected(position.X, position.Y))
        {
            return false;
        }

        return context.Workspace.BeginTransform();
    }
}
