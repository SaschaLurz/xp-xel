using XPXel.Core.ComponentModel;

namespace XPXel.Core.Editing;

/// <summary>
/// How layers behave when artwork is put down on them: whether an operation that would land
/// on top of existing pixels is allowed to overwrite them, or is given a layer of its own.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Both of the options here answer the same question — "what happens
/// to what was already there?" — and both answer it the safe way by default. Placing a
/// transform over a drawing, or pasting on top of one, is the fastest way in any pixel editor
/// to destroy work that took an hour, and the artist usually only finds out several actions
/// later when undo is no longer a comfortable way back. Landing on a new layer costs nothing:
/// the layers are merged with one command when the artist is happy, and until then both
/// versions are still there.
/// </para>
/// <para>
/// <b>Why they are switchable.</b> Someone who works on a single layer on purpose — which is
/// how a lot of small sprite work is done — would find a stack of new layers appearing behind
/// their back far more annoying than the risk it protects them from. So the behaviour is on by
/// default and can be turned off, in the layers panel and in Settings ▸ Layers.
/// </para>
/// <para>
/// This is application-wide state, like <see cref="ToolSettings"/>: a way of working rather
/// than a property of any one picture, so it is a singleton handed to every document.
/// </para>
/// </remarks>
public sealed class LayerSettings : ObservableModel
{
    private bool _transformToNewLayer = true;
    private bool _pasteToNewLayer = true;

    /// <summary>
    /// Whether applying a transform that would cover existing pixels puts the result on a new
    /// layer above the one it came from.
    /// </summary>
    /// <remarks>
    /// Only when it actually would cover something. A transform that lands in empty space, or
    /// one that scales a whole layer in place, has nothing to destroy and stays where it is —
    /// so this never produces a new layer the artist cannot account for.
    /// </remarks>
    public bool TransformToNewLayer
    {
        get => _transformToNewLayer;
        set => SetProperty(ref _transformToNewLayer, value);
    }

    /// <summary>
    /// Whether pixels pasted from <em>another program</em> are placed on a new layer of their
    /// own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only artwork from outside.</b> An image dragged in from a browser or another editor
    /// has no relationship to the layer it lands on, and keeping it separate is what makes it
    /// something that can be moved around afterwards. Pixels copied within XP-xel are a
    /// different matter: the artist knows exactly where they came from, and a new layer for
    /// every paste turns "put this on each frame" into a stack of layers — which is precisely
    /// what it did before this was narrowed.
    /// </para>
    /// <para>
    /// To copy a whole layer, including its artwork on every frame, use Copy Layer in the
    /// layers panel rather than the pixel clipboard.
    /// </para>
    /// </remarks>
    public bool PasteToNewLayer
    {
        get => _pasteToNewLayer;
        set => SetProperty(ref _pasteToNewLayer, value);
    }
}
