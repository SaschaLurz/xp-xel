using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using XPXel.UI.Services;
using XPXel.UI.ViewModels;
using XPXel.UI.Views;

namespace XPXel.UI;

/// <summary>
/// The Avalonia application: builds the service container and shows the shell.
/// </summary>
/// <remarks>
/// <para>
/// <b>One class, every platform.</b> Avalonia reports how the application was launched
/// through its lifetime object: a desktop host gets a window, while the browser and mobile
/// get a single view with no window at all. Both hand the same
/// <see cref="MainViewModel"/> to the same <see cref="MainView"/>, so the entire user
/// interface is shared and only the frame around it differs.
/// </para>
/// <para>
/// Platform heads customise behaviour by passing registrations to
/// <see cref="Initialize(Action{IServiceCollection})"/> rather than by subclassing.
/// </para>
/// </remarks>
public partial class App : Application
{
    private Action<IServiceCollection>? _configurePlatformServices;
    private ServiceProvider? _services;

    /// <summary>
    /// Supplies platform-specific service registrations. A head calls this on its
    /// <see cref="App"/> instance before Avalonia starts.
    /// </summary>
    /// <param name="configurePlatformServices">Registrations applied after the defaults.</param>
    public void UsePlatformServices(Action<IServiceCollection> configurePlatformServices) =>
        _configurePlatformServices = configurePlatformServices;

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        _services = AppServices.Build(_configurePlatformServices);

        // The theme has to be applied before the shell is created so the first frame is
        // already in the right colours rather than flashing light and then going dark.
        _services.GetRequiredService<IThemeService>().Initialize();

        MainViewModel viewModel = _services.GetRequiredService<MainViewModel>();

        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new MainWindow { DataContext = viewModel };
                break;

            case ISingleViewApplicationLifetime singleView:
                singleView.MainView = new MainView { DataContext = viewModel };
                break;

            default:
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
