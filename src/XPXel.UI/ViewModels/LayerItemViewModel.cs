using System.ComponentModel;
using XPXel.Core.Documents;
using XPXel.Core.History;
using XPXel.Core.Imaging;

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
public sealed class LayerItemViewModel : ViewModelBase, IDisposable
{
    private readonly DocumentHistory _history;

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
        set => Layer.IsVisible = value;
    }

    /// <summary>Whether the layer rejects edits.</summary>
    public bool IsLocked
    {
        get => Layer.IsLocked;
        set => Layer.IsLocked = value;
    }

    /// <summary>The layer's opacity as a percentage, which is how artists think about it.</summary>
    public double OpacityPercent
    {
        get => Layer.Opacity * 100.0;
        set => Layer.Opacity = value / 100.0;
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

            _history.Execute(new ChangeLayerPropertyCommand<BlendMode>(
                "Change Blend Mode", Layer, mode => Layer.BlendMode = mode, Layer.BlendMode, value));
        }
    }

    /// <inheritdoc />
    public void Dispose() => Layer.PropertyChanged -= OnLayerPropertyChanged;

    private void OnLayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The model's property names line up with this view model's except for opacity,
        // which is presented as a percentage.
        OnPropertyChanged(e.PropertyName switch
        {
            nameof(LayerNode.Opacity) => nameof(OpacityPercent),
            _ => e.PropertyName,
        });

        // The canvas is not told to redraw here: PixelDocument already forwards every
        // layer's property changes as a document-level Properties change, and raising it a
        // second time would recomposite the canvas twice for one toggle.
    }
}
