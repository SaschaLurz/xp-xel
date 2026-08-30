using XPXel.Core.Documents;

namespace XPXel.Core.History;

/// <summary>
/// Changes one presentation property of a layer — name, visibility, opacity, blend mode —
/// in an undoable way.
/// </summary>
/// <typeparam name="TValue">The property's type.</typeparam>
/// <remarks>
/// <para>
/// A single generic command covers every simple property rather than there being one class
/// per property. The caller supplies a setter, so adding a new layer property needs no new
/// command type:
/// </para>
/// <code>
/// history.Execute(new ChangeLayerPropertyCommand&lt;double&gt;(
///     "Change Opacity", layer, value =&gt; layer.Opacity = value, layer.Opacity, 0.5));
/// </code>
/// <para>
/// <b>Continuous controls.</b> A slider drag should produce one history entry, not one per
/// pixel of travel. Create the command when the drag <em>ends</em>, passing the value the
/// property had when the drag began as <c>oldValue</c>, and let the control write directly
/// to the layer while the drag is in progress.
/// </para>
/// </remarks>
public sealed class ChangeLayerPropertyCommand<TValue> : IUndoableCommand
{
    private readonly Action<TValue> _setter;
    private readonly TValue _oldValue;
    private readonly TValue _newValue;

    /// <summary>Initialises the command.</summary>
    /// <param name="name">The action name shown in the Edit menu.</param>
    /// <param name="layer">The layer being changed. Retained so callers can identify the target.</param>
    /// <param name="setter">Writes a value to the property.</param>
    /// <param name="oldValue">The value to restore on undo.</param>
    /// <param name="newValue">The value to apply.</param>
    public ChangeLayerPropertyCommand(
        string name,
        LayerNode layer,
        Action<TValue> setter,
        TValue oldValue,
        TValue newValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(setter);

        Name = name;
        Layer = layer;
        _setter = setter;
        _oldValue = oldValue;
        _newValue = newValue;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>The layer whose property this command changes.</summary>
    public LayerNode Layer { get; }

    /// <inheritdoc />
    public void Redo() => _setter(_newValue);

    /// <inheritdoc />
    public void Undo() => _setter(_oldValue);
}
