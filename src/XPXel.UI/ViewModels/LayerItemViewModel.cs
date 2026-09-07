using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media.Imaging;
using XPXel.Core.Documents;
using XPXel.Core.History;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.UI.Rendering;

namespace XPXel.UI.ViewModels;

/// <summary>
/// One row of the layers panel, wrapping a <see cref="LayerNode"/>.
/// </summary>
/// <remarks>
/// <para>
/// The view model forwards the layer's own change notifications rather than caching its
/// values, so a change made anywhere — an undo, a script, a future timeline — is reflected
/// in the panel without anything having to remember to refresh it.
/// </para>
/// <para>
/// <b>What is and is not undoable, and why.</b> Renaming and changing the blend mode are
/// discrete decisions and go through the undo history. Visibility does not: it is a viewing
/// aid rather than a change to the artwork, and Ctrl+Z restoring a layer you had just hidden
/// is more annoying than helpful — this matches Aseprite and Photoshop. Opacity is applied
/// directly for now because a slider drag would otherwise push one history entry per pixel
/// of travel; making it undoable needs a "commit when the drag ends" hook, which is noted
/// here as the next step rather than faked.
/// </para>
/// </remarks>
public sealed partial class LayerItemViewModel : ViewModelBase, IDisposable
{
    private readonly DocumentHistory _history;
    private WriteableBitmap? _thumbnail;
    private bool _isEditingName;
    private bool _isMarked;
    private LayerDropPosition _dropPosition;

    /// <summary>Initialises the row.</summary>
    /// <param name="layer">The layer to present.</param>
    /// <param name="history">The undo history to record discrete changes in.</param>
    /// <param name="depth">How deeply the layer is nested, used to indent the row.</param>
    public LayerItemViewModel(LayerNode layer, DocumentHistory history, int depth)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(history);

        Layer = layer;
        _history = history;
        Depth = depth;

        Layer.PropertyChanged += OnLayerPropertyChanged;
    }

    /// <summary>The layer this row presents.</summary>
    public LayerNode Layer { get; }

    /// <summary>
    /// The other layers a change to this row should also reach, or <see langword="null"/> when
    /// it should reach only this one.
    /// </summary>
    /// <remarks>
    /// Supplied by <see cref="LayersViewModel"/>, which is the only thing that knows what is
    /// marked. It answers with the whole marked set when this row is part of it, and with
    /// nothing when it is not - so ticking the visibility box on an unmarked row is still an
    /// ordinary single-layer change, however many rows happen to be highlighted elsewhere.
    /// </remarks>
    internal Func<LayerItemViewModel, IReadOnlyList<LayerNode>>? Peers { get; set; }

    /// <summary>
    /// Whether this row is part of the marked set, which is what the panel highlights.
    /// </summary>
    public bool IsMarked
    {
        get => _isMarked;
        internal set => SetProperty(ref _isMarked, value);
    }

    /// <summary>How deeply the layer is nested; the row indents by this much.</summary>
    public int Depth { get; }

    /// <summary>The indentation in device-independent pixels, for binding to a margin.</summary>
    public double Indent => Depth * 14.0;

    /// <summary>Gets a value indicating whether this row represents a folder.</summary>
    public bool IsGroup => Layer is GroupLayer;

    /// <summary>The layer's display name. Editing it is recorded in the undo history.</summary>
    public string Name
    {
        get => Layer.Name;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value == Layer.Name)
            {
                return;
            }

            _history.Execute(new ChangeLayerPropertyCommand<string>(
                "Rename Layer", Layer, name => Layer.Name = name, Layer.Name, value));
        }
    }

    /// <summary>Whether the layer is drawn. Not recorded in the undo history; see the type remarks.</summary>
    public bool IsVisible
    {
        get => Layer.IsVisible;
        set => ApplyToPeers(layer => layer.IsVisible = value);
    }

    /// <summary>Whether the layer rejects edits.</summary>
    public bool IsLocked
    {
        get => Layer.IsLocked;
        set => ApplyToPeers(layer => layer.IsLocked = value);
    }

    /// <summary>The layer's opacity as a percentage, which is how artists think about it.</summary>
    public double OpacityPercent
    {
        get => Layer.Opacity * 100.0;
        set => ApplyToPeers(layer => layer.Opacity = value / 100.0);
    }

    /// <summary>The layer's blend mode. Changing it is recorded in the undo history.</summary>
    public BlendMode BlendMode
    {
        get => Layer.BlendMode;
        set
        {
            if (value == Layer.BlendMode)
            {
                return;
            }

            List<IUndoableCommand> changes = [];

            foreach (LayerNode layer in Subjects())
            {
                LayerNode target = layer;

                if (target.BlendMode != value)
                {
                    changes.Add(new ChangeLayerPropertyCommand<BlendMode>(
                        "Change Blend Mode", target, mode => target.BlendMode = mode, target.BlendMode, value));
                }
            }

            if (changes.Count == 0)
            {
                return;
            }

            _history.Execute(changes.Count == 1
                ? changes[0]
                : new CompositeCommand("Change Blend Mode", changes));
        }
    }

    /// <summary>
    /// The layer's organisational label colour, or <see langword="null"/> for none.
    /// Changing it is recorded in the undo history, as renaming is.
    /// </summary>
    public PixelColor? TagColor
    {
        get => Layer.TagColor;
        set
        {
            if (value == Layer.TagColor)
            {
                return;
            }

            _history.Execute(new ChangeLayerPropertyCommand<PixelColor?>(
                "Change Layer Colour", Layer, colour => Layer.TagColor = colour, Layer.TagColor, value));
        }
    }

    /// <summary>The label colour as a brush, or a transparent brush when the layer has none.</summary>
    public IBrush TagBrush => Layer.TagColor is { } colour
        ? new SolidColorBrush(Color.FromArgb(colour.A, colour.R, colour.G, colour.B))
        : Brushes.Transparent;

    /// <summary>Gets a value indicating whether the row should show its colour strip.</summary>
    public bool HasTagColor => Layer.TagColor is not null;

    /// <summary>
    /// Whether the row is currently showing an editable text box for its name.
    /// </summary>
    /// <remarks>
    /// Renaming is modal at the row level rather than always-editable, because a permanently
    /// live text box swallows the single-click that selects the layer and makes the list
    /// awkward to navigate. The view switches between a label and a text box on this flag,
    /// which is set by a double-click and cleared on Enter, Escape or loss of focus.
    /// </remarks>
    public bool IsEditingName
    {
        get => _isEditingName;
        set => SetProperty(ref _isEditingName, value);
    }

    /// <summary>
    /// A small preview of what this layer contains, or <see langword="null"/> before the
    /// first render.
    /// </summary>
    public WriteableBitmap? Thumbnail
    {
        get => _thumbnail;
        private set => SetProperty(ref _thumbnail, value);
    }

    /// <summary>Re-renders <see cref="Thumbnail"/> from the document's current pixels.</summary>
    /// <param name="document">The document that owns this layer's cels.</param>
    /// <param name="frame">The frame to preview.</param>
    /// <remarks>
    /// Callers are expected to throttle: rendering costs time proportional to the canvas,
    /// so driving this straight from the document's change event would rebuild every preview
    /// on every pointer move during a stroke. <c>LayersViewModel</c> owns that throttle.
    /// </remarks>
    public void RefreshThumbnail(PixelDocument document, Frame frame)
    {
        WriteableBitmap? rendered = LayerThumbnailRenderer.Render(document, Layer, frame);

        // The old bitmap is not disposed: it may still be attached to a visual that has not
        // yet re-bound, and disposing an in-use WriteableBitmap crashes the renderer. These
        // are at most a few kilobytes each and are collected normally.
        Thumbnail = rendered;
    }

    /// <summary>
    /// Where a layer being dragged would land relative to this row, used to draw the
    /// insertion line. <see cref="LayerDropPosition.None"/> while nothing is being dragged
    /// over it.
    /// </summary>
    public LayerDropPosition DropPosition
    {
        get => _dropPosition;
        set
        {
            if (SetProperty(ref _dropPosition, value))
            {
                OnPropertyChanged(nameof(IsDropTargetAbove));
                OnPropertyChanged(nameof(IsDropTargetBelow));
                OnPropertyChanged(nameof(IsDropTargetInside));
            }
        }
    }

    /// <summary>Gets a value indicating whether the insertion line sits above this row.</summary>
    public bool IsDropTargetAbove => DropPosition == LayerDropPosition.Above;

    /// <summary>Gets a value indicating whether the insertion line sits below this row.</summary>
    public bool IsDropTargetBelow => DropPosition == LayerDropPosition.Below;

    /// <summary>
    /// Gets a value indicating whether dropping now would move the dragged layer inside this
    /// folder. The row draws an outline rather than an insertion line, because "into this
    /// box" and "between these two rows" have to look different to be told apart mid-drag.
    /// </summary>
    public bool IsDropTargetInside => DropPosition == LayerDropPosition.Inside;

    /// <summary>
    /// Applies a label colour given as hexadecimal, or clears it when the text is empty.
    /// </summary>
    /// <param name="hex">A colour such as <c>#E4572E</c>, or <see langword="null"/>/empty.</param>
    /// <remarks>
    /// The parameter is text rather than a colour so the swatches can be declared directly
    /// in XAML without a converter or a per-swatch view model.
    /// </remarks>
    [RelayCommand]
    private void SetTagColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            TagColor = null;
            return;
        }

        if (PixelColor.TryParseHex(hex, out PixelColor colour))
        {
            TagColor = colour;
        }
    }

    /// <summary>Puts the row into rename mode.</summary>
    [RelayCommand]
    private void BeginRename() => IsEditingName = true;

    /// <inheritdoc />
    public void Dispose() => Layer.PropertyChanged -= OnLayerPropertyChanged;

    /// <summary>Every layer a change made on this row should reach.</summary>
    private IReadOnlyList<LayerNode> Subjects()
    {
        IReadOnlyList<LayerNode>? peers = Peers?.Invoke(this);
        return peers is { Count: > 0 } ? peers : [Layer];
    }

    /// <summary>
    /// Applies a change to this layer and to every other marked one.
    /// </summary>
    /// <remarks>
    /// This is what makes hiding six marked layers a single tick rather than six. It is written
    /// as a loop over the layers rather than as a loop over the rows because a marked folder
    /// has no row of its own for each child, and the change is meant for the folder itself.
    /// </remarks>
    private void ApplyToPeers(Action<LayerNode> apply)
    {
        foreach (LayerNode layer in Subjects())
        {
            apply(layer);
        }
    }

    private void OnLayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The model's property names line up with this view model's except for opacity,
        // which is presented as a percentage.
        OnPropertyChanged(e.PropertyName switch
        {
            nameof(LayerNode.Opacity) => nameof(OpacityPercent),
            _ => e.PropertyName,
        });

        if (e.PropertyName == nameof(LayerNode.TagColor))
        {
            OnPropertyChanged(nameof(TagBrush));
            OnPropertyChanged(nameof(HasTagColor));
        }

        // The canvas is not told to redraw here: PixelDocument already forwards every
        // layer's property changes as a document-level Properties change, and raising it a
        // second time would recomposite the canvas twice for one toggle.
    }
}
