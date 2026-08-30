using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using XPXel.Core.Drawing;
using XPXel.Core.Editing;
using XPXel.Core.Tools;
using XPXel.UI.Services;

namespace XPXel.UI.ViewModels;

/// <summary>
/// One entry in the toolbox.
/// </summary>
/// <param name="Tool">The tool this entry selects.</param>
public sealed record ToolItemViewModel(ITool Tool)
{
    /// <summary>The name shown beneath the icon.</summary>
    public string DisplayName => Tool.DisplayName;

    /// <summary>The tooltip text.</summary>
    public string Description => Tool.Description;

    /// <summary>
    /// A short glyph standing in for an icon. Icons are deliberately deferred: shipping a
    /// coherent icon set is a design task, and a lettered placeholder keeps the toolbox
    /// honest about that rather than pretending with mismatched clip art.
    /// </summary>
    public string Glyph => Tool.Id switch
    {
        PencilTool.ToolId => "✎",
        EraserTool.ToolId => "□",
        BucketFillTool.ToolId => "◆",
        EyedropperTool.ToolId => "●",
        _ => "?",
    };
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

    /// <summary>The brush shapes offered in the options bar.</summary>
    public static IReadOnlyList<BrushShape> BrushShapes { get; } = Enum.GetValues<BrushShape>();

    /// <summary>The tool that currently receives canvas input.</summary>
    public ITool? ActiveTool => SelectedTool?.Tool;

    partial void OnSelectedToolChanged(ToolItemViewModel? value)
    {
        _settings.SetValue(SettingsKey, value?.Tool.Id);
        OnPropertyChanged(nameof(ActiveTool));
    }
}
