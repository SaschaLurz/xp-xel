using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.History;
using XPXel.Core.Imaging;

namespace XPXel.UI.ViewModels;

/// <summary>
/// The layers panel: the stack of layers, which one is selected, and the operations that
/// change the stack.
/// </summary>
/// <remarks>
/// <para>
/// <b>Display order is the reverse of storage order.</b> The document stores layers
/// bottom-most first so the compositor can walk them forwards; every layers panel ever built
/// shows the top-most layer at the top of the list. The flip happens here, in exactly one
/// place, and nothing else in the codebase needs to think about it.
/// </para>
/// <para>
/// The list is rebuilt whenever the document reports a structural change, rather than being
/// patched incrementally. A pixel art document has tens of layers, not thousands, so the
/// rebuild is imperceptible and it removes an entire category of "the panel and the model
/// disagree" bugs.
/// </para>
/// </remarks>
public sealed partial class LayersViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// How long the panel waits after the last pixel change before re-rendering previews.
    /// </summary>
    /// <remarks>
    /// Rendering a preview costs time proportional to the canvas, so doing it straight from
    /// the document's change event would rebuild every row on every pointer move of a
    /// stroke. A short delay coalesces a whole stroke into one rebuild while still feeling
    /// immediate — the preview updates before the artist can look away from the canvas.
    /// </remarks>
    private static readonly TimeSpan ThumbnailRefreshDelay = TimeSpan.FromMilliseconds(150);

    private readonly DispatcherTimer _thumbnailTimer;

    private DocumentWorkspace? _workspace;
    private PixelDocument? _observedDocument;
    private bool _isRebuilding;

    /// <summary>Initialises the panel.</summary>
    public LayersViewModel()
    {
        _thumbnailTimer = new DispatcherTimer { Interval = ThumbnailRefreshDelay };
        _thumbnailTimer.Tick += OnThumbnailTimerTick;
    }

    [ObservableProperty]
    private LayerItemViewModel? _selectedLayer;

    /// <summary>The layers, top-most first.</summary>
    public ObservableCollection<LayerItemViewModel> Layers { get; } = [];

    /// <summary>The blend modes offered in each row's dropdown.</summary>
    public static IReadOnlyList<BlendMode> BlendModes { get; } = Enum.GetValues<BlendMode>();

    /// <summary>Points the panel at a different document, or at none.</summary>
    /// <param name="workspace">The document to present.</param>
    public void Attach(DocumentWorkspace? workspace)
    {
        if (_observedDocument is not null)
        {
            _observedDocument.Changed -= OnDocumentChanged;
        }

        _workspace = workspace;
        _observedDocument = workspace?.Document;

        if (_observedDocument is not null)
        {
            _observedDocument.Changed += OnDocumentChanged;
        }

        Rebuild();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _thumbnailTimer.Stop();
        _thumbnailTimer.Tick -= OnThumbnailTimerTick;

        if (_observedDocument is not null)
        {
            _observedDocument.Changed -= OnDocumentChanged;
            _observedDocument = null;
        }

        ClearRows();
    }

    /// <summary>Adds an empty layer above the selected one and selects it.</summary>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void AddLayer()
    {
        if (_workspace is not { } workspace)
        {
            return;
        }

        PixelDocument document = workspace.Document;
        GroupLayer parent = SelectedLayer?.Layer.Parent ?? document.Root;
        int index = SelectedLayer is null ? parent.Children.Count : parent.IndexOf(SelectedLayer.Layer) + 1;

        RasterLayer layer = new(Guid.NewGuid(), NextLayerName(document));
        workspace.History.Execute(new AddLayerCommand("Add Layer", document, layer, parent, index));

        SelectLayer(layer);
    }

    /// <summary>Copies a layer, artwork and all, and selects the copy.</summary>
    /// <param name="row">
    /// The layer to copy, or <see langword="null"/> for the selected one. Dropping a row
    /// onto the "new layer" button passes it explicitly, because the dragged layer is not
    /// necessarily the selected one.
    /// </param>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DuplicateLayer(LayerItemViewModel? row)
    {
        if (_workspace is not { } workspace || (row ?? SelectedLayer) is not { } selected)
        {
            return;
        }

        PixelDocument document = workspace.Document;
        LayerNode source = selected.Layer;

        // DuplicateLayer both copies the cels and inserts the layer, so the copy is removed
        // again and handed to the command, which owns putting it back on redo.
        LayerNode copy = document.DuplicateLayer(source, $"{source.Name} copy");
        IReadOnlyList<Cel> cels = document.RemoveLayer(copy);

        workspace.History.Execute(new AddLayerCommand(
            "Duplicate Layer", document, copy, source.Parent ?? document.Root, source.Parent!.IndexOf(source) + 1, cels));

        SelectLayer(copy);
    }

    /// <summary>Deletes the selected layer. The last remaining layer cannot be deleted.</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteLayer))]
    private void DeleteLayer()
    {
        if (_workspace is not { } workspace || SelectedLayer is not { } selected)
        {
            return;
        }

        workspace.History.Execute(new RemoveLayerCommand("Delete Layer", workspace.Document, selected.Layer));
        SelectedLayer = Layers.FirstOrDefault();
    }

    /// <summary>Moves the selected layer one position up the stack.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveLayerUp() => MoveSelected(1);

    /// <summary>Moves the selected layer one position down the stack.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveLayerDown() => MoveSelected(-1);

    private bool HasDocument() => _workspace is not null;

    private bool HasSelection() => _workspace is not null && SelectedLayer is not null;

    /// <summary>
    /// A document must always keep at least one layer, otherwise there is nowhere to draw
    /// and the artist is left with a canvas that silently ignores every stroke.
    /// </summary>
    private bool CanDeleteLayer() => HasSelection() && _workspace!.Document.Root.Children.Count > 1;

    private void MoveSelected(int delta)
    {
        if (_workspace is not { } workspace || SelectedLayer?.Layer is not { Parent: { } parent } layer)
        {
            return;
        }

        int current = parent.IndexOf(layer);
        int target = current + delta;
        if (target < 0 || target >= parent.Children.Count)
        {
            return;
        }

        workspace.History.Execute(new MoveLayerCommand("Reorder Layer", workspace.Document, layer, parent, target));
        SelectLayer(layer);
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (e.Kind == DocumentChangeKind.Structure)
        {
            Rebuild();
            return;
        }

        if (e.Kind == DocumentChangeKind.Pixels)
        {
            ScheduleThumbnailRefresh();
        }
    }

    /// <summary>
    /// Requests a preview rebuild, restarting the delay so a burst of changes costs one
    /// rebuild rather than one per change.
    /// </summary>
    private void ScheduleThumbnailRefresh()
    {
        _thumbnailTimer.Stop();
        _thumbnailTimer.Start();
    }

    private void OnThumbnailTimerTick(object? sender, EventArgs e)
    {
        _thumbnailTimer.Stop();
        RefreshThumbnails();
    }

    /// <summary>
    /// Re-renders every row's preview.
    /// </summary>
    /// <remarks>
    /// Every row is refreshed rather than only the one being drawn on, because an undo, a
    /// redo or a future scripted edit can change any layer and the change event does not say
    /// which. A stale preview is a visible bug, so correctness wins here. If documents ever
    /// grow large enough for this to be felt, the fix is to carry the affected layer on
    /// <see cref="DocumentChangedEventArgs"/> rather than to guess here.
    /// </remarks>
    private void RefreshThumbnails()
    {
        if (_workspace is not { } workspace)
        {
            return;
        }

        foreach (LayerItemViewModel row in Layers)
        {
            row.RefreshThumbnail(workspace.Document, workspace.ActiveFrame);
        }
    }

    /// <summary>
    /// Moves a dragged layer next to another row, which is how the panel reorders the stack.
    /// </summary>
    /// <param name="source">The row being dragged.</param>
    /// <param name="target">The row it was dropped on.</param>
    /// <param name="insertAbove">
    /// <see langword="true"/> to place it above the target <i>as displayed</i>, which means
    /// a higher index in the document's bottom-first storage order.
    /// </param>
    public void ReorderLayer(LayerItemViewModel source, LayerItemViewModel target, bool insertAbove)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        if (_workspace is not { } workspace
            || ReferenceEquals(source, target)
            || target.Layer.Parent is not { } parent)
        {
            return;
        }

        // Dragging a group into its own descendant would detach that subtree from the
        // document. GroupLayer guards against this too, but failing silently here avoids
        // pushing a no-op onto the undo stack.
        if (source.Layer is GroupLayer group && IsDescendantOf(target.Layer, group))
        {
            return;
        }

        int targetIndex = parent.IndexOf(target.Layer);
        int destination = insertAbove ? targetIndex + 1 : targetIndex;

        // Within one group the move is remove-then-insert, so every position above the
        // layer's old slot shifts down by one and the destination must follow.
        int sourceIndex = parent.IndexOf(source.Layer);
        if (sourceIndex >= 0 && sourceIndex < destination)
        {
            destination--;
        }

        destination = Math.Clamp(destination, 0, Math.Max(0, parent.Children.Count - 1));
        if (sourceIndex == destination && ReferenceEquals(source.Layer.Parent, parent))
        {
            return;
        }

        workspace.History.Execute(new MoveLayerCommand(
            "Reorder Layer", workspace.Document, source.Layer, parent, destination));

        SelectLayer(source.Layer);
    }

    /// <summary>Walks up the parent chain to test containment.</summary>
    private static bool IsDescendantOf(LayerNode node, GroupLayer ancestor)
    {
        for (LayerNode? current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private void Rebuild()
    {
        Guid? previousSelection = SelectedLayer?.Layer.Id;

        _isRebuilding = true;
        try
        {
            ClearRows();

            if (_workspace is { } workspace)
            {
                AppendRows(workspace.Document.Root, workspace.History, depth: 0);
            }

            SelectedLayer = Layers.FirstOrDefault(row => row.Layer.Id == previousSelection)
                ?? Layers.FirstOrDefault(row => row.Layer == _workspace?.ActiveLayer)
                ?? Layers.FirstOrDefault();
        }
        finally
        {
            _isRebuilding = false;
        }

        // Selection was assigned while suppressed, so push it to the workspace now.
        ApplySelectionToWorkspace();
        NotifyCommandsCanExecuteChanged();

        // New rows start with no preview, so render immediately rather than waiting for the
        // throttle: an empty box beside a layer reads as "this layer is empty".
        RefreshThumbnails();
    }

    /// <summary>Walks the tree in reverse so the top-most layer ends up first in the list.</summary>
    private void AppendRows(GroupLayer group, DocumentHistory history, int depth)
    {
        for (int i = group.Children.Count - 1; i >= 0; i--)
        {
            LayerNode child = group.Children[i];
            Layers.Add(new LayerItemViewModel(child, history, depth));

            if (child is GroupLayer nested)
            {
                AppendRows(nested, history, depth + 1);
            }
        }
    }

    private void ClearRows()
    {
        foreach (LayerItemViewModel row in Layers)
        {
            row.Dispose();
        }

        Layers.Clear();
    }

    private void SelectLayer(LayerNode layer) =>
        SelectedLayer = Layers.FirstOrDefault(row => row.Layer == layer) ?? SelectedLayer;

    private void ApplySelectionToWorkspace()
    {
        if (_workspace is { } workspace)
        {
            workspace.ActiveLayer = SelectedLayer?.Layer;
        }
    }

    partial void OnSelectedLayerChanged(LayerItemViewModel? value)
    {
        if (_isRebuilding)
        {
            return;
        }

        ApplySelectionToWorkspace();
        NotifyCommandsCanExecuteChanged();
    }

    private void NotifyCommandsCanExecuteChanged()
    {
        AddLayerCommand.NotifyCanExecuteChanged();
        DuplicateLayerCommand.NotifyCanExecuteChanged();
        DeleteLayerCommand.NotifyCanExecuteChanged();
        MoveLayerUpCommand.NotifyCanExecuteChanged();
        MoveLayerDownCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Produces "Layer 3" style names that do not collide with existing ones.</summary>
    private static string NextLayerName(PixelDocument document)
    {
        int count = document.EnumerateLayers().Count();
        return $"Layer {count + 1}";
    }
}
