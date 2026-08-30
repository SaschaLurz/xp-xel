using System.Text.Json;
using System.Text.Json.Serialization;
using XPXel.UI.Services;

namespace XPXel.Desktop;

/// <summary>
/// Stores preferences as a small JSON file in the user's application data folder.
/// </summary>
/// <remarks>
/// <para>
/// Location follows each platform's convention through
/// <see cref="Environment.SpecialFolder.ApplicationData"/>: <c>%APPDATA%</c> on Windows,
/// <c>~/.config</c> on Linux, and <c>~/.config</c> under the user's home on macOS.
/// </para>
/// <para>
/// <b>Every failure is swallowed.</b> A read-only disk, a portable install on a locked-down
/// machine, or a corrupt file must not stop the application starting. Preferences are a
/// convenience; losing them costs an artist a theme click, whereas refusing to launch costs
/// them their session.
/// </para>
/// <para>
/// Writes go straight through on each change rather than being batched. The file holds a
/// handful of short strings and changes only when the artist alters a setting, so the
/// simplicity is worth more than the saved I/O.
/// </para>
/// </remarks>
internal sealed class FileSettingsStore : ISettingsStore
{
    private readonly string _filePath;
    private readonly Dictionary<string, string> _values;

    /// <summary>Initialises the store and loads any existing settings.</summary>
    public FileSettingsStore()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XP-xel");

        _filePath = Path.Combine(directory, "settings.json");
        _values = Load(_filePath);
    }

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

        Save();
    }

    /// <inheritdoc />
    public Task SaveAsync()
    {
        Save();
        return Task.CompletedTask;
    }

    private static Dictionary<string, string> Load(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                using FileStream stream = File.OpenRead(filePath);
                Dictionary<string, string>? loaded = JsonSerializer.Deserialize(
                    stream, SettingsJsonContext.Default.DictionaryStringString);

                if (loaded is not null)
                {
                    return new Dictionary<string, string>(loaded, StringComparer.Ordinal);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Start from defaults rather than failing to launch.
        }

        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            using FileStream stream = File.Create(_filePath);
            JsonSerializer.Serialize(stream, _values, SettingsJsonContext.Default.DictionaryStringString);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferences are best-effort; a failed write must never surface to the artist.
        }
    }
}

/// <summary>
/// Source-generated serializer for the settings file, so the desktop head stays compatible
/// with trimming and a future NativeAOT publish.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
