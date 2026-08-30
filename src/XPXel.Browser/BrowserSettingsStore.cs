using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using XPXel.UI.Services;

namespace XPXel.Browser;

/// <summary>
/// Stores preferences in the browser's <c>localStorage</c>.
/// </summary>
/// <remarks>
/// <para>
/// The keys are prefixed so that XP-xel cannot collide with anything else served from the
/// same origin — a real risk when the application is hosted on a shared domain such as a
/// project page.
/// </para>
/// <para>
/// <b>Every call is guarded.</b> Local storage is not always available: private browsing
/// modes, storage quotas, and blocked third-party storage all cause the API to throw rather
/// than return null. Preferences are a convenience, so a failure quietly means "no stored
/// value" instead of stopping the application.
/// </para>
/// <para>
/// <see cref="JSImport"/> generates the interop at compile time rather than through
/// reflection, which is what allows the WebAssembly build to be trimmed.
/// </para>
/// </remarks>
[SupportedOSPlatform("browser")]
internal sealed partial class BrowserSettingsStore : ISettingsStore
{
    private const string KeyPrefix = "xpxel.";

    /// <inheritdoc />
    public string? GetValue(string key)
    {
        try
        {
            return GetLocalStorageItem(KeyPrefix + key);
        }
        catch (JSException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void SetValue(string key, string? value)
    {
        try
        {
            if (value is null)
            {
                RemoveLocalStorageItem(KeyPrefix + key);
            }
            else
            {
                SetLocalStorageItem(KeyPrefix + key, value);
            }
        }
        catch (JSException)
        {
            // Storage is unavailable or full; preferences simply do not persist.
        }
    }

    /// <inheritdoc />
    /// <remarks>Local storage writes are already durable, so there is nothing to flush.</remarks>
    public Task SaveAsync() => Task.CompletedTask;

    [JSImport("globalThis.localStorage.getItem")]
    private static partial string? GetLocalStorageItem(string key);

    [JSImport("globalThis.localStorage.setItem")]
    private static partial void SetLocalStorageItem(string key, string value);

    [JSImport("globalThis.localStorage.removeItem")]
    private static partial void RemoveLocalStorageItem(string key);
}
