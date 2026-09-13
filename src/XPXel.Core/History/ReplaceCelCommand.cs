using XPXel.Core.Documents;

namespace XPXel.Core.History;

/// <summary>
/// Swaps one cel for another on the same layer and frame, and swaps it back on undo.
/// </summary>
/// <remarks>
/// <para>
/// A cel's buffer never changes size once it exists — a stroke in progress holds a reference
/// to it and records pixel indices against it — so the only way to make room for artwork
/// heading off the canvas is to build a bigger cel and put that in the document instead. That
/// is what <see cref="Cel.CreateExpanded"/> produces and what this records.
/// </para>
/// <para>
/// <b>It always comes first in its composite.</b> The raster edits that follow it are recorded
/// against the <em>new</em> cel, so redo has to install that cel before they are replayed, and
/// undo has to unwind them before the old one goes back. A composite's ordering gives both for
/// free as long as this is the first part.
/// </para>
/// </remarks>
public sealed class ReplaceCelCommand : IUndoableCommand
{
    private readonly PixelDocument _document;
    private readonly Cel _before;
    private readonly Cel _after;

    /// <summary>Initialises the command.</summary>
    /// <param name="name">The action name shown in the Edit menu.</param>
    /// <param name="document">The document holding the cel.</param>
    /// <param name="before">The cel to put back on undo.</param>
    /// <param name="after">The cel to install.</param>
    public ReplaceCelCommand(string name, PixelDocument document, Cel before, Cel after)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        Name = name;
        _document = document;
        _before = before;
        _after = after;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public void Redo()
    {
        _document.Cels.Set(_after);
        _document.NotifyPixelsChanged(_document.Bounds);
    }

    /// <inheritdoc />
    public void Undo()
    {
        _document.Cels.Set(_before);
        _document.NotifyPixelsChanged(_document.Bounds);
    }
}
