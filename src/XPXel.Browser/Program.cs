using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Browser;
using Microsoft.Extensions.DependencyInjection;
using XPXel.UI;
using XPXel.UI.Services;

[assembly: SupportedOSPlatform("browser")]

namespace XPXel.Browser;

/// <summary>
/// The WebAssembly entry point.
/// </summary>
/// <remarks>
/// <para>
/// Avalonia attaches to a single HTML element — the <c>#out</c> div in
/// <c>wwwroot/index.html</c> — and renders the entire application into it. Nothing about
/// the user interface differs from the desktop build; only this file and the static web
/// assets are unique to the browser.
/// </para>
/// <para>
/// Like the desktop head, its real job is registering the services that only exist here:
/// settings backed by the browser's local storage instead of a file.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>The WebAssembly entry point.</summary>
    /// <param name="args">Arguments supplied by the JavaScript host.</param>
    /// <returns>A task that completes when the application shuts down.</returns>
    private static Task Main(string[] args) => BuildAvaloniaApp()
        .WithInterFont()
        .StartBrowserAppAsync("out");

    /// <summary>Configures Avalonia.</summary>
    /// <returns>The configured builder.</returns>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure(CreateApp);

    private static App CreateApp()
    {
        App app = new();
        app.UsePlatformServices(RegisterBrowserServices);
        return app;
    }

    private static void RegisterBrowserServices(IServiceCollection services) =>
        services.AddSingleton<ISettingsStore, BrowserSettingsStore>();
}
