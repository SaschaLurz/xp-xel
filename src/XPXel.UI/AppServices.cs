using Microsoft.Extensions.DependencyInjection;
using XPXel.Core.Editing;
using XPXel.Core.Tools;
using XPXel.Formats;
using XPXel.Formats.Png;
using XPXel.Formats.Project;
using XPXel.UI.Services;
using XPXel.UI.ViewModels;

namespace XPXel.UI;

/// <summary>
/// The composition root: the one place that decides which concrete types the application
/// runs with.
/// </summary>
/// <remarks>
/// <para>
/// <b>This file is the extension point for most new features.</b> A new tool, a new file
/// format or a new panel is a class plus one line here. Nothing else needs editing, because
/// the registries and view models take their contents from the container.
/// </para>
/// <para>
/// <b>Registration order is the UI order.</b> The toolbox lists tools, and the file dialogs
/// list formats, in the order they are registered below. That keeps those decisions visible
/// and deliberate instead of depending on reflection or alphabetical accident.
/// </para>
/// <para>
/// <b>Platform overrides.</b> Each platform head passes a callback that runs last, so a head
/// can replace any default simply by registering its own implementation — the desktop head
/// does exactly that for <see cref="ISettingsStore"/>. Microsoft's container resolves the
/// most recent registration for a single-service request.
/// </para>
/// </remarks>
public static class AppServices
{
    /// <summary>Builds the application's service provider.</summary>
    /// <param name="configurePlatform">
    /// Platform-specific registrations, applied after the defaults so they take precedence.
    /// </param>
    /// <returns>The configured container.</returns>
    public static ServiceProvider Build(Action<IServiceCollection>? configurePlatform = null)
    {
        ServiceCollection services = new();

        AddEditingState(services);
        AddTools(services);
        AddFormats(services);
        AddPlatformDefaults(services);
        AddViewModels(services);

        configurePlatform?.Invoke(services);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Application-wide editing state. These are singletons because they must survive
    /// switching documents: an artist does not expect their colour or brush size to reset
    /// when they open a different file.
    /// </summary>
    private static void AddEditingState(IServiceCollection services)
    {
        services.AddSingleton<ToolSettings>();
        services.AddSingleton<ColorSlots>();
    }

    /// <summary>
    /// The tools, in the order the toolbox shows them. Add a new tool here and it appears
    /// in the UI with no other change.
    /// </summary>
    private static void AddTools(IServiceCollection services)
    {
        services.AddSingleton<ITool, PencilTool>();
        services.AddSingleton<ITool, EraserTool>();
        services.AddSingleton<ITool, BucketFillTool>();
        services.AddSingleton<ITool, EyedropperTool>();

        services.AddSingleton<ToolRegistry>();
    }

    /// <summary>
    /// The file formats, in the order the file dialogs show them. The project format comes
    /// first because it is the one that loses nothing.
    /// </summary>
    private static void AddFormats(IServiceCollection services)
    {
        // Each codec implements both interfaces, so it is registered once as a concrete
        // type and then surfaced under both. Registering it twice would create two
        // instances, which is harmless today but exactly the kind of thing that stops being
        // harmless once a codec holds state.
        services.AddSingleton<XpxelProjectSerializer>();
        services.AddSingleton<PngCodec>();

        services.AddSingleton<IImageImporter>(sp => sp.GetRequiredService<XpxelProjectSerializer>());
        services.AddSingleton<IImageImporter>(sp => sp.GetRequiredService<PngCodec>());
        services.AddSingleton<IImageExporter>(sp => sp.GetRequiredService<XpxelProjectSerializer>());
        services.AddSingleton<IImageExporter>(sp => sp.GetRequiredService<PngCodec>());

        services.AddSingleton<ImageFormatRegistry>();
        services.AddSingleton<DocumentFileService>();
    }

    /// <summary>
    /// Cross-platform service implementations. A head may replace any of these.
    /// </summary>
    private static void AddPlatformDefaults(IServiceCollection services)
    {
        services.AddSingleton<IFileDialogService, StorageFileDialogService>();
        services.AddSingleton<IThemeService, ThemeService>();

        // Forgetful by default. A head that can persist settings registers its own store.
        services.AddSingleton<ISettingsStore, InMemorySettingsStore>();
    }

    private static void AddViewModels(IServiceCollection services)
    {
        services.AddSingleton<ToolboxViewModel>();
        services.AddSingleton<LayersViewModel>();
        services.AddSingleton<PaletteViewModel>();
        services.AddSingleton<MainViewModel>();
    }
}
