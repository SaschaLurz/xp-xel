using System.ComponentModel;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.Primitives;
using XPXel.Core.Tools;
using XPXel.Formats;
using XPXel.UI.Services;
using XPXel.UI.ViewModels.Dialogs;

namespace XPXel.UI.ViewModels;

/// <summary>
/// The application shell: it owns the open document, the panels, the menu commands and the
/// dialog overlay.
/// </summary>
/// <remarks>
/// <para>
/// <b>One document at a time, deliberately.</b> v0.1 has a single
/// <see cref="DocumentWorkspace"/>. Everything document-specific is reached through it and
/// the panels are re-pointed with <c>Attach</c>, so adding tabs later means holding a
/// collection of workspaces and changing which one is current — not restructuring the shell.
/// </para>
/// <para>
/// <b>Where the layers are.</b> This class orchestrates; it does not implement. File
/// input and output is <see cref="DocumentFileService"/>, drawing is the tools, appearance
/// is <see cref="IThemeService"/>. If logic starts accumulating here it belongs somewhere
/// else.
/// </para>
/// </remarks>
public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    private const string DefaultDocumentName = "Untitled";

    private readonly DocumentFileService _files;
    private readonly IFileDialogService _fileDialog;
    private readonly IThemeService _themeService;
    private readonly ToolSettings _toolSettings;
    private readonly ColorSlots _colors;

    /// <summary>
    /// The handle the current document was loaded from or last saved to. Kept alongside the
    /// workspace's file path because in the browser there is no path — only a handle — and
    /// re-saving needs the handle.
    /// </summary>
    private IStorageFile? _currentFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private DocumentWorkspace? _workspace;

    [ObservableProperty]
    private ToolContext? _toolContext;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomText))]
    private double _zoom = 1.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HoveredPixelText))]
    private ImagePoint? _hoveredPixel;

    [ObservableProperty]
    private bool _showPixelGrid = true;

    [ObservableProperty]
    private int _exportScale = 1;

    /// <summary>Initialises the shell.</summary>
    /// <param name="toolbox">The toolbox panel.</param>
    /// <param name="layers">The layers panel.</param>
    /// <param name="palette">The colour panel.</param>
    /// <param name="files">Reads and writes documents.</param>
    /// <param name="fileDialog">Asks the artist to choose files.</param>
    /// <param name="themeService">Controls the light or dark appearance.</param>
    /// <param name="toolSettings">The shared tool options.</param>
    /// <param name="colors">The primary and secondary colours.</param>
    public MainViewModel(
        ToolboxViewModel toolbox,
        LayersViewModel layers,
        PaletteViewModel palette,
        DocumentFileService files,
        IFileDialogService fileDialog,
        IThemeService themeService,
        ToolSettings toolSettings,
        ColorSlots colors)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(fileDialog);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(toolSettings);
        ArgumentNullException.ThrowIfNull(colors);

        Toolbox = toolbox;
        Layers = layers;
        Palette = palette;
        _files = files;
        _fileDialog = fileDialog;
        _themeService = themeService;
        _toolSettings = toolSettings;
        _colors = colors;

        Toolbox.PropertyChanged += OnToolboxPropertyChanged;

        SetWorkspace(new DocumentWorkspace(PixelDocument.CreateNew(new ImageSize(32, 32))));
    }

    /// <summary>The toolbox and tool options.</summary>
    public ToolboxViewModel Toolbox { get; }

    /// <summary>The layers panel.</summary>
    public LayersViewModel Layers { get; }

    /// <summary>The colour and palette panel.</summary>
    public PaletteViewModel Palette { get; }

    /// <summary>The modal overlay host.</summary>
    public DialogHostViewModel Dialogs { get; } = new();

    /// <summary>The appearance options offered in the View menu.</summary>
    public static IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();

    /// <summary>The tool that should receive canvas input.</summary>
    public ITool? ActiveTool => Toolbox.ActiveTool;

    /// <summary>The artist's chosen appearance.</summary>
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

    /// <summary>The window title, including an asterisk when there are unsaved changes.</summary>
    public string Title
    {
        get
        {
            if (Workspace is not { } workspace)
            {
                return "XP-xel";
            }

            string name = string.IsNullOrEmpty(workspace.FilePath) ? DefaultDocumentName : workspace.DisplayName;
            string modified = workspace.HasUnsavedChanges ? "*" : string.Empty;
            return $"{name}{modified} — XP-xel";
        }
    }

    /// <summary>A short description of the document for the status bar.</summary>
    public string DocumentSizeText => Workspace is { } workspace
        ? $"{workspace.Document.Size.Width} × {workspace.Document.Size.Height}"
        : string.Empty;

    /// <summary>
    /// The pointer's pixel coordinate for the status bar, or an em dash when the pointer is
    /// off the canvas. A placeholder rather than an empty string keeps the status bar from
    /// visibly reflowing as the pointer moves on and off the artwork.
    /// </summary>
    public string HoveredPixelText => HoveredPixel is { } point ? $"{point.X}, {point.Y}" : "—";

    /// <summary>The current zoom for the status bar, as a percentage.</summary>
    public string ZoomText => $"{Zoom * 100:F0}%";

    /// <inheritdoc />
    public void Dispose()
    {
        Toolbox.PropertyChanged -= OnToolboxPropertyChanged;
        DetachWorkspace();
        Layers.Dispose();
        Palette.Dispose();
    }

    // ---------------------------------------------------------------------------------
    // File commands
    // ---------------------------------------------------------------------------------

    /// <summary>Creates a new document after asking for its size.</summary>
    [RelayCommand]
    private async Task NewDocumentAsync()
    {
        if (!await ConfirmDiscardChangesAsync().ConfigureAwait(true))
        {
            return;
        }

        if (await Dialogs.ShowAsync(new NewDocumentDialogViewModel()).ConfigureAwait(true) is not ImageSize size)
        {
            return;
        }

        _currentFile = null;
        SetWorkspace(new DocumentWorkspace(PixelDocument.CreateNew(size)));
        StatusMessage = $"New {size} document";
    }

    /// <summary>Opens an existing project or image.</summary>
    [RelayCommand]
    private async Task OpenAsync()
    {
        if (!await ConfirmDiscardChangesAsync().ConfigureAwait(true))
        {
            return;
        }

        IStorageFile? file = await _fileDialog
            .PickOpenFileAsync("Open", _files.ImportableFormats)
            .ConfigureAwait(true);

        if (file is null)
        {
            return;
        }

        try
        {
            PixelDocument document = await _files.ReadAsync(file).ConfigureAwait(true);

            // Only a format that round-trips everything may be re-saved in place; opening a
            // PNG and pressing Ctrl+S must not silently flatten the layers the artist then
            // added.
            bool isProject = _files.IsProjectFile(file.Name);
            _currentFile = isProject ? file : null;

            DocumentWorkspace workspace = new(document, isProject ? file.TryGetLocalPath() ?? file.Name : null);
            workspace.History.MarkSaved();
            SetWorkspace(workspace);

            StatusMessage = $"Opened {file.Name}";
        }
        catch (Exception ex) when (ex is ImageFormatException or IOException)
        {
            await ShowErrorAsync("Could not open file", ex.Message).ConfigureAwait(true);
        }
    }

    /// <summary>Saves the project, asking for a location the first time.</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_currentFile is null)
        {
            await SaveAsAsync().ConfigureAwait(true);
            return;
        }

        await WriteProjectAsync(_currentFile).ConfigureAwait(true);
    }

    /// <summary>Saves the project to a new location.</summary>
    [RelayCommand]
    private async Task SaveAsAsync()
    {
        if (Workspace is not { } workspace)
        {
            return;
        }

        IStorageFile? file = await _fileDialog
            .PickSaveFileAsync("Save Project", _files.ProjectFormats, workspace.DisplayName)
            .ConfigureAwait(true);

        if (file is not null)
        {
            await WriteProjectAsync(file).ConfigureAwait(true);
        }
    }

    /// <summary>Exports a flattened image.</summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        if (Workspace is not { } workspace)
        {
            return;
        }

        IReadOnlyList<FormatDescriptor> formats =
            [.. _files.ExportableFormats.Where(f => !f.SupportsLayers)];

        IStorageFile? file = await _fileDialog
            .PickSaveFileAsync("Export Image", formats, workspace.DisplayName)
            .ConfigureAwait(true);

        if (file is null)
        {
            return;
        }

        try
        {
            ExportOptions options = new() { Frame = workspace.ActiveFrame, Scale = ExportScale };
            await _files.WriteAsync(file, workspace.Document, options).ConfigureAwait(true);

            StatusMessage = ExportScale > 1
                ? $"Exported {file.Name} at {ExportScale}×"
                : $"Exported {file.Name}";
        }
        catch (Exception ex) when (ex is ImageFormatException or IOException)
        {
            await ShowErrorAsync("Could not export image", ex.Message).ConfigureAwait(true);
        }
    }

    // ---------------------------------------------------------------------------------
    // Edit commands
    // ---------------------------------------------------------------------------------

    /// <summary>Reverses the most recent action.</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (Workspace is { } workspace && workspace.History.Undo())
        {
            StatusMessage = "Undo";
        }
    }

    /// <summary>Reapplies the most recently undone action.</summary>
    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (Workspace is { } workspace && workspace.History.Redo())
        {
            StatusMessage = "Redo";
        }
    }

    /// <summary>Exchanges the primary and secondary colours.</summary>
    [RelayCommand]
    private void SwapColors() => _colors.Swap();

    /// <summary>Sets the brush size, used by the numeric keys.</summary>
    /// <param name="size">The size to apply.</param>
    [RelayCommand]
    private void SetBrushSize(int size) => _toolSettings.BrushSize = size;

    /// <summary>Applies an appearance choice, used by the View menu.</summary>
    /// <param name="theme">The appearance to apply.</param>
    [RelayCommand]
    private void SetTheme(AppTheme theme) => Theme = theme;

    private bool CanUndo() => Workspace?.History.CanUndo ?? false;

    private bool CanRedo() => Workspace?.History.CanRedo ?? false;

    // ---------------------------------------------------------------------------------
    // Document lifecycle
    // ---------------------------------------------------------------------------------

    private void SetWorkspace(DocumentWorkspace workspace)
    {
        DetachWorkspace();

        Workspace = workspace;
        workspace.PropertyChanged += OnWorkspacePropertyChanged;
        workspace.History.Changed += OnHistoryChanged;

        ToolContext = new ToolContext(workspace, _toolSettings, _colors);

        Layers.Attach(workspace);
        Palette.Attach(workspace);

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DocumentSizeText));
        UpdateHistoryCommands();
    }

    private void DetachWorkspace()
    {
        if (Workspace is not { } workspace)
        {
            return;
        }

        workspace.PropertyChanged -= OnWorkspacePropertyChanged;
        workspace.History.Changed -= OnHistoryChanged;
    }

    private async Task WriteProjectAsync(IStorageFile file)
    {
        if (Workspace is not { } workspace)
        {
            return;
        }

        try
        {
            await _files.WriteAsync(file, workspace.Document, ExportOptions.Default).ConfigureAwait(true);

            _currentFile = file;
            workspace.MarkSaved(file.TryGetLocalPath() ?? file.Name);
            StatusMessage = $"Saved {file.Name}";
        }
        catch (Exception ex) when (ex is ImageFormatException or IOException)
        {
            await ShowErrorAsync("Could not save project", ex.Message).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Asks whether to save before an action that would discard the document.
    /// </summary>
    /// <returns><see langword="true"/> if the caller should proceed.</returns>
    private async Task<bool> ConfirmDiscardChangesAsync()
    {
        if (Workspace is not { HasUnsavedChanges: true } workspace)
        {
            return true;
        }

        ConfirmDialogViewModel dialog = new(
            "Unsaved changes",
            $"'{workspace.DisplayName}' has unsaved changes. Save before continuing?",
            confirmText: "Save",
            cancelText: "Cancel",
            alternativeText: "Discard");

        object? result = await Dialogs.ShowAsync(dialog).ConfigureAwait(true);

        switch (result)
        {
            case ConfirmDialogViewModel.Response.Confirmed:
                await SaveAsync().ConfigureAwait(true);

                // If the save was itself cancelled the document is still modified, and
                // continuing would lose exactly what the artist just tried to keep.
                return !Workspace!.HasUnsavedChanges;

            case ConfirmDialogViewModel.Response.Alternative:
                return true;

            default:
                return false;
        }
    }

    private async Task ShowErrorAsync(string title, string message) =>
        await Dialogs.ShowAsync(new ConfirmDialogViewModel(title, message, confirmText: "OK", cancelText: null))
            .ConfigureAwait(true);

    private void OnHistoryChanged(object? sender, EventArgs e) => UpdateHistoryCommands();

    private void UpdateHistoryCommands()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(Title));
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentWorkspace.FilePath) or nameof(DocumentWorkspace.HasUnsavedChanges))
        {
            OnPropertyChanged(nameof(Title));
        }
    }

    private void OnToolboxPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ToolboxViewModel.ActiveTool))
        {
            OnPropertyChanged(nameof(ActiveTool));
        }
    }
}
