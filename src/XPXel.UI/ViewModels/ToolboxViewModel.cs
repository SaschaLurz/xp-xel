using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    /// <summary>
    /// The slot this tool shares with the tools that do the same job differently, or its own
    /// identity when it has a slot to itself.
    /// </summary>
    public string GroupId =>
        ToolPresentations.For(Tool.Id).Group is { Length: > 0 } group ? group : Tool.Id;
}

/// <summary>
/// One square of the toolbox, holding either a single tool or a group of related ones.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the toolbox is squares rather than tools.</b> Fourteen tools in a two-wide panel is
/// a tall ribbon to read, and several of them are plainly the same job done differently — the
/// pen-xel and the brush, the three shapes, the three ways of marking pixels. Collapsing each
/// of those into one square, with the rest a right-click away, is what Photoshop does and what
/// anyone arriving from it already knows how to use.
/// </para>
/// <para>
/// <b>The slot remembers which member was last used</b>, so a slot set to the ellipse stays on
/// the ellipse until the artist says otherwise. That is the half of the gesture people
/// actually rely on: the flyout is opened rarely, and the square has to keep meaning whatever
/// it was left meaning.
/// </para>
/// <para>
/// Every member keeps its own single-key shortcut, so nothing about grouping makes a tool
/// harder to reach from the keyboard.
/// </para>
/// </remarks>
public sealed partial class ToolSlotViewModel : ViewModelBase
{
    /// <summary>The member currently shown on the square.</summary>
    [ObservableProperty]
    private ToolItemViewModel _current;

    /// <summary>Initialises the slot.</summary>
    /// <param name="members">The tools sharing this square, in toolbox order.</param>
    /// <param name="choose">Puts one of them in the artist's hand.</param>
    public ToolSlotViewModel(IReadOnlyList<ToolItemViewModel> members, Action<ToolItemViewModel> choose)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(choose);

        Members = members;
        _current = members[0];
        ChooseCommand = new RelayCommand<ToolItemViewModel>(tool =>
        {
            if (tool is not null)
            {
                choose(tool);
            }
        });
    }

    /// <summary>The tools sharing this square.</summary>
    public IReadOnlyList<ToolItemViewModel> Members { get; }

    /// <summary>Gets a value indicating whether this square holds more than one tool.</summary>
    public bool HasSeveral => Members.Count > 1;

    /// <summary>Puts one of this square's tools in the artist's hand.</summary>
    public IRelayCommand<ToolItemViewModel> ChooseCommand { get; }

    /// <summary>Whether a tool belongs to this square.</summary>
    /// <param name="tool">The tool to look for.</param>
    /// <returns><see langword="true"/> if it is one of the members.</returns>
    public bool Contains(ToolItemViewModel tool) => Members.Contains(tool);
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

    /// <summary>Guards the two-way trip between the chosen tool and the chosen square.</summary>
    /// <remarks>
    /// Picking a tool has to highlight its square, and highlighting a square has to pick its
    /// tool, so each side sets the other and without this the pair would chase each other
    /// round. Guarding is simpler than trying to make one of the two directions unnecessary,
    /// because both genuinely happen: the keyboard drives the first and the mouse the second.
    /// </remarks>
    private bool _isSyncingSelection;

    [ObservableProperty]
    private ToolItemViewModel? _selectedTool;

    [ObservableProperty]
    private ToolSlotViewModel? _selectedSlot;

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

        Slots = [.. BuildSlots(Tools, tool => SelectedTool = tool)];

        // Restore the tool from the previous session, falling back to the first registered
        // tool when the setting is absent or names a tool this build does not have.
        string? storedId = settings.GetValue(SettingsKey);
        _selectedTool = Tools.FirstOrDefault(t => t.Tool.Id == storedId) ?? Tools.FirstOrDefault();

        // Set directly rather than through the property, because the generated change handler
        // has not run for the field assignment above and sizes are remembered per tool.
        toolSettings.ActiveToolId = _selectedTool?.Tool.Id ?? string.Empty;
        SyncSlotsToSelectedTool();
    }

    /// <summary>The available tools, in toolbox order.</summary>
    public ObservableCollection<ToolItemViewModel> Tools { get; }

    /// <summary>The toolbox's squares, in the order they are shown.</summary>
    public ObservableCollection<ToolSlotViewModel> Slots { get; }

    /// <summary>The shared tool options, bound directly by the options bar.</summary>
    public ToolSettings ToolSettings { get; }

    /// <summary>The brush shapes offered in the brush panel.</summary>
    public static IReadOnlyList<BrushShape> BrushShapes { get; } = Enum.GetValues<BrushShape>();

    /// <summary>The tool that currently receives canvas input.</summary>
    public ITool? ActiveTool => SelectedTool?.Tool;

    /// <summary>The active tool's name, for the heading of its settings panel.</summary>
    public string ActiveToolName => SelectedTool?.DisplayName ?? "No tool";

    /// <summary>Gets a value indicating whether the brush settings apply to the active tool.</summary>
    public bool ShowsBrushOptions => HasOption(ToolOptions.Brush) || HasOption(ToolOptions.Pencil);

    /// <summary>Gets a value indicating whether the active tool lets its shape be chosen.</summary>
    public bool ShowsBrushShapeOption => HasOption(ToolOptions.Brush);

    /// <summary>
    /// Gets a value indicating whether the active tool honours softness and stabilisation.
    /// </summary>
    public bool ShowsBrushDynamics => HasOption(ToolOptions.Dynamics);

    /// <summary>Gets a value indicating whether the active tool is the always-square pencil.</summary>
    public bool IsPencilActive => HasOption(ToolOptions.Pencil);

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
    /// <remarks>
    /// <see cref="ToolOptions.Selection"/> does not count. It describes how the tool reads the
    /// modifier keys, which the cursor says and the panel has no control for, so a tool carrying
    /// only that flag would open an options panel with nothing in it.
    /// </remarks>
    public bool HasToolOptions =>
        ((SelectedTool?.Options ?? ToolOptions.None) & ~ToolOptions.Selection) != ToolOptions.None;

    /// <summary>Selects a tool by its identifier.</summary>
    /// <param name="id">The tool's stable identifier.</param>
    /// <returns><see langword="true"/> if that tool exists and is now in hand.</returns>
    /// <remarks>
    /// For the gestures that imply a tool rather than name one - double-clicking text, which
    /// needs the text tool whatever the artist was holding a moment ago.
    /// </remarks>
    public bool TrySelectById(string id)
    {
        if (Tools.FirstOrDefault(t => string.Equals(t.Tool.Id, id, StringComparison.Ordinal)) is not { } match)
        {
            return false;
        }

        SelectedTool = match;
        return true;
    }

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

    /// <summary>
    /// Sorts the tools into squares, grouping the ones that name a common leader.
    /// </summary>
    /// <remarks>
    /// A square appears where the first of its members is registered, so the order the toolbox
    /// shows is still decided entirely by <c>AppServices</c> and a tool added there turns up
    /// without this method changing.
    /// </remarks>
    private static List<ToolSlotViewModel> BuildSlots(
        IEnumerable<ToolItemViewModel> tools,
        Action<ToolItemViewModel> choose)
    {
        List<string> order = [];
        Dictionary<string, List<ToolItemViewModel>> grouped = new(StringComparer.Ordinal);

        foreach (ToolItemViewModel tool in tools)
        {
            if (!grouped.TryGetValue(tool.GroupId, out List<ToolItemViewModel>? members))
            {
                members = [];
                grouped.Add(tool.GroupId, members);
                order.Add(tool.GroupId);
            }

            members.Add(tool);
        }

        return [.. order.Select(id => new ToolSlotViewModel(grouped[id], choose))];
    }

    /// <summary>Puts the highlight on the square holding the chosen tool, and shows it there.</summary>
    private void SyncSlotsToSelectedTool()
    {
        if (_isSyncingSelection || SelectedTool is not { } tool)
        {
            return;
        }

        _isSyncingSelection = true;

        try
        {
            foreach (ToolSlotViewModel slot in Slots)
            {
                if (!slot.Contains(tool))
                {
                    continue;
                }

                slot.Current = tool;
                SelectedSlot = slot;
                return;
            }
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }

    partial void OnSelectedSlotChanged(ToolSlotViewModel? value)
    {
        // Clicking a square picks whichever of its tools it is currently showing.
        if (!_isSyncingSelection && value is not null)
        {
            SelectedTool = value.Current;
        }
    }

    partial void OnSelectedToolChanged(ToolItemViewModel? value)
    {
        SyncSlotsToSelectedTool();

        _settings.SetValue(SettingsKey, value?.Tool.Id);

        // Size is remembered per tool, so the settings object has to be told which tool the
        // options bar and the brush preview are now describing.
        ToolSettings.ActiveToolId = value?.Tool.Id ?? string.Empty;

        OnPropertyChanged(nameof(ActiveTool));
        OnPropertyChanged(nameof(ActiveToolName));
        OnPropertyChanged(nameof(ShowsBrushOptions));
        OnPropertyChanged(nameof(ShowsBrushShapeOption));
        OnPropertyChanged(nameof(ShowsBrushDynamics));
        OnPropertyChanged(nameof(IsPencilActive));
        OnPropertyChanged(nameof(ShowsShapeOptions));
        OnPropertyChanged(nameof(ShowsFillOptions));
        OnPropertyChanged(nameof(ShowsTextOptions));
        OnPropertyChanged(nameof(HasToolOptions));
    }

    private bool HasOption(ToolOptions option) => (SelectedTool?.Options ?? ToolOptions.None).HasFlag(option);
}
