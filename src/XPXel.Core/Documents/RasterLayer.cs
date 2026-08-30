namespace XPXel.Core.Documents;

/// <summary>
/// A layer whose content is a grid of pixels — the only layer type an artist can create in
/// v0.1, and the one that holds actual artwork.
/// </summary>
/// <remarks>
/// <para>
/// The class is deliberately empty beyond what <see cref="LayerNode"/> provides, because a
/// raster layer's pixels are <em>not</em> stored on the layer. They live in
/// <see cref="Cel"/> objects owned by the <see cref="PixelDocument"/> and keyed by layer
/// identity plus frame identity. That indirection is what lets a single layer hold
/// different artwork on each animation frame without the layer object knowing that
/// animation exists.
/// </para>
/// <para>
/// Future layer types that would slot in beside this one: an adjustment layer (applies a
/// filter to everything below), a reference layer (visible while drawing, excluded from
/// export), and a tilemap layer. Each would subclass <see cref="LayerNode"/> and gain a
/// branch in <c>LayerCompositor.ComposeNode</c>.
/// </para>
/// </remarks>
public sealed class RasterLayer : LayerNode
{
    /// <summary>Initialises a raster layer.</summary>
    /// <param name="id">The layer's stable identity.</param>
    /// <param name="name">The display name shown in the layer panel.</param>
    public RasterLayer(Guid id, string name)
        : base(id, name)
    {
    }

    /// <inheritdoc />
    public override LayerNode CreateStructuralCopy(Guid newId, string newName)
    {
        RasterLayer copy = new(newId, newName);
        CopySettingsTo(copy);
        return copy;
    }
}
