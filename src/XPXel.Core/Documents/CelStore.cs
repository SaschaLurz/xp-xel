using XPXel.Core.Imaging;
using XPXel.Core.Primitives;

namespace XPXel.Core.Documents;

/// <summary>
/// Identifies a cel by the layer and frame it belongs to.
/// </summary>
/// <param name="LayerId">The owning layer's identity.</param>
/// <param name="FrameId">The owning frame's identity.</param>
public readonly record struct CelKey(Guid LayerId, Guid FrameId);

/// <summary>
/// The document's collection of cels, addressed by layer and frame.
/// </summary>
/// <remarks>
/// <para>
/// Storing cels centrally on the document — rather than hanging them off layers — keeps
/// both axes independent. Deleting a frame is one call here; deleting a layer is another;
/// neither needs to walk the other axis, and neither needs the layer tree to know that
/// frames exist.
/// </para>
/// <para>
/// The store is <em>sparse</em>: a missing entry means "this layer has no content on this
/// frame", which the compositor renders as nothing. That is already how an empty layer
/// behaves, and it is the representation an animation feature would want for held frames.
/// </para>
/// </remarks>
public sealed class CelStore
{
    private readonly Dictionary<CelKey, Cel> _cels = [];

    /// <summary>The number of cels currently stored.</summary>
    public int Count => _cels.Count;

    /// <summary>Enumerates every cel in the document, in no particular order.</summary>
    public IEnumerable<Cel> All => _cels.Values;

    /// <summary>Looks up the cel for a layer and frame.</summary>
    /// <param name="layerId">The layer's identity.</param>
    /// <param name="frameId">The frame's identity.</param>
    /// <returns>The cel, or <see langword="null"/> if the layer has no content on that frame.</returns>
    public Cel? Find(Guid layerId, Guid frameId) =>
        _cels.GetValueOrDefault(new CelKey(layerId, frameId));

    /// <summary>
    /// Returns the cel for a layer and frame, creating an empty one of the given size if it
    /// does not exist yet. This is what a drawing tool calls before its first edit.
    /// </summary>
    /// <param name="layerId">The layer's identity.</param>
    /// <param name="frameId">The frame's identity.</param>
    /// <param name="size">The buffer size to use if a cel has to be created.</param>
    /// <returns>The existing or newly created cel.</returns>
    public Cel GetOrCreate(Guid layerId, Guid frameId, ImageSize size)
    {
        CelKey key = new(layerId, frameId);
        if (_cels.TryGetValue(key, out Cel? existing))
        {
            return existing;
        }

        Cel created = new(layerId, frameId, new PixelBuffer(size));
        _cels.Add(key, created);
        return created;
    }

    /// <summary>Stores a cel, replacing any existing content for the same layer and frame.</summary>
    /// <param name="cel">The cel to store.</param>
    public void Set(Cel cel)
    {
        ArgumentNullException.ThrowIfNull(cel);
        _cels[new CelKey(cel.LayerId, cel.FrameId)] = cel;
    }

    /// <summary>Removes the cel for a layer and frame, if one exists.</summary>
    /// <param name="layerId">The layer's identity.</param>
    /// <param name="frameId">The frame's identity.</param>
    /// <returns>The removed cel, or <see langword="null"/> if there was none.</returns>
    public Cel? Remove(Guid layerId, Guid frameId)
    {
        CelKey key = new(layerId, frameId);
        if (_cels.Remove(key, out Cel? removed))
        {
            return removed;
        }

        return null;
    }

    /// <summary>
    /// Removes and returns every cel belonging to a layer. Used when a layer is deleted;
    /// the returned cels are retained by the undo command so the deletion can be reversed.
    /// </summary>
    /// <param name="layerId">The layer's identity.</param>
    /// <returns>The removed cels.</returns>
    public IReadOnlyList<Cel> RemoveAllForLayer(Guid layerId) => RemoveWhere(key => key.LayerId == layerId);

    /// <summary>
    /// Removes and returns every cel belonging to a frame. Used when a frame is deleted.
    /// </summary>
    /// <param name="frameId">The frame's identity.</param>
    /// <returns>The removed cels.</returns>
    public IReadOnlyList<Cel> RemoveAllForFrame(Guid frameId) => RemoveWhere(key => key.FrameId == frameId);

    /// <summary>Restores a batch of previously removed cels. Used by undo.</summary>
    /// <param name="cels">The cels to put back.</param>
    public void RestoreRange(IEnumerable<Cel> cels)
    {
        ArgumentNullException.ThrowIfNull(cels);

        foreach (Cel cel in cels)
        {
            Set(cel);
        }
    }

    private List<Cel> RemoveWhere(Func<CelKey, bool> predicate)
    {
        List<CelKey> keys = [];
        foreach (CelKey key in _cels.Keys)
        {
            if (predicate(key))
            {
                keys.Add(key);
            }
        }

        List<Cel> removed = new(keys.Count);
        foreach (CelKey key in keys)
        {
            if (_cels.Remove(key, out Cel? cel))
            {
                removed.Add(cel);
            }
        }

        return removed;
    }
}
