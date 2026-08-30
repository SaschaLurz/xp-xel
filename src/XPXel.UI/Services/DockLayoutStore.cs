using Dock.Model.Controls;
using Dock.Serializer.SystemTextJson;

namespace XPXel.UI.Services;

/// <summary>
/// Saves and restores the arrangement of panels and floating windows between sessions.
/// </summary>
/// <remarks>
/// <para>
/// The layout is stored as JSON through <see cref="ISettingsStore"/>, so it lands in a file
/// on the desktop and in local storage in the browser without this class knowing which.
/// </para>
/// <para>
/// <b>Every failure falls back to the default layout.</b> A layout is a convenience, and a
/// corrupt or outdated one must never stop the editor from starting — which is exactly what
/// an unhandled deserialisation error would do, and in the most annoying possible way, since
/// the artist would have no way to reset it without finding the settings file. Anything that
/// goes wrong here therefore returns <see langword="null"/> and the caller builds a fresh
/// layout.
/// </para>
/// <para>
/// <b>A note on trimming.</b> Dock's serialiser uses reflection-based System.Text.Json. That
/// is the one piece of XP-xel that would need attention before enabling aggressive trimming
/// or NativeAOT; the fix is to pass a source-generated <c>IJsonTypeInfoResolver</c> to
/// <see cref="DockSerializer"/>. Nothing else about the layout would change.
/// </para>
/// </remarks>
public sealed class DockLayoutStore
{
    /// <summary>The settings key the serialised layout is stored under.</summary>
    private const string LayoutKey = "layout.dock";

    /// <summary>
    /// Bumped whenever the default layout changes shape enough that restoring an old one
    /// would produce something confusing — a panel in a place it can no longer be reached
    /// from, for instance. A mismatch silently discards the saved layout.
    /// </summary>
    private const string LayoutVersion = "1";

    private const string VersionKey = "layout.version";

    private readonly ISettingsStore _settings;
    private readonly DockSerializer _serializer = new(typeof(List<>));

    /// <summary>Initialises the store.</summary>
    /// <param name="settings">Where the serialised layout is kept.</param>
    public DockLayoutStore(ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    /// <summary>Reads the saved layout.</summary>
    /// <returns>The restored layout, or <see langword="null"/> if there is none to restore.</returns>
    public IRootDock? Load()
    {
        try
        {
            if (_settings.GetValue(VersionKey) != LayoutVersion)
            {
                return null;
            }

            string? json = _settings.GetValue(LayoutKey);
            return string.IsNullOrWhiteSpace(json) ? null : _serializer.Deserialize<IRootDock?>(json);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Deliberately broad: the serialiser can throw a wide range of exceptions on a
            // layout written by a different version, and none of them are worth a crash.
            return null;
        }
    }

    /// <summary>Writes the current layout.</summary>
    /// <param name="layout">The layout to persist.</param>
    public void Save(IRootDock layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        try
        {
            _settings.SetValue(LayoutKey, _serializer.Serialize(layout));
            _settings.SetValue(VersionKey, LayoutVersion);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Failing to remember the layout is not worth interrupting the artist over.
        }
    }

    /// <summary>Forgets the saved layout, so the next start uses the default arrangement.</summary>
    public void Clear()
    {
        _settings.SetValue(LayoutKey, null);
        _settings.SetValue(VersionKey, null);
    }
}
