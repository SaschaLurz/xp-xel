using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.History;
using XPXel.Core.Primitives;
using XPXel.Core.Selection;
using XPXel.Core.Tools;

// Both XP-xel and Dock have an ITool: one is a drawing tool, the other a docked panel.
// The alias keeps the drawing tool unqualified here, since that is the one this file means.
using ITool = XPXel.Core.Tools.ITool;
using XPXel.Formats;
using XPXel.Formats.Animation;
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
    private readonly AutosaveService _autosave;
    private readonly AnimationExportService _animationExport;
    private readonly AnimationExportRegistry _animationFormats;

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
    /// <param name="brushPanel">The brush panel.</param>
    /// <param name="textPanel">The text panel.</param>
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
        BrushPanelViewModel brushPanel,
        TextPanelViewModel textPanel,
        TimelineViewModel timeline,
        PreviewPanelViewModel preview,
        CanvasSettings canvasSettings,
        DocumentFileService files,
        IFileDialogService fileDialog,
        IThemeService themeService,
        DockLayoutStore layoutStore,
        ToolSettings toolSettings,
        ColorSlots colors,
        AnimationExportService animationExport,
        AnimationExportRegistry animationFormats,
        AutosaveService autosave,
        HistoryPanelViewModel history)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(brushPanel);
        ArgumentNullException.ThrowIfNull(textPanel);
        ArgumentNullException.ThrowIfNull(canvasSettings);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(fileDialog);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(layoutStore);
        ArgumentNullException.ThrowIfNull(toolSettings);
        ArgumentNullException.ThrowIfNull(colors);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(animationExport);
        ArgumentNullException.ThrowIfNull(animationFormats);
        ArgumentNullException.ThrowIfNull(autosave);
        ArgumentNullException.ThrowIfNull(history);

        Toolbox = toolbox;
        Layers = layers;
        Palette = palette;
        BrushPanel = brushPanel;
        TextPanel = textPanel;
        CanvasSettings = canvasSettings;
        _files = files;
        _fileDialog = fileDialog;
        _themeService = themeService;
        _layoutStore = layoutStore;
        _toolSettings = toolSettings;
        _colors = colors;
        _animationExport = animationExport;
        _animationFormats = animationFormats;
        _autosave = autosave;

        Timeline = timeline;
        Preview = preview;
        History = history;

        Toolbox.PropertyChanged += OnToolboxPropertyChanged;

        _factory = new XpDockFactory(toolbox, layers, palette, brushPanel, textPanel, timeline, preview, history);
        _factory.ActiveDockableChanged += OnActiveDockableChanged;

        // Closing a panel with the x on its tab, or dragging it out and shutting the window,
        // has to move the checkmark in the View menu as surely as unticking it does.
        _factory.DockableClosed += OnDockableCountChanged;
        _factory.DockableAdded += OnDockableCountChanged;
        _factory.DockableRemoved += OnDockableCountChanged;

        Panels = [.. _factory.Definitions.Select(d => new PanelToggleViewModel(d.Id, d.Title, OnPanelToggled))];

        BuildLayout(restoreSaved: true);
        AddDocument(new DocumentWorkspace(PixelDocument.CreateNew(new ImageSize(32, 32))), file: null);

        // Started last, once there is a layout to enumerate documents from.
        _autosave.Completed += OnAutosaveCompleted;
        _autosave.Start(CollectWorkspaces);
    }

    /// <summary>The toolbox and tool options.</summary>
    public ToolboxViewModel Toolbox { get; }

    /// <summary>The layers panel.</summary>
    public LayersViewModel Layers { get; }

    /// <summary>The colour and palette panel.</summary>
    public PaletteViewModel Palette { get; }

    /// <summary>The brush panel: size, shape, softness and stabilisation.</summary>
    public BrushPanelViewModel BrushPanel { get; }

    /// <summary>The text panel: the words of a text layer and the typography they use.</summary>
    public TextPanelViewModel TextPanel { get; }

    /// <summary>The timeline: frames, layers and the named animations over them.</summary>
    public TimelineViewModel Timeline { get; }

    /// <summary>The animation preview.</summary>
    public PreviewPanelViewModel Preview { get; }

    /// <summary>The undo history, and the depth limit that governs it.</summary>
    public HistoryPanelViewModel History { get; }

    /// <summary>
    /// One checkable entry per panel for the View menu, showing which are on screen.
    /// </summary>
    public ObservableCollection<PanelToggleViewModel> Panels { get; }

    /// <summary>Canvas view options shared by every open document.</summary>
    public CanvasSettings CanvasSettings { get; }

    /// <summary>The modal overlay host.</summary>
    public DialogHostViewModel Dialogs { get; } = new();

    /// <summary>The docking factory, which the <c>DockControl</c> needs to route operations.</summary>
    public IFactory Factory => _factory;

    /// <summary>
    /// Gets a value indicating whether this platform keeps autosaved backups, which is what
    /// decides whether the menu entry that opens their folder does anything.
    /// </summary>
    public bool IsAutosaveAvailable => _autosave.IsAvailable;

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
                previous.Workspace.PropertyChanged -= OnWorkspacePropertyChanged;
                previous.Workspace.Document.Changed -= OnDocumentChanged;
            }

            _activeDocument = value;

            if (_activeDocument is { } current)
            {
                current.PropertyChanged += OnActiveDocumentPropertyChanged;
                current.Workspace.History.Changed += OnHistoryChanged;
                current.Workspace.PropertyChanged += OnWorkspacePropertyChanged;
                current.Workspace.Document.Changed += OnDocumentChanged;
            }

            // The panels follow the active tab rather than being rebuilt per tab.
            Layers.Attach(_activeDocument?.Workspace);
            Palette.Attach(_activeDocument?.Workspace);
            TextPanel.Attach(_activeDocument?.Workspace);
            Timeline.Attach(_activeDocument?.Workspace);
            Preview.Attach(_activeDocument?.Workspace);
            History.Attach(_activeDocument?.Workspace);

            OnPropertyChanged();
            OnPropertyChanged(nameof(Workspace));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(DocumentSizeText));
            OnPropertyChanged(nameof(ZoomText));
            OnPropertyChanged(nameof(HoveredPixelText));
            OnPropertyChanged(nameof(HoveredXText));
            OnPropertyChanged(nameof(HoveredYText));
            NotifyEditabilityChanged();
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
    /// The pointer's pixel coordinate for the status bar, or dashes when the pointer is off
    /// the canvas. Placeholders rather than an empty string keep the status bar from visibly
    /// reflowing as the pointer moves on and off the artwork.
    /// </summary>
    /// <remarks>
    /// Counted from one, to agree with the rulers along the canvas edges. Everything that
    /// computes with coordinates keeps counting from zero; the conversion happens here and in
    /// <c>CanvasRulerControl</c>, the two places a number is shown to a person.
    /// </remarks>
    public string HoveredPixelText =>
        ActiveDocument?.HoveredPixel is { } point ? $"x {point.X + 1}, y {point.Y + 1}" : "x —, y —";

    /// <summary>The pointer's column, for the status bar's own box.</summary>
    /// <remarks>
    /// Split from <see cref="HoveredPixelText"/> and shown in a fixed-width box because a
    /// single run of text re-flows as the numbers change width: going from 9 to 10 shunts the
    /// y label sideways, and at drawing speed the whole readout appears to twitch. Two boxes
    /// wide enough for the numbers they will hold do not move at all.
    /// </remarks>
    public string HoveredXText => Format(ActiveDocument?.HoveredPixel?.X);

    /// <summary>The pointer's row, for the status bar's own box.</summary>
    public string HoveredYText => Format(ActiveDocument?.HoveredPixel?.Y);

    /// <summary>The current zoom for the status bar, as a percentage.</summary>
    public string ZoomText => $"{(ActiveDocument?.Zoom ?? 1.0) * 100:F0}%";

    /// <summary>
    /// Renders one coordinate for the status bar, counting from one the way the rulers do.
    /// </summary>
    private static string Format(int? coordinate) =>
        coordinate is { } value ? (value + 1).ToString(CultureInfo.CurrentCulture) : "—";

    /// <summary>
    /// Why the drawing tools would currently do nothing, or an empty string when they will
    /// work.
    /// </summary>
    /// <remarks>
    /// Every one of these states used to fail in the same way: the artist draws, nothing
    /// appears, and there is no clue why. A tool cannot say so itself — it is handed a context
    /// and told to draw, and simply gets no session back — so the shell answers instead, in
    /// the one place the artist is already looking for information about the document.
    /// </remarks>
    public string EditabilityWarning
    {
        get
        {
            if (Workspace is not { } workspace)
            {
                return string.Empty;
            }

            return workspace.ActiveLayer switch
            {
                null => "No layer selected — add one to start drawing.",
                TextLayer => "Text layer: edit it with the Text tool and the Text panel. Drawing tools do not affect it.",
                { IsLocked: true } => "This layer is locked, so drawing tools do nothing.",
                { IsEffectivelyVisible: false } => "This layer is hidden, so drawing tools do nothing.",
                _ => string.Empty,
            };
        }
    }

    /// <summary>Whether <see cref="EditabilityWarning"/> has anything to say.</summary>
    public bool HasEditabilityWarning => EditabilityWarning.Length > 0;

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
        _factory.DockableClosed -= OnDockableCountChanged;
        _factory.DockableAdded -= OnDockableCountChanged;
        _factory.DockableRemoved -= OnDockableCountChanged;

        ActiveDocument = null;
        Layers.Dispose();
        Palette.Dispose();
        TextPanel.Dispose();
        History.Dispose();
        BrushPanel.Dispose();
        Timeline.Dispose();
        Preview.Dispose();
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

        // Half-typed text is still text the artist expects to be saved, and so are pixels
        // still being dragged. Ctrl+S is one of the few shortcuts that reaches the shell while
        // the caret is on the canvas.
        document.Workspace.CommitTextEdit();
        document.Workspace.CommitTransform();

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

        workspace.CommitTextEdit();
        workspace.CommitTransform();

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

    /// <summary>Exports the animation as a sprite sheet, a frame sequence, a GIF or an APNG.</summary>
    /// <remarks>
    /// Separate from <see cref="ExportAsync"/>, which writes one still image. Merging the two
    /// would mean a dialog whose options mostly do not apply, and the two are reached at quite
    /// different moments: a still export is "give me this picture", an animation export is
    /// "give me this into my game".
    /// </remarks>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task ExportAnimationAsync()
    {
        if (ActiveDocument is not { Workspace: { } workspace })
        {
            return;
        }

        workspace.CommitTextEdit();
        workspace.CommitTransform();
        Timeline.Playback.Stop();

        ExportAnimationDialogViewModel dialog = new(
            workspace.Document,
            _animationFormats.Exporters,
            workspace.DisplayName);

        if (await Dialogs.ShowAsync(dialog).ConfigureAwait(true) is not AnimationExportRequest request)
        {
            return;
        }

        try
        {
            AnimationExportResult? result = await _animationExport
                .ExportAsync(workspace.Document, new AnimationExportRequestData(request.Exporter, request.Options))
                .ConfigureAwait(true);

            if (result is { } written)
            {
                StatusMessage = written.FileCount == 1
                    ? $"Exported {written.Destination}"
                    : $"Exported {written.FileCount} files to {written.Destination}";
            }
        }
        catch (Exception ex) when (ex is ImageFormatException or IOException)
        {
            await ShowErrorAsync("Could not export animation", ex.Message).ConfigureAwait(true);
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

    /// <summary>Brings a panel to the front, switching it back on if it was closed.</summary>
    /// <param name="panelId">The panel's identity, as given in <see cref="XpDockFactory"/>.</param>
    /// <remarks>
    /// This is what makes closing a panel safe. Without it, clicking the × on the layers panel
    /// would remove it with no way back short of resetting the whole layout.
    /// </remarks>
    [RelayCommand]
    private void ShowPanel(string panelId)
    {
        if (!_factory.ShowPanel(panelId))
        {
            // The layout has lost every dock that could hold the panel, which only happens
            // once the artist has closed all of them. Rebuilding the default arrangement is
            // the honest repair; inventing a place to put one panel back is not.
            ResetLayout();
            return;
        }

        RefreshPanelVisibility();
    }

    /// <summary>
    /// Discards the saved arrangement and rebuilds the default one, keeping every open
    /// document.
    /// </summary>
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

        StatusMessage = "Layout reset to default";
    }

    /// <summary>
    /// Picks the selected pixels up so they can be dragged, or puts them down if they are
    /// already up.
    /// </summary>
    /// <remarks>
    /// One key for both halves, the way Photoshop's free transform works: Ctrl+T lifts the
    /// selection, and Ctrl+T again commits it. Enter also commits and Escape abandons, both
    /// handled by the canvas, which is where the artist's attention is.
    /// </remarks>
    [RelayCommand]
    private void FreeTransform()
    {
        if (ActiveDocument?.Workspace is not { } workspace)
        {
            return;
        }

        if (workspace.ActiveTransform is not null)
        {
            workspace.CommitTransform();
            StatusMessage = "Selection moved";
            return;
        }

        StatusMessage = workspace.BeginTransform()
            ? "Drag the selected pixels, or nudge them with the arrow keys. Enter to place, Escape to cancel"
            : "Select some pixels with the marquee tool first";
    }

    /// <summary>Shows the folder the autosaved backups are kept in.</summary>
    /// <remarks>
    /// The backups are ordinary project files in an ordinary folder, so this is the whole of
    /// the recovery story: open the folder, and open the file. Anything more elaborate would be
    /// something else for an artist to learn at the worst possible moment.
    /// </remarks>
    [RelayCommand]
    private async Task OpenBackupFolderAsync()
    {
        if (_autosave.Location is not { } location)
        {
            StatusMessage = "This version of XP-xel does not keep backups";
            return;
        }

        StatusMessage = await _fileDialog.RevealFolderAsync(location).ConfigureAwait(true)
            ? $"Backups are kept in {location}"
            : $"Could not open {location}";
    }

    /// <summary>Reports what the last round of autosaving managed.</summary>
    /// <remarks>
    /// The status bar and nothing louder. A backup is a safety net, and a safety net that
    /// interrupts the work it is protecting has stopped being one.
    /// </remarks>
    private void OnAutosaveCompleted(object? sender, AutosaveResult e)
    {
        if (e.Failed > 0)
        {
            StatusMessage = "Autosave failed — check that the backup folder can be written to";
            return;
        }

        string plural = e.Written == 1 ? string.Empty : "s";
        StatusMessage = $"Autosaved {e.Written} document{plural} at {DateTime.Now:HH:mm}";
    }

    /// <summary>The workspaces behind every open tab, for the autosave service.</summary>
    private IReadOnlyList<DocumentWorkspace> CollectWorkspaces() =>
        Layout is { } layout
            ? [.. XpDockFactory.Enumerate(layout).OfType<EditorDocument>().Select(d => d.Workspace)]
            : [];

    /// <summary>Selects a tool by its single-key shortcut.</summary>
    /// <param name="shortcut">The key that was pressed, as a one-character string.</param>
    /// <remarks>
    /// Called from the shell's key handling rather than bound as a <c>KeyBinding</c>, because
    /// a bare letter must not become a shortcut while the artist is typing — into a layer's
    /// name, or into a text layer on the canvas. The shell checks that first.
    /// </remarks>
    public bool TrySelectToolByShortcut(string shortcut) => Toolbox.TrySelectByShortcut(shortcut);

    /// <summary>Selects the palette colour a number key names.</summary>
    /// <param name="digit">The digit that was pressed, 0 to 9.</param>
    /// <returns><see langword="true"/> if a colour claimed the key.</returns>
    /// <remarks>
    /// Gated on the same "is anything being typed into" check as the tool letters, for the
    /// same reason: a palette that jumped to slot 3 while the artist typed "Frame 3" into an
    /// animation's name would be maddening.
    /// </remarks>
    public bool TrySelectColorByHotkey(int digit) => Palette.TrySelectByHotkey(digit);

    // ---------------------------------------------------------------------------------
    // Image commands
    // ---------------------------------------------------------------------------------

    /// <summary>Crops or enlarges the canvas, after asking for the new size.</summary>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task ChangeCanvasSizeAsync()
    {
        if (Workspace is not { } workspace)
        {
            return;
        }

        ImageSize current = workspace.Document.Size;

        if (await Dialogs.ShowAsync(new CanvasSizeDialogViewModel(current)).ConfigureAwait(true)
            is not CanvasResizeRequest request)
        {
            return;
        }

        if (request.Size == current)
        {
            return;
        }

        // The anchor is turned into an offset here rather than inside the command, so that
        // the command records exactly where the artwork went and undo needs no arithmetic.
        ImagePoint offset = PixelDocument.GetAnchorOffset(current, request.Size, request.Anchor);

        ApplyCanvasResize(
            workspace,
            request.Size,
            offset,
            $"Canvas resized to {request.Size.Width} × {request.Size.Height}");
    }

    /// <summary>Crops the canvas down to the bounding box of the current selection.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CropToSelection()
    {
        if (Workspace is not { Selection: { IsEmpty: false } selection } workspace)
        {
            return;
        }

        // The bounding box, not the mask: a canvas is a rectangle, so a lasso-shaped
        // selection can only ever say which rectangle to keep.
        ImageRect bounds = selection.Bounds;

        ApplyCanvasResize(
            workspace,
            bounds.Size,
            new ImagePoint(-bounds.X, -bounds.Y),
            $"Cropped to {bounds.Width} × {bounds.Height}");
    }

    /// <summary>Applies a canvas resize and tidies up everything that was sized to the old canvas.</summary>
    private void ApplyCanvasResize(DocumentWorkspace workspace, ImageSize size, ImagePoint offset, string message)
    {
        workspace.History.Execute(new ResizeCanvasCommand("Canvas Size", workspace.Document, size, offset));

        // A selection mask is allocated for the canvas it was made on, so it cannot survive a
        // resize; keeping it would leave every tool testing coordinates against a mask of the
        // wrong shape. Clearing is the honest outcome, and it is what crop-to-selection wants
        // anyway — the selection has just become the whole canvas.
        workspace.Selection = null;

        StatusMessage = message;
    }

    // ---------------------------------------------------------------------------------
    // Selection commands
    // ---------------------------------------------------------------------------------

    /// <summary>Selects the whole canvas.</summary>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void SelectAll()
    {
        if (Workspace is not { } workspace)
        {
            return;
        }

        workspace.Selection = SelectionMask.FromRect(workspace.Document.Size, workspace.Document.Bounds);
        StatusMessage = "Selected all";
    }

    /// <summary>Clears the selection, so the whole canvas is editable again.</summary>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void Deselect()
    {
        if (Workspace is { } workspace)
        {
            workspace.Selection = null;
            StatusMessage = "Deselected";
        }
    }

    /// <summary>Swaps what is selected for what is not.</summary>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void InvertSelection()
    {
        if (Workspace is not { } workspace)
        {
            return;
        }

        ImageSize size = workspace.Document.Size;

        // Inverting nothing selects everything, which is what an artist means by inverting an
        // empty selection — the alternative reading, selecting nothing, is never useful.
        if (workspace.Selection is not { } current)
        {
            workspace.Selection = SelectionMask.FromRect(size, workspace.Document.Bounds);
            StatusMessage = "Selected all";
            return;
        }

        SelectionMask inverted = new(size);

        for (int y = 0; y < size.Height; y++)
        {
            for (int x = 0; x < size.Width; x++)
            {
                inverted.SetCoverage(x, y, (byte)(byte.MaxValue - current.GetCoverage(x, y)));
            }
        }

        workspace.Selection = inverted.IsEmpty ? null : inverted;
        StatusMessage = "Inverted selection";
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
            _factory.RestorePanelContent(restored);
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
        RefreshPanelVisibility();
    }

    // ---------------------------------------------------------------------------------
    // Panels
    // ---------------------------------------------------------------------------------

    /// <summary>Acts on the artist ticking or unticking a panel in the View menu.</summary>
    private void OnPanelToggled(PanelToggleViewModel panel, bool isVisible)
    {
        if (isVisible)
        {
            ShowPanel(panel.Id);
            StatusMessage = $"Showing {panel.Title}";
            return;
        }

        _factory.HidePanel(panel.Id);
        StatusMessage = $"Hid {panel.Title}";
        RefreshPanelVisibility();
    }

    /// <summary>
    /// Brings every checkmark in the View menu back into agreement with the layout.
    /// </summary>
    /// <remarks>
    /// Called whenever a dockable is added, closed or removed, because the artist has as many
    /// ways to close a panel as to open one — the × on its tab, dragging it out and shutting
    /// the window it became — and a checkmark that only tracked the menu would be wrong within
    /// a minute of use.
    /// </remarks>
    private void RefreshPanelVisibility()
    {
        foreach (PanelToggleViewModel panel in Panels)
        {
            panel.SetVisibleFromLayout(_factory.IsPanelVisible(panel.Id));
        }
    }

    private void OnDockableCountChanged(object? sender, EventArgs e) => RefreshPanelVisibility();

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

    private bool HasSelection() => Workspace?.Selection is { IsEmpty: false };

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
                OnPropertyChanged(nameof(HoveredXText));
                OnPropertyChanged(nameof(HoveredYText));
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

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentWorkspace.ActiveLayer) or nameof(DocumentWorkspace.CanEditActiveLayer))
        {
            NotifyEditabilityChanged();
        }

        // Cropping to a selection, and offering to move it, are only worth showing while
        // there is one.
        if (e.PropertyName is nameof(DocumentWorkspace.Selection) or nameof(DocumentWorkspace.ActiveTransform))
        {
            CropToSelectionCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Watches the document for layer property changes, which is how locking or hiding the
    /// active layer reaches the status bar — the workspace does not raise those itself.
    /// </summary>
    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (e.Kind is DocumentChangeKind.Properties or DocumentChangeKind.Structure)
        {
            NotifyEditabilityChanged();
        }

        // Handled here rather than at the call site so that undoing and redoing a resize
        // updates the status bar too, without either command knowing about it.
        if (e.Kind == DocumentChangeKind.Size)
        {
            OnPropertyChanged(nameof(DocumentSizeText));
        }
    }

    private void NotifyEditabilityChanged()
    {
        OnPropertyChanged(nameof(EditabilityWarning));
        OnPropertyChanged(nameof(HasEditabilityWarning));
    }

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
        ExportAnimationCommand.NotifyCanExecuteChanged();
        CloseDocumentCommand.NotifyCanExecuteChanged();
        SelectAllCommand.NotifyCanExecuteChanged();
        DeselectCommand.NotifyCanExecuteChanged();
        InvertSelectionCommand.NotifyCanExecuteChanged();
        ChangeCanvasSizeCommand.NotifyCanExecuteChanged();
        CropToSelectionCommand.NotifyCanExecuteChanged();
    }

    private void OnToolboxPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ToolboxViewModel.ActiveTool))
        {
            OnPropertyChanged(nameof(ActiveTool));
        }
    }
}
