using XPXel.Core.Documents;

namespace XPXel.Core.History;

/// <summary>Adds a named animation to the document, and removes it again on undo.</summary>
public sealed class AddTagCommand : IUndoableCommand
{
    private readonly PixelDocument _document;
    private readonly AnimationTag _tag;
    private int _index;

    /// <summary>Initialises the command.</summary>
    /// <param name="name">The action name shown in the Edit menu.</param>
    /// <param name="document">The document to add to.</param>
    /// <param name="tag">The tag to add.</param>
    /// <param name="index">The position in the tag list, or -1 to append.</param>
    public AddTagCommand(string name, PixelDocument document, AnimationTag tag, int index = -1)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(tag);

        Name = name;
        _document = document;
        _tag = tag;
        _index = index;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>The tag this command adds.</summary>
    public AnimationTag Tag => _tag;

    /// <inheritdoc />
    public void Redo() => _document.InsertTag(_tag, _index);

    /// <inheritdoc />
    public void Undo() => _index = _document.RemoveTag(_tag);
}

/// <summary>Removes a named animation, and puts it back on undo.</summary>
public sealed class RemoveTagCommand : IUndoableCommand
{
    private readonly PixelDocument _document;
    private readonly AnimationTag _tag;
    private int _index;

    /// <summary>Initialises the command.</summary>
    /// <param name="name">The action name shown in the Edit menu.</param>
    /// <param name="document">The document to remove from.</param>
    /// <param name="tag">The tag to remove.</param>
    public RemoveTagCommand(string name, PixelDocument document, AnimationTag tag)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(tag);

        Name = name;
        _document = document;
        _tag = tag;
        _index = document.Tags.IndexOf(tag);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public void Redo() => _index = _document.RemoveTag(_tag);

    /// <inheritdoc />
    public void Undo() => _document.InsertTag(_tag, _index);
}

/// <summary>
/// Changes a tag's name, range, loop direction, repeat count or colour, all as one step.
/// </summary>
/// <remarks>
/// A single command for every property of a tag rather than one per field. A tag is a small
/// value object edited through a little form, and an artist who renames an animation and
/// widens its range in the same breath expects one undo to take both back.
/// </remarks>
public sealed class ChangeTagCommand : IUndoableCommand
{
    private readonly AnimationTag _tag;
    private readonly AnimationTag _before;
    private readonly AnimationTag _after;

    /// <summary>Initialises the command.</summary>
    /// <param name="name">The action name shown in the Edit menu.</param>
    /// <param name="tag">The tag being changed.</param>
    /// <param name="before">A copy of the tag as it was.</param>
    /// <param name="after">A copy of the tag as it should become.</param>
    public ChangeTagCommand(string name, AnimationTag tag, AnimationTag before, AnimationTag after)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        Name = name;
        _tag = tag;

        // Copied, so that later edits to the live tag cannot rewrite this command's history.
        _before = before.CloneWith(before.Id);
        _after = after.CloneWith(after.Id);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>Gets a value indicating whether the command would actually change anything.</summary>
    public bool HasChanges =>
        !string.Equals(_before.Name, _after.Name, StringComparison.Ordinal)
        || _before.From != _after.From
        || _before.To != _after.To
        || _before.Direction != _after.Direction
        || _before.RepeatCount != _after.RepeatCount
        || _before.Color != _after.Color;

    /// <inheritdoc />
    public void Redo() => Apply(_after);

    /// <inheritdoc />
    public void Undo() => Apply(_before);

    private void Apply(AnimationTag source)
    {
        _tag.Name = source.Name;

        // To first, then From: both setters keep the range non-inverted by dragging the other
        // end after them, so writing the wider bound first avoids a transient state that
        // would clamp the value being written second.
        _tag.To = Math.Max(source.From, source.To);
        _tag.From = source.From;
        _tag.To = source.To;

        _tag.Direction = source.Direction;
        _tag.RepeatCount = source.RepeatCount;
        _tag.Color = source.Color;
    }
}
