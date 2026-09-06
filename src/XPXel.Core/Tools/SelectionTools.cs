using XPXel.Core.Primitives;
using XPXel.Core.Selection;

namespace XPXel.Core.Tools;

/// <summary>
/// The behaviour every tool that marks pixels shares.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pressing inside a selection picks it up.</b> Having marked something, the next thing an
/// artist wants is almost always to move it, and reaching for a menu to do it breaks the
/// gesture in half. Every marking tool therefore hands a press that lands on marked pixels to
/// <see cref="Editing.SelectionTransform"/> rather than starting a new mark — which is what
/// makes "mark it, then drag it" work the same way whichever tool did the marking.
/// </para>
/// <para>
/// <b>Shift adds and Control subtracts, on every one of them.</b> The modifiers are read here
/// rather than in each tool so that they cannot drift apart: an artist who has learnt them on
/// the marquee must not have to learn them again on the wand. See
/// <see cref="SelectionEditMode"/>.
/// </para>
/// </remarks>
public static class SelectionTools
{
    /// <summary>
    /// Reads the modifier keys of a press to decide what the gesture should do to the marks
    /// that are already there.
    /// </summary>
    /// <param name="e">The pointer interaction.</param>
    /// <returns>The mode the gesture should use.</returns>
    /// <remarks>
    /// Alt is deliberately not a synonym for Control here. The marquee uses it to constrain a
    /// drag to a square, and a key that meant two things at once on one tool would be worse
    /// than a key that means one thing on all of them.
    /// </remarks>
    public static SelectionEditMode GetEditMode(ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // Subtracting wins over adding, so Shift+Control is not a third, unguessable meaning.
        if (e.IsControlDown)
        {
            return SelectionEditMode.Subtract;
        }

        return e.IsShiftDown ? SelectionEditMode.Add : SelectionEditMode.Replace;
    }

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

    /// <summary>
    /// Returns the mask a gesture should write into.
    /// </summary>
    /// <param name="context">The document being acted on.</param>
    /// <param name="mode">What the gesture is doing to the existing marks.</param>
    /// <returns>
    /// The existing mask when the gesture is adding to or subtracting from it, and a fresh
    /// empty one when it is replacing it.
    /// </returns>
    /// <remarks>
    /// A mask made for a canvas of a different size is discarded rather than reused: it would
    /// index the wrong pixels, and the only way that happens is a resize the artist has
    /// already seen.
    /// </remarks>
    public static SelectionMask GetTargetMask(ToolContext context, SelectionEditMode mode)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (mode == SelectionEditMode.Replace)
        {
            return new SelectionMask(context.Document.Size);
        }

        return context.Workspace.Selection is { } existing && existing.Size == context.Document.Size
            ? existing
            : new SelectionMask(context.Document.Size);
    }

    /// <summary>Marks or unmarks one pixel according to the mode.</summary>
    /// <param name="mask">The mask being edited.</param>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    /// <param name="mode">What the gesture is doing.</param>
    public static void SetPixel(SelectionMask mask, int x, int y, SelectionEditMode mode)
    {
        ArgumentNullException.ThrowIfNull(mask);
        mask.SetCoverage(x, y, mode == SelectionEditMode.Subtract ? (byte)0 : byte.MaxValue);
    }

    /// <summary>
    /// Installs a finished mask on the workspace, tidying up after the gesture.
    /// </summary>
    /// <param name="context">The document being acted on.</param>
    /// <param name="mask">The mask the gesture produced.</param>
    /// <remarks>
    /// <para>
    /// The bounds are recomputed because subtracting can only ever have left them too large —
    /// <see cref="SelectionMask.SetCoverage"/> grows them and never shrinks them, since
    /// rescanning the mask on every pixel of a drag would be absurd.
    /// </para>
    /// <para>
    /// An empty mask is replaced by no mask at all. The two mean the same thing to every tool
    /// that reads one, but only the second says so: leaving an empty mask behind would clip
    /// every drawing tool to nothing, which looks exactly like the editor having stopped
    /// working.
    /// </para>
    /// </remarks>
    public static void Publish(ToolContext context, SelectionMask mask)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(mask);

        mask.RecalculateBounds();

        if (mask.IsEmpty)
        {
            context.Workspace.Selection = null;
            return;
        }

        if (ReferenceEquals(context.Workspace.Selection, mask))
        {
            // The same mask, mutated in place. Assigning it back would raise nothing, because
            // the property compares references.
            context.Workspace.NotifySelectionChanged();
            return;
        }

        context.Workspace.Selection = mask;
    }
}
