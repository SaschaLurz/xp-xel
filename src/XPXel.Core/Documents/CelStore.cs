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
    /// <summary>
    /// The frame a continuous layer's single drawing is filed under.
    /// </summary>
    /// <remarks>
    /// No frame has an empty identity, so this cannot collide with a real one. Keying a
    /// continuous layer by the layer alone is what makes every frame resolve to the same cel
    /// without a single caller of <see cref="Find"/> having to know that continuous layers
    /// exist — the compositor, the thumbnails, the exporters and every tool are unchanged.
    /// </remarks>
    private static readonly Guid EveryFrame = Guid.Empty;

    private readonly Dictionary<CelKey, Cel> _cels = [];

    /// <summary>The layers whose content is one drawing shared by every frame.</summary>
    private readonly HashSet<Guid> _continuousLayers = [];

    /// <summary>The number of cels currently stored.</summary>
    public int Count => _cels.Count;

    /// <summary>Enumerates every cel in the document, in no particular order.</summary>
    public IEnumerable<Cel> All => _cels.Values;

    /// <summary>Looks up the cel for a layer and frame.</summary>
    /// <param name="layerId">The layer's identity.</param>
    /// <param name="frameId">The frame's identity.</param>
    /// <returns>The cel, or <see langword="null"/> if the layer has no content on that frame.</returns>
    /// <remarks>
    /// A continuous layer answers with the same cel whatever frame is asked for; see
    /// <see cref="LayerNode.IsContinuous"/>.
    /// </remarks>
    public Cel? Find(Guid layerId, Guid frameId) =>
        _cels.GetValueOrDefault(new CelKey(layerId, KeyFrame(layerId, frameId)));

    /// <summary>
    /// Gets a value indicating whether a layer's content is shared across every frame.
    /// </summary>
    /// <param name="layerId">The layer's identity.</param>
    /// <returns><see langword="true"/> if it is a continuous layer.</returns>
    public bool IsContinuous(Guid layerId) => _continuousLayers.Contains(layerId);

    /// <summary>
    /// Declares whether a layer's content is shared across every frame, moving whatever it
    /// already holds to match.
    /// </summary>
    /// <param name="layerId">The layer's identity.</param>
    /// <param name="continuous">Whether it should share one drawing.</param>
    /// <param name="frames">Every frame in the document, for handing out copies.</param>
    /// <param name="keepFrom">
    /// The frame whose drawing survives when several are being collapsed into one, or
    /// <see langword="null"/> to keep the first one that has any content.
    /// </param>
    /// <remarks>
    /// <b>Turning it on picks one drawing and discards the rest</b>, which is the only honest
    /// answer: ten frames of different artwork cannot become one drawing without choosing. The
    /// caller records what was there so an undo can put it back. <b>Turning it off gives each
    /// frame its own copy</b>, so the frames can then be changed independently.
    /// </remarks>
    public void SetContinuous(Guid layerId, bool continuous, IEnumerable<Frame> frames, Guid? keepFrom = null)
    {
        ArgumentNullException.ThrowIfNull(frames);

        if (continuous == _continuousLayers.Contains(layerId))
        {
            return;
        }

        List<Frame> all = [.. frames];

        if (continuous)
        {
            Cel? kept = keepFrom is { } from ? Find(layerId, from) : null;

            foreach (Frame frame in all)
            {
                if (_cels.Remove(new CelKey(layerId, frame.Id), out Cel? cel))
                {
                    kept ??= cel;
                }
            }

            _continuousLayers.Add(layerId);

            if (kept is not null)
            {
                _cels[new CelKey(layerId, EveryFrame)] = kept.CopyTo(layerId, EveryFrame);
            }

            return;
        }

        _continuousLayers.Remove(layerId);

        if (!_cels.Remove(new CelKey(layerId, EveryFrame), out Cel? shared))
        {
            return;
        }

        foreach (Frame frame in all)
        {
            _cels[new CelKey(layerId, frame.Id)] = shared.CopyTo(layerId, frame.Id);
        }
    }

    /// <summary>The frame a layer's content is actually filed under.</summary>
    private Guid KeyFrame(Guid layerId, Guid frameId) =>
        _continuousLayers.Contains(layerId) ? EveryFrame : frameId;

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
        CelKey key = new(layerId, KeyFrame(layerId, frameId));
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
        _cels[new CelKey(cel.LayerId, KeyFrame(cel.LayerId, cel.FrameId))] = cel;
    }

    /// <summary>Removes the cel for a layer and frame, if one exists.</summary>
    /// <param name="layerId">The layer's identity.</param>
    /// <param name="frameId">The frame's identity.</param>
    /// <returns>The removed cel, or <see langword="null"/> if there was none.</returns>
    public Cel? Remove(Guid layerId, Guid frameId)
    {
        CelKey key = new(layerId, KeyFrame(layerId, frameId));
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
    public IReadOnlyList<Cel> RemoveAllForLayer(Guid layerId)
    {
        // The flag goes with the artwork. A layer put back by an undo carries its own cels,
        // and those are keyed the way they were when they were taken out.
        _continuousLayers.Remove(layerId);
        return RemoveWhere(key => key.LayerId == layerId);
    }

    /// <summary>
    /// Removes and returns every cel belonging to a frame. Used when a frame is deleted.
    /// </summary>
    /// <param name="frameId">The frame's identity.</param>
    /// <returns>The removed cels.</returns>
    /// <remarks>
    /// A continuous layer is not touched: its one drawing belongs to every frame, so deleting
    /// one frame is not a reason to take the background away from the others.
    /// </remarks>
    public IReadOnlyList<Cel> RemoveAllForFrame(Guid frameId) =>
        RemoveWhere(key => key.FrameId == frameId && key.FrameId != EveryFrame);

    /// <summary>Restores a batch of previously removed cels. Used by undo.</summary>
    /// <param name="cels">The cels to put back.</param>
    public void RestoreRange(IEnumerable<Cel> cels)
    {
        ArgumentNullException.ThrowIfNull(cels);

        foreach (Cel cel in cels)
        {
            // Keyed by what the cel says rather than through Set, so a continuous layer's cel
            // goes back exactly where it was taken from even though the flag has been cleared
            // by the removal that produced it.
            _cels[new CelKey(cel.LayerId, cel.FrameId)] = cel;

            if (cel.FrameId == EveryFrame)
            {
                _continuousLayers.Add(cel.LayerId);
            }
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
