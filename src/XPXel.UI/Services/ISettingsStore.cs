namespace XPXel.UI.Services;

/// <summary>
/// Persists small user preferences — the chosen theme, the last tool, brush size — across
/// sessions.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a flat string-to-string store rather than a typed settings object. Where the
/// values physically live differs completely between platforms — a JSON file on desktop,
/// browser storage in WebAssembly, sandboxed app storage on mobile — and a key/value
/// interface is the largest surface every one of those can implement without compromise.
/// </para>
/// <para>
/// Callers must tolerate <see cref="Get"/> returning <see langword="null"/> and values that
/// no longer parse. Settings are a convenience; nothing may depend on them.
/// </para>
/// </remarks>
public interface ISettingsStore
{
    /// <summary>Reads a value.</summary>
    /// <param name="key">A dotted key such as <c>"appearance.theme"</c>.</param>
    /// <returns>The stored value, or <see langword="null"/> if the key is unset.</returns>
    string? GetValue(string key);

    /// <summary>Writes a value, or removes it when <paramref name="value"/> is null.</summary>
    /// <param name="key">A dotted key such as <c>"appearance.theme"</c>.</param>
    /// <param name="value">The value to store.</param>
    void SetValue(string key, string? value);

    /// <summary>Persists any pending changes to the underlying medium.</summary>
    /// <returns>A task that completes once the settings are durable.</returns>
    Task SaveAsync();
}

/// <summary>
/// A settings store that keeps everything in memory and forgets it when the application
/// exits.
/// </summary>
/// <remarks>
/// Used as the fallback so that a platform head which has not yet implemented persistence —
/// currently the WebAssembly head — still runs correctly rather than crashing on start-up.
/// </remarks>
public sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public string? GetValue(string key) => _values.GetValueOrDefault(key);

    /// <inheritdoc />
    public void SetValue(string key, string? value)
    {
        if (value is null)
        {
            _values.Remove(key);
        }
        else
        {
            _values[key] = value;
        }
    }

    /// <inheritdoc />
    public Task SaveAsync() => Task.CompletedTask;
}
