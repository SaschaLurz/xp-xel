using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using XPXel.Core.Drawing;
using XPXel.Core.Editing;
using XPXel.Core.Tools;
using XPXel.UI.Media;
using XPXel.UI.Services;

namespace XPXel.UI.ViewModels;

/// <summary>
/// One entry in the toolbox.
/// </summary>
/// <param name="Tool">The tool this entry selects.</param>
public sealed record ToolItemViewModel(ITool Tool)
{
    /// <summary>The name shown in the tooltip.</summary>
    public string DisplayName => Tool.DisplayName;

    /// <summary>The tooltip body.</summary>
    public string Description => Tool.Description;

    /// <summary>
    /// The tool's icon, as vector geometry drawn by a <c>Path</c>.
    /// </summary>
    /// <remarks>
    /// The picture comes from <see cref="ToolPresentations"/> rather than from the tool
    /// itself, because a tool in <c>XPXel.Core</c> must not know what an icon is. A tool with
    /// no entry there still appears, with a neutral placeholder.
    /// </remarks>
    public Geometry Icon => ToolPresentations.IconFor(Tool.Id);

    /// <summary>The key that selects this tool, or an empty string if it has none.</summary>
    public string Shortcut => ToolPresentations.For(Tool.Id).Shortcut;

    /// <summary>The tooltip heading: the tool's name, with its shortcut key if it has one.</summary>
    public string HeaderText => Shortcut.Length > 0 ? $"{DisplayName}  ({Shortcut})" : DisplayName;

    /// <summary>Which groups of settings this tool uses.</summary>
    public ToolOptions Options => ToolPresentations.For(Tool.Id).Options;
}

/// <summary>
/// The toolbox and the options bar: which tool is active, and the settings shared by the
/// drawing tools.
/// </summary>
/// <remarks>
/// The tool list comes from <see cref="ToolRegistry"/>, which is populated by dependency
/// injection, so a newly registered tool appears here without this class changing.
/// </remarks>
public sealed partial class ToolboxViewModel : ViewModelBase
{
    /// <summary>The settings key under which the selected tool is remembered.</summary>
    public const string SettingsKey = "tools.selected";

    private readonly ISettingsStore _settings;

    [ObservableProperty]
    private ToolItemViewModel? _selectedTool;

    /// <summary>Initialises the toolbox.</summary>
    /// <param name="registry">The available tools.</param>
    /// <param name="toolSettings">The shared tool options.</param>
    /// <param name="settings">Where the selected tool is remembered between sessions.</param>
    public ToolboxViewModel(ToolRegistry registry, ToolSettings toolSettings, ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(toolSettings);
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        ToolSettings = toolSettings;

        Tools = [];
        foreach (ITool tool in registry.Tools)
        {
            Tools.Add(new ToolItemViewModel(tool));
        }

        // Restore the tool from the previous session, falling back to the first registered
        // tool when the setting is absent or names a tool this build does not have.
        string? storedId = settings.GetValue(SettingsKey);
        _selectedTool = Tools.FirstOrDefault(t => t.Tool.Id == storedId) ?? Tools.FirstOrDefault();
    }

    /// <summary>The available tools, in toolbox order.</summary>
    public ObservableCollection<ToolItemViewModel> Tools { get; }

    /// <summary>The shared tool options, bound directly by the options bar.</summary>
    public ToolSettings ToolSettings { get; }

    /// <summary>The brush shapes offered in the brush panel.</summary>
    public static IReadOnlyList<BrushShape> BrushShapes { get; } = Enum.GetValues<BrushShape>();

    /// <summary>The tool that currently receives canvas input.</summary>
    public ITool? ActiveTool => SelectedTool?.Tool;

    /// <summary>The active tool's name, for the heading of its settings panel.</summary>
    public string ActiveToolName => SelectedTool?.DisplayName ?? "No tool";

    /// <summary>Gets a value indicating whether the brush settings apply to the active tool.</summary>
    public bool ShowsBrushOptions => HasOption(ToolOptions.Brush);

    /// <summary>Gets a value indicating whether the shape fill setting applies to the active tool.</summary>
    public bool ShowsShapeOptions => HasOption(ToolOptions.Shape);

    /// <summary>Gets a value indicating whether the flood fill settings apply to the active tool.</summary>
    public bool ShowsFillOptions => HasOption(ToolOptions.Fill);

    /// <summary>Gets a value indicating whether the text defaults apply to the active tool.</summary>
    public bool ShowsTextOptions => HasOption(ToolOptions.Text);

    /// <summary>
    /// Gets a value indicating whether the active tool has any settings at all. The panel
    /// says so plainly rather than opening empty, which reads as a bug.
    /// </summary>
    public bool HasToolOptions => (SelectedTool?.Options ?? ToolOptions.None) != ToolOptions.None;

    /// <summary>Selects the tool bound to a single-key shortcut.</summary>
    /// <param name="key">The key that was pressed, as a one-character string.</param>
    /// <returns><see langword="true"/> if a tool claimed the key.</returns>
    /// <remarks>
    /// The shell calls this only once it is satisfied the keystroke is not text the artist is
    /// typing. Matching here rather than in the shell keeps the shortcut beside the icon it
    /// belongs to, in <see cref="ToolPresentations"/>.
    /// </remarks>
    public bool TrySelectByShortcut(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        ToolItemViewModel? match = Tools.FirstOrDefault(
            t => string.Equals(t.Shortcut, key, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            return false;
        }

        SelectedTool = match;
        return true;
    }

    partial void OnSelectedToolChanged(ToolItemViewModel? value)
    {
        _settings.SetValue(SettingsKey, value?.Tool.Id);

        OnPropertyChanged(nameof(ActiveTool));
        OnPropertyChanged(nameof(ActiveToolName));
        OnPropertyChanged(nameof(ShowsBrushOptions));
        OnPropertyChanged(nameof(ShowsShapeOptions));
        OnPropertyChanged(nameof(ShowsFillOptions));
        OnPropertyChanged(nameof(ShowsTextOptions));
        OnPropertyChanged(nameof(HasToolOptions));
    }

    private bool HasOption(ToolOptions option) => (SelectedTool?.Options ?? ToolOptions.None).HasFlag(option);
}
