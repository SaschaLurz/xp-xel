using XPXel.Core.ComponentModel;
using XPXel.Core.Primitives;

namespace XPXel.Core.Tools;

/// <summary>
/// Transient geometry a tool wants drawn on top of the canvas while an interaction is in
/// progress — the dashed rectangle a marquee drags out, and whatever later tools need.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists when shape tools do not need it.</b> A shape tool previews itself by
/// drawing onto the layer and undoing it again, which is exactly right for something that
/// will end up as pixels. A selection never becomes pixels: its preview is a piece of user
/// interface, and drawing it into the artwork would be wrong even briefly. So it is
/// described here, in terms a tool can express without knowing what a screen is, and the
/// canvas decides how to render it.
/// </para>
/// <para>
/// The type stays deliberately small. It is a channel from the tools to the canvas, not a
/// display list — anything that starts to look like drawing commands belongs in the canvas
/// control instead.
/// </para>
/// </remarks>
public sealed class ToolOverlay : ObservableModel
{
    private ImageRect? _marquee;
    private ImageRect? _caret;

    /// <summary>
    /// A rectangle to outline, in document coordinates, or <see langword="null"/> for none.
    /// Used by the selection tool while its drag is in progress.
    /// </summary>
    public ImageRect? Marquee
    {
        get => _marquee;
        set => SetProperty(ref _marquee, value);
    }

    /// <summary>
    /// The text caret, in document coordinates, or <see langword="null"/> when no text is
    /// being edited.
    /// </summary>
    public ImageRect? Caret
    {
        get => _caret;
        set => SetProperty(ref _caret, value);
    }

    /// <summary>Removes everything currently being previewed.</summary>
    public void Clear()
    {
        Marquee = null;
        Caret = null;
    }
}
