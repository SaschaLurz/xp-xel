using XPXel.Core.ComponentModel;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;

namespace XPXel.Core.Documents;

/// <summary>
/// The base class for every entry in a document's layer tree.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a tree and not a list.</b> XP-xel ships only <see cref="RasterLayer"/> in v0.1,
/// so a flat list would work today. It is a tree from the start because layer folders are
/// a near-certain future feature, and retro-fitting a hierarchy would touch the document
/// model, the compositor, the layer panel, the undo commands and the file format all at
/// once. Supporting it now costs one recursive loop in the compositor.
/// </para>
/// <para>
/// <b>Ordering convention.</b> Within <see cref="GroupLayer.Children"/>, index 0 is the
/// <em>bottom-most</em> layer and the last index is the top-most. The compositor therefore
/// walks the list forwards. Layer panels conventionally show the top-most layer first, so
/// the UI reverses this for display — the model never does.
/// </para>
/// <para>
/// A layer carries only structure and presentation settings. Its pixels live in
/// <see cref="Cel"/> objects held by the document, keyed by layer <em>and</em> frame, which
/// is what allows one layer to have different content on different animation frames.
/// </para>
/// </remarks>
public abstract class LayerNode : ObservableModel
{
    private string _name;
    private bool _isVisible = true;
    private bool _isLocked;
    private double _opacity = 1.0;
    private BlendMode _blendMode = BlendMode.Normal;
    private PixelColor? _tagColor;

    /// <summary>Initialises a layer.</summary>
    /// <param name="id">
    /// The layer's stable identity. It is persisted in project files and used as half of a
    /// cel's key, so it must never change over the lifetime of a layer.
    /// </param>
    /// <param name="name">The display name shown in the layer panel.</param>
    protected LayerNode(Guid id, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        Id = id;
        _name = name;
    }

    /// <summary>
    /// The layer's stable identity, persisted in project files and used to key its cels.
    /// </summary>
    public Guid Id { get; }

    /// <summary>The display name shown in the layer panel.</summary>
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? string.Empty);
    }

    /// <summary>
    /// Whether the layer contributes to the composited image. Hidden layers are skipped
    /// entirely by the compositor and are excluded from flattened exports.
    /// </summary>
    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    /// <summary>
    /// Whether the layer rejects edits. Locking affects tools only; it does not change how
    /// the layer renders.
    /// </summary>
    public bool IsLocked
    {
        get => _isLocked;
        set => SetProperty(ref _isLocked, value);
    }

    /// <summary>
    /// The layer's overall opacity, from 0 (invisible) to 1 (fully opaque). Values outside
    /// that range are clamped rather than rejected, so a slider or an imported file can
    /// never put the document into an invalid state.
    /// </summary>
    public double Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, Math.Clamp(value, 0.0, 1.0));
    }

    /// <summary>How this layer combines with everything composited beneath it.</summary>
    public BlendMode BlendMode
    {
        get => _blendMode;
        set => SetProperty(ref _blendMode, value);
    }

    /// <summary>
    /// An optional colour used to label this layer in the user interface, or
    /// <see langword="null"/> for no label.
    /// </summary>
    /// <remarks>
    /// This is purely organisational — a way for an artist to group "character", "effects"
    /// and "background" layers at a glance in a deep stack. It has <b>no</b> effect on
    /// compositing, and the renderer must never read it. Keeping it on the model rather
    /// than in a UI-side lookup is what allows it to survive a save and reload.
    /// </remarks>
    public PixelColor? TagColor
    {
        get => _tagColor;
        set => SetProperty(ref _tagColor, value);
    }

    /// <summary>
    /// The group that contains this layer, or <see langword="null"/> for the document's
    /// root. Maintained by <see cref="GroupLayer"/>; never assign it directly.
    /// </summary>
    public GroupLayer? Parent { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether this layer and every ancestor is visible — the
    /// condition the compositor actually cares about, since hiding a folder must hide its
    /// contents.
    /// </summary>
    public bool IsEffectivelyVisible
    {
        get
        {
            for (LayerNode? node = this; node is not null; node = node.Parent)
            {
                if (!node.IsVisible)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Creates a structural copy of this layer under a new identity. Pixel content is not
    /// copied: cels belong to the document, so duplicating a layer's artwork is
    /// <see cref="PixelDocument.DuplicateLayer"/>'s job.
    /// </summary>
    /// <param name="newId">The identity for the copy.</param>
    /// <param name="newName">The display name for the copy.</param>
    /// <returns>A new layer with the same presentation settings.</returns>
    public abstract LayerNode CreateStructuralCopy(Guid newId, string newName);

    /// <summary>Copies the presentation settings of this layer onto another.</summary>
    /// <param name="target">The layer to receive the settings.</param>
    protected void CopySettingsTo(LayerNode target)
    {
        ArgumentNullException.ThrowIfNull(target);

        target.IsVisible = IsVisible;
        target.IsLocked = IsLocked;
        target.Opacity = Opacity;
        target.BlendMode = BlendMode;
        target.TagColor = TagColor;
    }

    /// <inheritdoc />
    public override string ToString() => $"{GetType().Name} \"{Name}\" ({Id:D})";
}
