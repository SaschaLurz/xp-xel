using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Windows.Input;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using XPXel.Core.Editing;
using XPXel.Core.Primitives;
using XPXel.Core.Tools;
using XPXel.UI.ViewModels;

namespace XPXel.UI.Docking;

/// <summary>
/// One open image, as a document tab.
/// </summary>
/// <remarks>
/// <para>
/// <b>One of these is one <see cref="DocumentWorkspace"/>.</b> That was the plan from the
/// first version: the workspace already bundled everything that is per-document rather than
/// per-application — the document, its undo history, the active layer and frame, the
/// selection and the file path — so opening several images at once became a collection of
/// workspaces rather than a restructuring of the editor.
/// </para>
/// <para>
/// <b>What lives here and what does not.</b> View state that belongs to <i>this image</i>
/// lives here: the zoom, the scroll position implied by it, the pixel under the pointer.
/// Anything shared across every open image — the current tool, the brush size, the two
/// colours, whether the pixel grid is on — deliberately does not, because an artist expects
/// to pick a brush once and keep it while switching tabs.
/// </para>
/// <para>
/// <b>Nothing here is serialised into a saved layout.</b> Every property below is a live
/// object — a document, a tool context, a file handle — and none of them mean anything
/// written to a settings file. Layouts store where panels sit, not what was open; documents
/// found in a restored layout are discarded on load. Without the attributes the serialiser
/// walks into the layer tree, hits its parent references and throws.
/// </para>
/// <para>
/// <b>Closing.</b> The docking system asks a document whether it may close through
/// <see cref="OnClose"/>, which is synchronous, while asking the artist to save is not. The
/// first attempt therefore refuses and starts the prompt; if the artist saves or discards,
/// <see cref="CloseConfirmed"/> is set and the close is retried, which then succeeds.
/// </para>
/// </remarks>
public sealed class EditorDocument : Document
{
    private double _zoom = 1.0;
    private ImagePoint? _hoveredPixel;

    /// <summary>Initialises the tab.</summary>
    /// <param name="workspace">The document and its editing state.</param>
    /// <param name="toolContext">The context tools receive when drawing on this document.</param>
    /// <param name="canvasSettings">View options shared by every open document.</param>
    /// <param name="toolbox">The shared toolbox, which decides what drawing does.</param>
    public EditorDocument(
        DocumentWorkspace workspace,
        ToolContext toolContext,
        CanvasSettings canvasSettings,
        ToolboxViewModel toolbox)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(toolContext);
        ArgumentNullException.ThrowIfNull(canvasSettings);
        ArgumentNullException.ThrowIfNull(toolbox);

        Workspace = workspace;
        ToolContext = toolContext;
        CanvasSettings = canvasSettings;
        Toolbox = toolbox;

        // A fresh identity per tab. Dock uses Id to recycle controls and to match a saved
        // layout back to its documents; two tabs sharing one would swap their canvases.
        Id = Guid.NewGuid().ToString("N");
        CanFloat = true;
        CanPin = false;

        workspace.PropertyChanged += OnWorkspacePropertyChanged;

        // On the document rather than on the shell, because the canvas menu binds to the
        // document — a tab torn off into a floating window has no shell above it to reach.
        MoveSelectionCommand = new RelayCommand(ToggleSelectionMove);

        UpdateTitle();
    }

    /// <summary>Raised when the tab wants the canvas to change zoom.</summary>
    /// <remarks>
    /// The zoom operations live on the canvas control, because only it knows the viewport
    /// size that "fit to window" depends on. This event is how a menu command reaches the
    /// one canvas that belongs to this tab, without the shell having to find controls by
    /// name — which stopped working the moment there could be more than one canvas.
    /// </remarks>
    public event EventHandler<CanvasZoomRequest>? ZoomRequested;

    /// <summary>The document being edited, together with its undo history and selection.</summary>
    [JsonIgnore]
    public DocumentWorkspace Workspace { get; }

    /// <summary>
    /// Picks the marked pixels up so they can be dragged, or puts them down again.
    /// </summary>
    /// <remarks>
    /// The same action as Ctrl+T, offered on the canvas menu because right-clicking what you
    /// have just marked is the other obvious way to ask for it.
    /// </remarks>
    [JsonIgnore]
    public ICommand MoveSelectionCommand { get; }

    /// <summary>Gets a value indicating whether anything is currently marked.</summary>
    [JsonIgnore]
    public bool IsAnythingSelected => Workspace.Selection is { IsEmpty: false };

    /// <summary>What the canvas menu's move entry should say, given what is happening.</summary>
    [JsonIgnore]
    public string MoveSelectionLabel =>
        Workspace.ActiveTransform is not null ? "Place the pixels here" : "Move position";

    /// <summary>The context handed to tools drawing on this document.</summary>
    [JsonIgnore]
    public ToolContext ToolContext { get; }

    /// <summary>View options shared across every open document.</summary>
    [JsonIgnore]
    public CanvasSettings CanvasSettings { get; }

    /// <summary>
    /// The shared toolbox. The canvas needs it to know which tool should receive input.
    /// </summary>
    /// <remarks>
    /// The tool is application-wide while the context it draws into is per-document, which
    /// is why the two arrive from different places. Reached through the document rather than
    /// through the shell because a document can be torn off into a floating window, where
    /// there is no shell above it in the visual tree to bind to.
    /// </remarks>
    [JsonIgnore]
    public ToolboxViewModel Toolbox { get; }

    /// <summary>
    /// The handle this document was loaded from or last saved to.
    /// </summary>
    /// <remarks>
    /// Kept alongside the workspace's file path because the browser has no paths, only
    /// handles, and saving again needs the handle rather than a display string.
    /// </remarks>
    [JsonIgnore]
    public IStorageFile? File { get; set; }

    /// <summary>
    /// Set once the artist has agreed to lose unsaved changes, so the retried close goes
    /// through instead of prompting again.
    /// </summary>
    [JsonIgnore]
    public bool CloseConfirmed { get; set; }

    /// <summary>
    /// Asked whether this document may close while it has unsaved changes. Supplied by the
    /// shell, which owns the dialog host.
    /// </summary>
    [JsonIgnore]
    public Func<EditorDocument, Task>? ConfirmCloseAsync { get; set; }

    /// <summary>The current magnification, where 1.0 is one screen pixel per image pixel.</summary>
    [JsonIgnore]
    public double Zoom
    {
        get => _zoom;
        set => SetProperty(ref _zoom, value);
    }

    /// <summary>The image pixel under the pointer, or <see langword="null"/> when outside.</summary>
    [JsonIgnore]
    public ImagePoint? HoveredPixel
    {
        get => _hoveredPixel;
        set => SetProperty(ref _hoveredPixel, value);
    }

    /// <summary>Asks this tab's canvas to change its zoom.</summary>
    /// <param name="request">What the canvas should do.</param>
    public void RequestZoom(CanvasZoomRequest request) => ZoomRequested?.Invoke(this, request);

    /// <inheritdoc />
    public override bool OnClose()
    {
        if (CloseConfirmed || !Workspace.HasUnsavedChanges)
        {
            Workspace.PropertyChanged -= OnWorkspacePropertyChanged;
            return true;
        }

        // Refuse for now and ask. The prompt closes the tab itself if the artist agrees.
        if (ConfirmCloseAsync is { } confirm)
        {
            _ = confirm(this);
        }

        return false;
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentWorkspace.FilePath) or nameof(DocumentWorkspace.HasUnsavedChanges))
        {
            UpdateTitle();
        }

        if (e.PropertyName is nameof(DocumentWorkspace.Selection) or nameof(DocumentWorkspace.ActiveTransform))
        {
            OnPropertyChanged(nameof(IsAnythingSelected));
            OnPropertyChanged(nameof(MoveSelectionLabel));
        }
    }

    /// <summary>Lifts the marked pixels, or places them if they are already lifted.</summary>
    private void ToggleSelectionMove()
    {
        if (Workspace.ActiveTransform is not null)
        {
            Workspace.CommitTransform();
            return;
        }

        Workspace.BeginTransform();
    }

    private void UpdateTitle()
    {
        string name = string.IsNullOrEmpty(Workspace.FilePath) ? "Untitled" : Workspace.DisplayName;

        // IsModified is what draws the tab's unsaved marker, so the asterisk is not repeated
        // in the title itself — that would show the change twice on the same tab.
        Title = name;
        IsModified = Workspace.HasUnsavedChanges;
    }
}
