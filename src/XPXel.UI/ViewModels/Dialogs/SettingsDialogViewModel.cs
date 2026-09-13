using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XPXel.Core.Animation;
using XPXel.Core.Editing;
using XPXel.UI.Services;

namespace XPXel.UI.ViewModels.Dialogs;

/// <summary>
/// Everything about the editor an artist can change, in one place, sorted by what it is about.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a dialog and not a menu.</b> The options were spread across the View menu, the
/// Animation menu, the options bar and the timeline strip, which meant that finding one
/// required already knowing which of those it lived in. They are still in all of those places,
/// because that is where they are reached from while working — but there is now one door with
/// "Settings" written on it, and everything is behind it.
/// </para>
/// <para>
/// <b>Nothing is applied on Close, because everything is applied immediately.</b> This dialog
/// holds no copies: every control binds straight to the shared settings object the editor is
/// already reading, so a checkbox ticked here changes the canvas behind the dialog while it is
/// still open. That is the right behaviour for preferences — the artist can see what the
/// setting does — and it is why there is one button rather than an OK and a Cancel.
/// </para>
/// <para>
/// <b>It owns nothing.</b> Each tab is a view over a settings object that lives for the length
/// of the session, so opening the dialog twice shows the same values and nothing has to be
/// copied in or out.
/// </para>
/// </remarks>
public sealed partial class SettingsDialogViewModel : DialogViewModel
{
    private readonly IThemeService _themeService;

    /// <summary>Which group of settings is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SectionIndex))]
    private SettingsSection _section;

    /// <summary>Initialises the dialog over the settings the editor is already using.</summary>
    /// <param name="canvas">The shared canvas view options.</param>
    /// <param name="tools">The shared tool options.</param>
    /// <param name="layers">The shared layer options.</param>
    /// <param name="onionSkin">The onion skin settings.</param>
    /// <param name="themeService">Controls the light or dark appearance.</param>
    /// <param name="section">Which group to open on.</param>
    public SettingsDialogViewModel(
        CanvasSettings canvas,
        ToolSettings tools,
        LayerSettings layers,
        OnionSkinSettings onionSkin,
        IThemeService themeService,
        SettingsSection section = SettingsSection.Appearance)
        : base("Settings")
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(onionSkin);
        ArgumentNullException.ThrowIfNull(themeService);

        Canvas = canvas;
        Tools = tools;
        Layers = layers;
        OnionSkin = onionSkin;
        _themeService = themeService;
        _section = section;
    }

    /// <summary>
    /// The showing group as a tab index, which is what a <c>TabControl</c> can be bound to.
    /// </summary>
    /// <remarks>
    /// The tabs are declared in the order <see cref="SettingsSection"/> lists them, so the two
    /// are the same number and no lookup table is needed to keep them in step.
    /// </remarks>
    public int SectionIndex
    {
        get => (int)Section;
        set => Section = (SettingsSection)Math.Clamp(value, 0, (int)SettingsSection.Animation);
    }

    /// <summary>The shared canvas view options.</summary>
    public CanvasSettings Canvas { get; }

    /// <summary>The shared tool options.</summary>
    public ToolSettings Tools { get; }

    /// <summary>The shared layer options.</summary>
    public LayerSettings Layers { get; }

    /// <summary>How neighbouring frames are ghosted under the current one.</summary>
    public OnionSkinSettings OnionSkin { get; }

    /// <summary>The brush shapes offered on the Tools tab.</summary>
    public static IReadOnlyList<XPXel.Core.Drawing.BrushShape> BrushShapes => ToolboxViewModel.BrushShapes;

    /// <summary>Whether the light or dark appearance is chosen, or the system's own.</summary>
    /// <remarks>
    /// Routed through here rather than bound at the service because the service is not
    /// observable — it applies a theme rather than holding one for anybody to watch — and the
    /// three radio buttons need something that raises a change when one of the others is
    /// picked.
    /// </remarks>
    public AppTheme Theme
    {
        get => _themeService.Theme;
        set
        {
            if (_themeService.Theme == value)
            {
                return;
            }

            _themeService.Theme = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Closes the dialog. Everything it changed has already taken effect.</summary>
    [RelayCommand]
    private void Close() => Cancel();
}

/// <summary>The groups the settings are sorted into, and the order the tabs appear in.</summary>
/// <remarks>
/// Named rather than indexed so that a caller can ask for the group it is about — the layers
/// panel's own settings button opens straight onto <see cref="Layers"/> — without depending on
/// how many tabs happen to come before it.
/// </remarks>
public enum SettingsSection
{
    /// <summary>How the editor looks: light, dark, or whatever the system says.</summary>
    Appearance,

    /// <summary>What is drawn around and over the artwork, and how the view moves.</summary>
    Canvas,

    /// <summary>What happens to a layer when artwork is placed on it.</summary>
    Layers,

    /// <summary>The defaults the drawing tools start from.</summary>
    Tools,

    /// <summary>Onion skinning and everything else about working across frames.</summary>
    Animation,
}
