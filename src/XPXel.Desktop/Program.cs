using Avalonia;
using Dock.Settings;
using Microsoft.Extensions.DependencyInjection;
using XPXel.UI;
using XPXel.UI.Services;

namespace XPXel.Desktop;

/// <summary>
/// The Windows, Linux and macOS entry point.
/// </summary>
/// <remarks>
/// <para>
/// One project covers all three desktop operating systems. <c>UsePlatformDetect</c> selects
/// the Win32, X11 or macOS backend at run time, so shipping for another desktop OS is a
/// different publish <c>RuntimeIdentifier</c> and nothing else.
/// </para>
/// <para>
/// The head's only real responsibility is registering the services that exist on a desktop
/// but not everywhere — here, settings persisted to a file. Everything else lives in
/// <c>XPXel.UI</c> and is shared with the browser.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>The process entry point.</summary>
    /// <param name="args">Command-line arguments, passed through to Avalonia.</param>
    /// <remarks>
    /// STAThread is required by Windows: the native file dialogs and clipboard are
    /// apartment-threaded COM components and fail without it. It is ignored elsewhere.
    /// </remarks>
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// Configures Avalonia. Named exactly this and kept public because the XAML previewer
    /// and designer tooling look it up by convention.
    /// </summary>
    /// <returns>The configured builder.</returns>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure(CreateApp)
        .UsePlatformDetect()
        .WithInterFont()

        // A panel torn off into its own window is still a real top-level window, and Avalonia
        // shuts down when the last one closes. Without this, closing the editor while the
        // colour panel is floating leaves the process running with only that panel on screen.
        .CloseFloatingWindowsOnMainWindowClose(true)
        .LogToTrace();

    private static App CreateApp()
    {
        App app = new();
        app.UsePlatformServices(RegisterDesktopServices);
        return app;
    }

    private static void RegisterDesktopServices(IServiceCollection services) =>
        services.AddSingleton<ISettingsStore, FileSettingsStore>();
}
