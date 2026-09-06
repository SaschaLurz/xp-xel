namespace XPXel.Formats.Animation;

/// <summary>
/// The animation export formats the application offers, in the order the export dialog lists
/// them.
/// </summary>
/// <remarks>
/// <para>
/// The animation counterpart to <see cref="ImageFormatRegistry"/>, and populated the same
/// way: from whatever <see cref="IAnimationExporter"/> implementations the container knows
/// about. Registering an exporter is therefore the only step needed to make it appear.
/// </para>
/// <para>
/// <b>Order is deliberate and is the composition root's decision.</b> The sprite sheet is
/// listed first because it is the format 2D game animation actually ships in, and the first
/// entry is the one an artist in a hurry will take.
/// </para>
/// </remarks>
public sealed class AnimationExportRegistry
{
    private readonly IAnimationExporter[] _exporters;

    /// <summary>Initialises the registry.</summary>
    /// <param name="exporters">The available exporters, in the order they should be listed.</param>
    /// <exception cref="ArgumentException">Two exporters share a format identifier.</exception>
    public AnimationExportRegistry(IEnumerable<IAnimationExporter> exporters)
    {
        ArgumentNullException.ThrowIfNull(exporters);

        _exporters = [.. exporters];

        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (IAnimationExporter exporter in _exporters)
        {
            if (!seen.Add(exporter.Descriptor.Id))
            {
                throw new ArgumentException(
                    $"More than one animation exporter uses the identifier '{exporter.Descriptor.Id}'.",
                    nameof(exporters));
            }
        }
    }

    /// <summary>The available exporters, in display order.</summary>
    public IReadOnlyList<IAnimationExporter> Exporters => _exporters;

    /// <summary>Looks up an exporter by its format identifier.</summary>
    /// <param name="formatId">The identifier, for example <c>"xpxel.format.spritesheet"</c>.</param>
    /// <returns>
    /// The exporter, or <see langword="null"/> when this build has no such format — which
    /// happens legitimately when restoring a setting written by a build that did.
    /// </returns>
    public IAnimationExporter? Find(string formatId) =>
        _exporters.FirstOrDefault(e => string.Equals(e.Descriptor.Id, formatId, StringComparison.Ordinal));
}
