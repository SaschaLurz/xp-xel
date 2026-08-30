using XPXel.Core.ComponentModel;
using XPXel.Core.Documents;
using XPXel.Core.History;
using XPXel.Core.Selection;

namespace XPXel.Core.Editing;

/// <summary>
/// One open document together with everything the editor tracks about it: its undo history,
/// which layer and frame are active, the current selection, and where it lives on disk.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PixelDocument"/> deliberately knows nothing about "the layer the artist is
/// working on" — that is a property of the editing session, not of the artwork. Separating
/// them means a single document could be open in two views, or processed head-lessly by an
/// exporter, without carrying UI state around.
/// </para>
/// <para>
/// <b>Growth path.</b> One workspace equals one document tab. Multi-document support is
/// therefore a collection of these plus a notion of which is current; nothing about this
/// type assumes it is the only one.
/// </para>
/// </remarks>
public sealed class DocumentWorkspace : ObservableModel
{
    private LayerNode? _activeLayer;
    private Frame _activeFrame;
    private SelectionMask? _selection;
    private string? _filePath;

    /// <summary>Opens a workspace over a document.</summary>
    /// <param name="document">The document being edited.</param>
    /// <param name="filePath">Where the document was loaded from, if anywhere.</param>
    public DocumentWorkspace(PixelDocument document, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        Document = document;
        History = new DocumentHistory();
        _filePath = filePath;
        _activeFrame = document.Frames[0];

        // Select the top-most layer, which is what an artist expects to be drawing on when
        // a document opens.
        _activeLayer = document.Root.Children.Count > 0
            ? document.Root.Children[^1]
            : null;

        History.Changed += OnHistoryChanged;
    }

    /// <summary>The document being edited.</summary>
    public PixelDocument Document { get; }

    /// <summary>The undo history for this document.</summary>
    public DocumentHistory History { get; }

    /// <summary>The layer that drawing tools operate on, or <see langword="null"/> if none.</summary>
    public LayerNode? ActiveLayer
    {
        get => _activeLayer;
        set
        {
            if (SetProperty(ref _activeLayer, value))
            {
                OnPropertyChanged(nameof(ActiveRasterLayer));
                OnPropertyChanged(nameof(CanEditActiveLayer));
            }
        }
    }

    /// <summary>
    /// The active layer if it is a raster layer that can hold pixels, otherwise
    /// <see langword="null"/>. Drawing tools use this rather than <see cref="ActiveLayer"/>.
    /// </summary>
    public RasterLayer? ActiveRasterLayer => _activeLayer as RasterLayer;

    /// <summary>
    /// Gets a value indicating whether drawing is currently possible: there is an active
    /// raster layer and it is neither locked nor hidden.
    /// </summary>
    /// <remarks>
    /// Hidden layers are excluded because drawing on something invisible looks to the artist
    /// like the tool is broken.
    /// </remarks>
    public bool CanEditActiveLayer =>
        ActiveRasterLayer is { IsLocked: false } layer && layer.IsEffectivelyVisible;

    /// <summary>The frame being edited. Always exactly one frame in v0.1.</summary>
    public Frame ActiveFrame
    {
        get => _activeFrame;
        set => SetProperty(ref _activeFrame, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>
    /// The active selection, or <see langword="null"/> when everything is editable. No tool
    /// in v0.1 creates one; see <see cref="SelectionMask"/> for why it is plumbed through
    /// anyway.
    /// </summary>
    public SelectionMask? Selection
    {
        get => _selection;
        set => SetProperty(ref _selection, value);
    }

    /// <summary>
    /// The full path the document was last loaded from or saved to, or
    /// <see langword="null"/> for a document that has never been saved.
    /// </summary>
    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (SetProperty(ref _filePath, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    /// <summary>The document's name for the window title and tab, without any path or extension.</summary>
    public string DisplayName => string.IsNullOrEmpty(_filePath)
        ? "Untitled"
        : Path.GetFileNameWithoutExtension(_filePath);

    /// <summary>
    /// Gets a value indicating whether the document differs from what was last saved.
    /// </summary>
    public bool HasUnsavedChanges => !History.IsAtSavedState;

    /// <summary>
    /// Returns the cel the active layer occupies on the active frame, creating it on first
    /// use, or <see langword="null"/> if the active layer cannot be drawn on.
    /// </summary>
    /// <returns>The cel to draw into, or <see langword="null"/>.</returns>
    public Cel? GetOrCreateActiveCel()
    {
        if (!CanEditActiveLayer || ActiveRasterLayer is not RasterLayer layer)
        {
            return null;
        }

        return Document.GetOrCreateCel(layer, ActiveFrame);
    }

    /// <summary>Records that the document has just been saved to <paramref name="filePath"/>.</summary>
    /// <param name="filePath">Where it was saved.</param>
    public void MarkSaved(string filePath)
    {
        FilePath = filePath;
        History.MarkSaved();
    }

    private void OnHistoryChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(HasUnsavedChanges));
}
