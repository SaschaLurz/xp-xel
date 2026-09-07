using System.Globalization;

namespace XPXel.UI.Services;

/// <summary>
/// One arrangement of panels the artist has named and kept.
/// </summary>
/// <param name="Name">What the artist called it, and what the View menu lists.</param>
/// <param name="Layout">
/// The serialised docking tree, in whatever form <see cref="DockLayoutStore"/> writes.
/// </param>
public sealed record LayoutPreset(string Name, string Layout);

/// <summary>
/// Keeps the arrangements an artist has saved by name, so a layout can be returned to rather
/// than rebuilt by hand.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is separate from <see cref="DockLayoutStore"/>.</b> That one remembers a single
/// thing — where the panels were when the editor last closed — and restores it silently on the
/// next launch. This one holds a list the artist curates: a setup for drawing, another for
/// animating, another for a second monitor. The two answer different questions and would only
/// get in each other's way sharing a class.
/// </para>
/// <para>
/// <b>Stored as numbered keys rather than as one JSON document.</b>
/// <see cref="ISettingsStore"/> is deliberately a flat string-to-string store, and writing a
/// serialised list into one of its values would mean a JSON document nested inside a JSON
/// document, plus a serialiser this project would then have to keep trim-safe. Numbered keys
/// need neither, and a single corrupt entry costs one layout rather than all of them.
/// </para>
/// <para>
/// <b>Names are the identity.</b> Saving over an existing name replaces it, which is what
/// "save" means when the artist types a name they have used before. Nothing here is
/// case-sensitive about that, because two layouts called "Drawing" and "drawing" are one
/// layout as far as the person who named them is concerned.
/// </para>
/// </remarks>
public sealed class LayoutPresetStore
{
    /// <summary>How many presets are stored.</summary>
    private const string CountKey = "layout.presets.count";

    /// <summary>The most presets that will be kept, so a stuck loop cannot fill the settings file.</summary>
    private const int MaximumPresets = 64;

    private readonly ISettingsStore _settings;

    /// <summary>Initialises the store.</summary>
    /// <param name="settings">Where the presets are kept.</param>
    public LayoutPresetStore(ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    /// <summary>Reads every saved preset, in the order they were written.</summary>
    /// <returns>The presets, which is empty when none have been saved.</returns>
    public IReadOnlyList<LayoutPreset> Load()
    {
        if (!int.TryParse(_settings.GetValue(CountKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
        {
            return [];
        }

        List<LayoutPreset> presets = [];

        for (int i = 0; i < Math.Clamp(count, 0, MaximumPresets); i++)
        {
            string? name = _settings.GetValue(NameKey(i));
            string? layout = _settings.GetValue(LayoutKey(i));

            // A half-written entry is skipped rather than surfaced as a layout that would fail
            // to apply. Losing one preset is a great deal better than an editor that cannot
            // open its own View menu.
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(layout))
            {
                presets.Add(new LayoutPreset(name, layout));
            }
        }

        return presets;
    }

    /// <summary>Writes the whole list, replacing whatever was there.</summary>
    /// <param name="presets">The presets to keep.</param>
    public void Save(IReadOnlyList<LayoutPreset> presets)
    {
        ArgumentNullException.ThrowIfNull(presets);

        // The old entries are cleared first and past the new count as well, so that shrinking
        // the list does not leave the tail of the previous one behind to be read back.
        int previous = int.TryParse(
            _settings.GetValue(CountKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out int stored)
            ? Math.Clamp(stored, 0, MaximumPresets)
            : 0;

        int count = Math.Min(presets.Count, MaximumPresets);

        for (int i = 0; i < count; i++)
        {
            _settings.SetValue(NameKey(i), presets[i].Name);
            _settings.SetValue(LayoutKey(i), presets[i].Layout);
        }

        for (int i = count; i < previous; i++)
        {
            _settings.SetValue(NameKey(i), null);
            _settings.SetValue(LayoutKey(i), null);
        }

        _settings.SetValue(CountKey, count.ToString(CultureInfo.InvariantCulture));
    }

    private static string NameKey(int index) =>
        string.Create(CultureInfo.InvariantCulture, $"layout.preset.{index}.name");

    private static string LayoutKey(int index) =>
        string.Create(CultureInfo.InvariantCulture, $"layout.preset.{index}.data");
}
