using System.ComponentModel;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.Primitives;
using XPXel.Core.Tools;

// Both XP-xel and Dock have an ITool: one is a drawing tool, the other a docked panel.
// The alias keeps the drawing tool unqualified here, since that is the one this file means.
using ITool = XPXel.Core.Tools.ITool;
using XPXel.Formats;
using XPXel.UI.Docking;
using XPXel.UI.Services;
using XPXel.UI.ViewModels.Dialogs;

namespace XPXel.UI.ViewModels;

/// <summary>
/// The application shell: it owns the open documents, the docking layout, the panels, the
/// menu commands and the dialog overlay.
/// </summary>
/// <remarks>
/// <para>
/// <b>Many documents, one set of panels.</b> Each open image is an
/// <see cref="EditorDocument"/> tab holding its own <see cref="DocumentWorkspace"/>. The
/// layers and colour panels are single instances that are re-pointed at whichever tab is
/// active, through <c>Attach</c>. That is how every editor of this kind works, and it is
/// why switching tabs does not rebuild the panels.
/// </para>
/// <para>
/// <b>The layout belongs to the artist.</b> This class builds a default arrangement through
/// <see cref="XpDockFactory"/> on first run and restores the saved one afterwards. It does
/// not otherwise position anything: panels are moved, floated, tabbed and closed by
/// dragging, and "Reset Layout" is the way back.
/// </para>
/// <para>
/// <b>Where the work happens.</b> This class orchestrates; it does not implement. File input
/// and output is <see cref="DocumentFileService"/>, drawing is the tools, appearance is
/// <see cref="IThemeService"/>, arrangement is the docking factory. If logic starts
/// accumulating here it belongs somewhere else.
/// </para>
/// </remarks>
public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly DocumentFileService _files;
    private readonly IFileDialogService _fileDialog;
    private readonly IThemeService _themeService;
    private readonly ToolSettings _toolSettings;
    private readonly ColorSlots _colors;
    private readonly XpDockFactory _factory;
    private readonly DockLayoutStore _layoutStore;

    [ObservableProperty]
    private IRootDock? _layout;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private int _exportScale = 1;

    private EditorDocument? _activeDocument;

    /// <summary>Initialises the shell.</summary>
    /// <param name="toolbox">The toolbox panel.</param>
    /// <param name="layers">The layers panel.</param>
    /// <param name="palette">The colour panel.</param>
    /// <param name="canvasSettings">Canvas view options shared across documents.</param>
    /// <param name="files">Reads and writes documents.</param>
    /// <param name="fileDialog">Asks the artist to choose files.</param>
    /// <param name="themeService">Controls the light or dark appearance.</param>
    /// <param name="layoutStore">Remembers the panel arrangement between sessions.</param>
    /// <param name="toolSettings">The shared tool options.</param>
    /// <param name="colors">The primary and secondary colours.</param>
    public MainViewModel(
        ToolboxViewModel toolbox,
        LayersViewModel layers,
        PaletteViewModel palette,
        CanvasSettings canvasSettings,
        DocumentFileService files,
        IFileDialogService fileDialog,
        IThemeService themeService,
        DockLayoutStore layoutStore,
        ToolSettings toolSettings,
        ColorSlots colors)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(canvasSettings);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(fileDialog);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(layoutStore);
        ArgumentNullException.ThrowIfNull(toolSettings);
        ArgumentNullException.ThrowIfNull(colors);

        Toolbox = toolbox;
        Layers = layers;
        Palette = palette;
        CanvasSettings = canvasSettings;
        _files = files;
        _fileDialog = fileDialog;
        _themeService = themeService;
        _layoutStore = layoutStore;
        _toolSettings = toolSettings;
        _colors = colors;

        Toolbox.PropertyChanged += OnToolboxPropertyChanged;

        _factory = new XpDockFactory(toolbox, layers, palette);
        _factory.ActiveDockableChanged += OnActiveDockableChanged;

        BuildLayout(restoreSaved: true);
        AddDocument(new DocumentWorkspace(PixelDocument.CreateNew(new ImageSize(32, 32))), file: null);
    }

    /// <summary>The toolbox and tool options.</summary>
    public ToolboxViewModel Toolbox { get; }

    /// <summary>The layers panel.</summary>
    public LayersViewModel Layers { get; }

    /// <summary>The colour and palette panel.</summary>
    public PaletteViewModel Palette { get; }

    /// <summary>Canvas view options shared by every open document.</summary>
    public CanvasSettings CanvasSettings { get; }

    /// <summary>The modal overlay host.</summary>
    public DialogHostViewModel Dialogs { get; } = new();

    /// <summary>The docking factory, which the <c>DockControl</c> needs to route operations.</summary>
    public IFactory Factory => _factory;

    /// <summary>The appearance options offered in the View menu.</summary>
    public static IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();

    /// <summary>The tool that should receive canvas input.</summary>
    public ITool? ActiveTool => Toolbox.ActiveTool;

    /// <summary>The document tab currently in front, or <see langword="null"/> if none.</summary>
    public EditorDocument? ActiveDocument
    {
        get => _activeDocument;
        private set
        {
            if (ReferenceEquals(_activeDocument, value))
            {
                return;
            }

            if (_activeDocument is { } previous)
            {
                previous.PropertyChanged -= OnActiveDocumentPropertyChanged;
                previous.Workspace.History.Changed -= OnHistoryChanged;
            }

            _activeDocument = value;

            if (_activeDocument is { } current)
            {
                current.PropertyChanged += OnActiveDocumentPropertyChanged;
                current.Workspace.History.Changed += OnHistoryChanged;
            }

            // The panels follow the active tab rather than being rebuilt per tab.
            Layers.Attach(_activeDocument?.Workspace);
            Palette.Attach(_activeDocument?.Workspace);

            OnPropertyChanged();
            OnPropertyChanged(nameof(Workspace));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(DocumentSizeText));
            OnPropertyChanged(nameof(ZoomText));
            OnPropertyChanged(nameof(HoveredPixelText));
            UpdateHistoryCommands();
            NotifyDocumentCommands();
        }
    }

    /// <summary>The active document's editing state, or <see langword="null"/> if none.</summary>
    public DocumentWorkspace? Workspace => ActiveDocument?.Workspace;

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
            if (ActiveDocument is not { } document)
            {
                return "XP-xel";
            }

            string modified = document.Workspace.HasUnsavedChanges ? "*" : string.Empty;
            return $"{document.Title}{modified} — XP-xel";
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
    public string HoveredPixelText => ActiveDocument?.HoveredPixel is { } point ? $"{point.X}, {point.Y}" : "—";

    /// <summary>The current zoom for the status bar, as a percentage.</summary>
    public string ZoomText => $"{(ActiveDocument?.Zoom ?? 1.0) * 100:F0}%";

    /// <summary>
    /// Records the current arrangement so the next session opens with it.
    /// </summary>
    /// <remarks>
    /// Called while the main window is closing rather than on application exit, and the
    /// difference matters. Torn-off panels are real windows, and they are closed <i>before</i>
    /// the application exits — by then a panel the artist had floating has already been
    /// returned to the docked tree, and saving at that point would quietly forget that it was
    /// floating at all.
    /// </remarks>
    public void SaveLayout()
    {
        if (Layout is { } layout)
        {
            _layoutStore.Save(layout);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Toolbox.PropertyChanged -= OnToolboxPropertyChanged;
        _factory.ActiveDockableChanged -= OnActiveDockableChanged;

        ActiveDocument = null;
        Layers.Dispose();
        Palette.Dispose();
    }

    // ---------------------------------------------------------------------------------
    // File commands
    // ---------------------------------------------------------------------------------

    /// <summary>Creates a new document in a new tab, after asking for its size.</summary>
    [RelayCommand]
    private async Task NewDocumentAsync()
    {
        if (await Dialogs.ShowAsync(new NewDocumentDialogViewModel()).ConfigureAwait(true) is not ImageSize size)
        {
            return;
        }

        AddDocument(new DocumentWorkspace(PixelDocument.CreateNew(size)), file: null);
        StatusMessage = $"New {size} document";
    }

    /// <summary>Opens an existing project or image in a new tab.</summary>
    [RelayCommand]
    private async Task OpenAsync()
    {
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

            DocumentWorkspace workspace = new(document, isProject ? file.TryGetLocalPath() ?? file.Name : null);
            workspace.History.MarkSaved();

            AddDocument(workspace, isProject ? file : null);
            StatusMessage = $"Opened {file.Name}";
        }
        catch (Exception ex) when (ex is ImageFormatException or IOException)
        {
            await ShowErrorAsync("Could not open file", ex.Message).ConfigureAwait(true);
        }
    }

    /// <summary>Saves the active project, asking for a location the first time.</summary>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task SaveAsync()
    {
        if (ActiveDocument is not { } document)
        {
            return;
        }

        if (document.File is null)
        {
            await SaveAsAsync().ConfigureAwait(true);
            return;
        }

        await WriteProjectAsync(document, document.File).ConfigureAwait(true);
    }

    /// <summary>Saves the active project to a new location.</summary>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task SaveAsAsync()
    {
        if (ActiveDocument is not { } document)
        {
            return;
        }

        IStorageFile? file = await _fileDialog
            .PickSaveFileAsync("Save Project", _files.ProjectFormats, document.Workspace.DisplayName)
            .ConfigureAwait(true);

        if (file is not null)
        {
            await WriteProjectAsync(document, file).ConfigureAwait(true);
        }
    }

    /// <summary>Exports the active document as a flattened image.</summary>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task ExportAsync()
    {
        if (ActiveDocument is not { Workspace: { } workspace })
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

    /// <summary>Closes the active document tab.</summary>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void CloseDocument()
    {
        if (ActiveDocument is { } document)
        {
            _factory.CloseDockable(document);
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

    // ---------------------------------------------------------------------------------
    // View commands
    // ---------------------------------------------------------------------------------

    /// <summary>Asks the active canvas to change its zoom.</summary>
    /// <param name="request">What the canvas should do.</param>
    [RelayCommand]
    private void Zoom(CanvasZoomRequest request) => ActiveDocument?.RequestZoom(request);

    /// <summary>Brings a panel back, whether it is closed, hidden or simply behind another.</summary>
    /// <param name="panelId">The panel's identity, as given in <see cref="XpDockFactory"/>.</param>
    /// <remarks>
    /// This is what makes closing a panel safe. Without it, clicking the × on the layers
    /// panel would remove it with no way back short of resetting the whole layout.
    /// </remarks>
    [RelayCommand]
    private void ShowPanel(string panelId)
    {
        if (Layout is not { } layout)
        {
            return;
        }

        PanelTool? panel = XpDockFactory.Enumerate(layout).OfType<PanelTool>()
            .FirstOrDefault(p => p.Id == panelId);

        if (panel is not null)
        {
            _factory.SetActiveDockable(panel);
            return;
        }

        // The panel was closed outright, so it no longer exists anywhere in the tree. The
        // honest repair is to rebuild the default arrangement rather than to invent a place
        // to put it back.
        ResetLayout();
    }

    /// <summary>Discards the saved arrangement and rebuilds the default one.</summary>
    [RelayCommand]
    private void ResetLayout()
    {
        _layoutStore.Clear();

        List<EditorDocument> documents = Layout is { } layout
            ? [.. XpDockFactory.Enumerate(layout).OfType<EditorDocument>()]
            : [];

        BuildLayout(restoreSaved: false);

        // Open documents survive a reset: losing unsaved artwork because a panel was in the
        // wrong place would be an unforgivable trade.
        foreach (EditorDocument document in documents)
        {
            AttachDocument(document);
        }

        StatusMessage = "Layout reset";
    }

    // ---------------------------------------------------------------------------------
    // Documents
    // ---------------------------------------------------------------------------------

    /// <summary>Creates a tab for a workspace and brings it to the front.</summary>
    private void AddDocument(DocumentWorkspace workspace, IStorageFile? file)
    {
        EditorDocument document = new(workspace, new ToolContext(workspace, _toolSettings, _colors), CanvasSettings, Toolbox)
        {
            File = file,
        };

        AttachDocument(document);
    }

    private void AttachDocument(EditorDocument document)
    {
        document.ConfirmCloseAsync = ConfirmCloseDocumentAsync;

        if (_factory.DocumentDock is not { } dock)
        {
            return;
        }

        _factory.AddDockable(dock, document);
        _factory.SetActiveDockable(document);
        ActiveDocument = document;
    }

    private void BuildLayout(bool restoreSaved)
    {
        IRootDock? restored = restoreSaved ? _layoutStore.Load() : null;

        if (restored is not null)
        {
            // A restored layout carries structure but no view models, so the live panels are
            // put back before it is initialised and anything tries to render them, and any
            // document shells left over from the last session are dropped.
            XpDockFactory.RestorePanelContent(restored, Toolbox, Layers, Palette);
            XpDockFactory.RemoveRestoredDocuments(restored);
        }

        IRootDock layout = restored ?? _factory.CreateLayout();
        _factory.InitLayout(layout);

        // Bring back any panels that were floating when the editor last closed. Without this
        // they exist in the restored tree but have no window on screen.
        if (layout.Windows is { Count: > 0 } && layout.ShowWindows.CanExecute(null))
        {
            layout.ShowWindows.Execute(null);
        }

        Layout = layout;
    }

    private async Task ConfirmCloseDocumentAsync(EditorDocument document)
    {
        ConfirmDialogViewModel dialog = new(
            "Unsaved changes",
            $"'{document.Title}' has unsaved changes. Save before closing?",
            confirmText: "Save",
            cancelText: "Cancel",
            alternativeText: "Discard");

        object? result = await Dialogs.ShowAsync(dialog).ConfigureAwait(true);

        switch (result)
        {
            case ConfirmDialogViewModel.Response.Confirmed:
                await SaveDocumentAsync(document).ConfigureAwait(true);

                // If the save was itself cancelled the document is still modified, and
                // closing would lose exactly what the artist just tried to keep.
                if (document.Workspace.HasUnsavedChanges)
                {
                    return;
                }

                break;

            case ConfirmDialogViewModel.Response.Alternative:
                break;

            default:
                return;
        }

        document.CloseConfirmed = true;
        _factory.CloseDockable(document);
    }

    private async Task SaveDocumentAsync(EditorDocument document)
    {
        IStorageFile? file = document.File;

        if (file is null)
        {
            file = await _fileDialog
                .PickSaveFileAsync("Save Project", _files.ProjectFormats, document.Workspace.DisplayName)
                .ConfigureAwait(true);

            if (file is null)
            {
                return;
            }
        }

        await WriteProjectAsync(document, file).ConfigureAwait(true);
    }

    private async Task WriteProjectAsync(EditorDocument document, IStorageFile file)
    {
        try
        {
            await _files.WriteAsync(file, document.Workspace.Document, ExportOptions.Default).ConfigureAwait(true);

            document.File = file;
            document.Workspace.MarkSaved(file.TryGetLocalPath() ?? file.Name);
            StatusMessage = $"Saved {file.Name}";
        }
        catch (Exception ex) when (ex is ImageFormatException or IOException)
        {
            await ShowErrorAsync("Could not save project", ex.Message).ConfigureAwait(true);
        }
    }

    private async Task ShowErrorAsync(string title, string message) =>
        await Dialogs.ShowAsync(new ConfirmDialogViewModel(title, message, confirmText: "OK", cancelText: null))
            .ConfigureAwait(true);

    // ---------------------------------------------------------------------------------
    // Change plumbing
    // ---------------------------------------------------------------------------------

    private bool HasDocument() => ActiveDocument is not null;

    private bool CanUndo() => Workspace?.History.CanUndo ?? false;

    private bool CanRedo() => Workspace?.History.CanRedo ?? false;

    private void OnActiveDockableChanged(object? sender, Dock.Model.Core.Events.ActiveDockableChangedEventArgs e)
    {
        // Selecting a panel also raises this, and a panel is not a document — leaving the
        // active document alone in that case is what keeps the layers panel pointed at the
        // image the artist is drawing on rather than blanking when they click the toolbox.
        if (e.Dockable is EditorDocument document)
        {
            ActiveDocument = document;
            return;
        }

        // A closed tab leaves no active dockable behind, so the shell has to find the next
        // one itself or fall back to having none.
        if (e.Dockable is null && Layout is { } layout)
        {
            ActiveDocument = XpDockFactory.Enumerate(layout).OfType<EditorDocument>().FirstOrDefault();
        }
    }

    private void OnActiveDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(EditorDocument.Zoom):
                OnPropertyChanged(nameof(ZoomText));
                break;

            case nameof(EditorDocument.HoveredPixel):
                OnPropertyChanged(nameof(HoveredPixelText));
                break;

            case nameof(EditorDocument.Title):
            case nameof(EditorDocument.IsModified):
                OnPropertyChanged(nameof(Title));
                break;

            default:
                break;
        }
    }

    private void OnHistoryChanged(object? sender, EventArgs e) => UpdateHistoryCommands();

    private void UpdateHistoryCommands()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(Title));
    }

    private void NotifyDocumentCommands()
    {
        SaveCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        CloseDocumentCommand.NotifyCanExecuteChanged();
    }

    private void OnToolboxPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ToolboxViewModel.ActiveTool))
        {
            OnPropertyChanged(nameof(ActiveTool));
        }
    }
}
