using Avalonia;
using Avalonia.Platform;
using Avalonia.Styling;

namespace XPXel.UI.Services;

/// <summary>The appearance options offered in the View menu.</summary>
public enum AppTheme
{
    /// <summary>Follow whatever the operating system is currently set to.</summary>
    System = 0,

    /// <summary>Always light.</summary>
    Light = 1,

    /// <summary>Always dark.</summary>
    Dark = 2,
}

/// <summary>
/// Chooses the application's light or dark appearance and remembers the choice.
/// </summary>
public interface IThemeService
{
    /// <summary>Raised whenever the effective appearance changes, including OS-driven changes.</summary>
    event EventHandler? ThemeChanged;

    /// <summary>The artist's choice: light, dark, or follow the system.</summary>
    AppTheme Theme { get; set; }

    /// <summary>
    /// Whether the application is currently showing dark colours, after resolving
    /// <see cref="AppTheme.System"/> against the operating system.
    /// </summary>
    bool IsDark { get; }

    /// <summary>Loads the stored preference and applies it. Called once at start-up.</summary>
    void Initialize();
}

/// <summary>
/// The default <see cref="IThemeService"/>, implemented on Avalonia's theme variants.
/// </summary>
/// <remarks>
/// <para>
/// <b>How "follow the system" works.</b> Setting the application's
/// <see cref="Application.RequestedThemeVariant"/> to <see cref="ThemeVariant.Default"/>
/// tells Avalonia to resolve the variant from the platform, and Avalonia updates it live
/// when the operating system's setting changes. There is therefore nothing to poll: the
/// service subscribes to <see cref="IPlatformSettings.ColorValuesChanged"/> only so that the
/// rest of the application can be told, for example to redraw the canvas chrome.
/// </para>
/// <para>
/// <b>Colours belong in resources, not here.</b> This service switches a variant; it never
/// decides what a colour is. Every brush in the application comes from the semantic tokens
/// in <c>Themes/Tokens.axaml</c>, which are defined once per variant. Adding a third theme
/// later means adding a resource dictionary, not editing views.
/// </para>
/// </remarks>
public sealed class ThemeService : IThemeService
{
    /// <summary>The settings key under which the preference is stored.</summary>
    public const string SettingsKey = "appearance.theme";

    private readonly ISettingsStore _settings;
    private AppTheme _theme = AppTheme.System;

    /// <summary>Initialises the service.</summary>
    /// <param name="settings">Where the preference is persisted.</param>
    public ThemeService(ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    /// <inheritdoc />
    public event EventHandler? ThemeChanged;

    /// <inheritdoc />
    public AppTheme Theme
    {
        get => _theme;
        set
        {
            if (_theme == value)
            {
                return;
            }

            _theme = value;
            _settings.SetValue(SettingsKey, value.ToString());
            Apply();
        }
    }

    /// <inheritdoc />
    public bool IsDark => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    /// <inheritdoc />
    public void Initialize()
    {
        if (Enum.TryParse(_settings.GetValue(SettingsKey), ignoreCase: true, out AppTheme stored))
        {
            _theme = stored;
        }

        if (Application.Current?.PlatformSettings is { } platformSettings)
        {
            platformSettings.ColorValuesChanged += OnPlatformColorValuesChanged;
        }

        Apply();
    }

    private void Apply()
    {
        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant = _theme switch
            {
                AppTheme.Light => ThemeVariant.Light,
                AppTheme.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlatformColorValuesChanged(object? sender, PlatformColorValues e)
    {
        // Only meaningful while following the system; an explicit choice is unaffected by
        // the OS changing.
        if (_theme == AppTheme.System)
        {
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
