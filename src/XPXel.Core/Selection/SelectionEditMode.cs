namespace XPXel.Core.Selection;

/// <summary>
/// What a selection gesture should do to the marks that are already there.
/// </summary>
/// <remarks>
/// <para>
/// Every tool that marks pixels understands all three, and reads the same modifier keys to
/// choose between them: nothing means <see cref="Replace"/>, Shift means <see cref="Add"/>,
/// Control means <see cref="Subtract"/>. That is the convention in every editor that has a
/// selection, and the reason it is defined once here rather than per tool is that an artist
/// who has learnt it on one tool must not have to learn it again on the next.
/// </para>
/// <para>
/// <b>Replace is the default, deliberately.</b> A tool that always added would make a stray
/// click permanent: the only way back would be to deselect and start again. Replacing means a
/// click is always a fresh start and Shift is the explicit "and this too".
/// </para>
/// </remarks>
public enum SelectionEditMode
{
    /// <summary>Throw away what was marked and mark this instead.</summary>
    Replace,

    /// <summary>Keep what was marked and mark this as well.</summary>
    Add,

    /// <summary>Keep what was marked, except for this.</summary>
    Subtract,
}
