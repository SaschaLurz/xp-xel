using XPXel.Core.ComponentModel;

namespace XPXel.Core.Documents;

/// <summary>
/// One moment in a document's timeline. Every document has at least one frame; v0.1 always
/// has exactly one and shows no timeline UI.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type is the reason animation will not require a rewrite.</b> The obvious v1
/// design — a layer owns one image — collapses the "which layer" and "which moment"
/// questions into a single axis. Separating them now means adding animation later is a new
/// panel plus frame-management commands, rather than surgery on the document model, the
/// compositor, the tools, the undo system and the file format simultaneously.
/// </para>
/// <para>
/// <b>Why frames carry a <see cref="Guid"/> rather than being addressed by index.</b>
/// Cels are keyed by (layer id, frame id). If frames were addressed positionally, then
/// inserting or reordering a frame would require rewriting every cel key and every undo
/// record that mentions one. With stable identities, reordering is a list operation and
/// nothing else in the document needs to know.
/// </para>
/// </remarks>
public sealed class Frame : ObservableModel
{
    /// <summary>The duration used for a newly created frame, in milliseconds.</summary>
    public const int DefaultDurationMilliseconds = 100;

    private int _durationMilliseconds = DefaultDurationMilliseconds;

    /// <summary>Initialises a frame.</summary>
    /// <param name="id">The frame's stable identity, used as half of every cel key.</param>
    public Frame(Guid id) => Id = id;

    /// <summary>The frame's stable identity, persisted in project files.</summary>
    public Guid Id { get; }

    /// <summary>
    /// How long this frame is displayed during playback, in milliseconds. Per-frame timing
    /// (rather than a single document-wide frame rate) matches how sprite animation is
    /// actually authored, where a hold pose runs longer than an in-between.
    /// </summary>
    public int DurationMilliseconds
    {
        get => _durationMilliseconds;
        set => SetProperty(ref _durationMilliseconds, Math.Max(1, value));
    }

    /// <inheritdoc />
    public override string ToString() => $"Frame {Id:D} ({DurationMilliseconds} ms)";
}
