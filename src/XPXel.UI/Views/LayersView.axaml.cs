using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XPXel.UI.ViewModels;

namespace XPXel.UI.Views;

/// <summary>
/// Code-behind for the layers panel: drag-and-drop restacking, dropping a layer onto a
/// folder or onto one of the buttons below the list, and the double-click-to-rename
/// interaction.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is code-behind and not a view model.</b> All of it is about pointer
/// positions, hit testing and focus — view concerns that have no meaning without a rendered
/// tree. The moment a decision is reached ("this layer goes above that one", "duplicate
/// this layer") it is handed straight to <see cref="LayersViewModel"/>, which owns the
/// actual document change and the undo entry. Nothing here touches the document.
/// </para>
/// <para>
/// <b>The drag payload is the row view model itself</b>, carried by an in-process data
/// format. XP-xel does not accept layers dragged from other applications, so nothing has to
/// be serialised, and passing the object avoids a lookup by name or index that could go
/// stale mid-drag.
/// </para>
/// <para>
/// <b>Every drop acts on the dragged row, never on the selected one.</b> Picking a layer up
/// does not select it, so the two are routinely different; a drop that quietly operated on
/// the selection would duplicate or — far worse — delete the wrong layer. When the dragged row
/// <i>is</i> one of several marked ones, the whole marked set travels with it, which is the
/// rule every file manager follows; <see cref="LayersViewModel.GetTargets"/> owns that decision
/// so that the buttons and the drops agree about it.
/// </para>
/// <para>
/// <b>Collapsing a multiple selection waits for the release.</b> A list box narrows its
/// selection to the row under the pointer the moment that row is pressed, which would throw the
/// marked set away before a drag of it could begin. So a plain press on an already-marked row
/// is held back here and applied on release instead, if the press turned out to be a click
/// rather than a drag. This is what every file manager does, for exactly this reason.
/// </para>
/// </remarks>
public partial class LayersView : UserControl
{
    /// <summary>
    /// The format under which a dragged row travels. An in-process format carries the object
    /// itself rather than bytes, which is exactly right here: a layer only means anything
    /// inside the application that owns it. The identifier is namespaced so it cannot
    /// collide with a format from another application.
    /// </summary>
    private static readonly DataFormat<LayerItemViewModel> LayerRowFormat =
        DataFormat.CreateInProcessFormat<LayerItemViewModel>("xpxel/layer-row");

    /// <summary>
    /// How far the pointer must travel before a press becomes a drag, in device-independent
    /// pixels. Without a threshold, an ordinary click that wobbles by a pixel would start a
    /// drag and make the list feel unusable.
    /// </summary>
    private const double DragThreshold = 4.0;

    /// <summary>The name of the button that a drop duplicates onto.</summary>
    private const string NewLayerButtonName = "NewLayerButton";

    /// <summary>The name of the button that a drop wraps a layer in a new folder onto.</summary>
    private const string NewFolderButtonName = "NewFolderButton";

    /// <summary>The name of the button that a drop deletes onto.</summary>
    private const string DeleteButtonName = "DeleteLayerButton";

    private Point _pressOrigin;
    private LayerItemViewModel? _pressedRow;

    /// <summary>
    /// The row whose selection collapse was held back at press time, to be applied on release
    /// if no drag started. See the type remarks.
    /// </summary>
    private LayerItemViewModel? _deferredCollapse;

    /// <summary>
    /// The press that may become a drag. Avalonia 12 requires the originating
    /// <see cref="PointerPressedEventArgs"/> to start one, so it is held until the pointer
    /// has moved far enough to prove the gesture is a drag rather than a click.
    /// </summary>
    private PointerPressedEventArgs? _pressArgs;

    /// <summary>Initialises the view.</summary>
    public LayersView()
    {
        InitializeComponent();

        AddHandler(PointerPressedEvent, OnRowPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnRowPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnRowPointerReleased, RoutingStrategies.Tunnel);

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
    }

    private LayersViewModel? Model => DataContext as LayersViewModel;

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // ---------------------------------------------------------------------------------
    // Starting a drag
    // ---------------------------------------------------------------------------------

    private void OnRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Only a plain left press starts a drag. A right press opens the context menu, and
        // a press on the visibility checkbox must stay a click.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.Source is CheckBox)
        {
            return;
        }

        _pressedRow = FindRow(e.Source as Visual);
        _pressOrigin = e.GetPosition(this);
        _pressArgs = e;
        _deferredCollapse = null;

        // Ctrl-clicking a preview marks everything on that layer. It is on the preview rather
        // than the whole row because the preview is the picture of the layer's contents, which
        // is exactly what the gesture selects — and because a modified click anywhere on a row
        // of a list already means "extend the selection" to every hand that uses a computer.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && _pressedRow is { } row
            && Model is { } model
            && IsInsideThumbnail(e.Source as Visual))
        {
            model.SelectLayerPixels(row);

            _pressedRow = null;
            _pressArgs = null;
            e.Handled = true;
            return;
        }

        // A double-click on the name starts a rename. This is checked here rather than with
        // a DoubleTapped handler on the TextBlock because the ListBox consumes the second
        // tap to extend selection before it reaches the row's own template.
        if (e.ClickCount == 2 && _pressedRow is not null && e.Source is not CheckBox)
        {
            _pressedRow.IsEditingName = true;
            _pressedRow = null;
            _pressArgs = null;
            return;
        }

        // A plain press on a row that is already part of a multiple selection is held back, so
        // that the selection survives long enough to be dragged. Ctrl and Shift are excluded
        // because there the artist is editing the selection and means it to change now.
        if (_pressedRow is { } marked
            && Model is { } panel
            && panel.MarkedRows.Count > 1
            && panel.MarkedRows.Contains(marked)
            && e.KeyModifiers == KeyModifiers.None)
        {
            _deferredCollapse = marked;
            e.Handled = true;
        }
    }

    private async void OnRowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedRow is not { } row
            || _pressArgs is not { } pressArgs
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Point position = e.GetPosition(this);
        if (Math.Abs(position.X - _pressOrigin.X) < DragThreshold
            && Math.Abs(position.Y - _pressOrigin.Y) < DragThreshold)
        {
            return;
        }

        // Clear the pending press before awaiting: the drag runs a nested input loop, and
        // re-entering this handler with the same row would start a second drag. The held-back
        // collapse is dropped too: this press turned out to be a drag, so the selection it was
        // going to narrow is the selection being dragged.
        _pressedRow = null;
        _pressArgs = null;
        _deferredCollapse = null;

        DataTransfer payload = new();
        payload.Add(DataTransferItem.Create(LayerRowFormat, row));

        try
        {
            // Move and Copy are both offered because the same drag can end as a restack, a
            // move into a folder, a delete — all moves — or on the new-layer button, which
            // copies.
            await DragDrop.DoDragDropAsync(pressArgs, payload, DragDropEffects.Move | DragDropEffects.Copy)
                .ConfigureAwait(true);
        }
        finally
        {
            ClearDropIndicators();
        }
    }

    private void OnRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pressedRow = null;
        _pressArgs = null;

        // The press never became a drag, so it was an ordinary click after all and means what a
        // click on a multiple selection always means: keep this one, drop the rest.
        if (_deferredCollapse is { } row && Model is { } model)
        {
            _deferredCollapse = null;

            model.SelectedLayer = row;
            model.SetMarkedRows([row]);
            SyncListSelection(row);
        }
    }

    /// <summary>Reports the list box selection to the view model as the marked set.</summary>
    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItems: { } selected } && Model is { } model)
        {
            model.SetMarkedRows(selected.OfType<LayerItemViewModel>());
        }
    }

    /// <summary>Narrows the list box to a single row, without re-entering the handler above.</summary>
    private void SyncListSelection(LayerItemViewModel row)
    {
        ListBox list = this.GetControl<ListBox>("LayerList");

        if (list.SelectedItems is { } selected && (selected.Count != 1 || selected[0] != row))
        {
            selected.Clear();
            selected.Add(row);
        }
    }

    // ---------------------------------------------------------------------------------
    // Receiving a drag
    // ---------------------------------------------------------------------------------

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(LayerRowFormat) is not { } dragged)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        ClearDropIndicators();

        // The buttons below the list are drop targets in their own right: each one applies
        // its own operation to the layer in the artist's hand. Duplicating is the only one
        // that copies, so it is the only one that reports Copy back to the drag source.
        if (IsOverButton(e, NewLayerButtonName))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        if (IsOverButton(e, NewFolderButtonName) || IsOverButton(e, DeleteButtonName))
        {
            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        if (FindRow(e.Source as Visual) is { } target && !ReferenceEquals(target, dragged))
        {
            target.DropPosition = GetDropPosition(e, target);
            e.DragEffects = DragDropEffects.Move;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        ClearDropIndicators();

        if (e.DataTransfer.TryGetValue(LayerRowFormat) is not { } dragged || Model is not { } model)
        {
            return;
        }

        // The commands are invoked rather than called through a public method so that each
        // drop lands one entry on the undo stack, exactly as the equivalent button click
        // would. Execute bypasses CanExecute, which is deliberate: CanExecute can only ever
        // look at the selected layer, while these act on the dragged one, so each command
        // re-checks its own preconditions for the layer it is actually given.
        if (IsOverButton(e, NewLayerButtonName))
        {
            model.DuplicateLayerCommand.Execute(dragged);
            e.Handled = true;
            return;
        }

        if (IsOverButton(e, NewFolderButtonName))
        {
            model.GroupIntoFolderCommand.Execute(dragged);
            e.Handled = true;
            return;
        }

        if (IsOverButton(e, DeleteButtonName))
        {
            model.DeleteLayerCommand.Execute(dragged);
            e.Handled = true;
            return;
        }

        if (FindRow(e.Source as Visual) is { } target && !ReferenceEquals(target, dragged))
        {
            LayerDropPosition position = GetDropPosition(e, target);
            if (position == LayerDropPosition.Inside)
            {
                model.MoveIntoFolder(dragged, target);
            }
            else
            {
                model.ReorderLayer(dragged, target, position == LayerDropPosition.Above);
            }
        }

        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => ClearDropIndicators();

    /// <summary>
    /// Decides where a drop on this row would land: above it, below it, or — for a folder —
    /// inside it.
    /// </summary>
    /// <remarks>
    /// An ordinary row splits at its midpoint, which is the long-established convention for
    /// a reorderable list. A folder instead gets three bands: the top and bottom quarters
    /// still restack, and the middle half moves the layer inside. Without the edge bands
    /// there would be no way to drop a layer <i>beside</i> a folder, which is needed just as
    /// often as dropping one into it.
    /// </remarks>
    private LayerDropPosition GetDropPosition(DragEventArgs e, LayerItemViewModel row)
    {
        if (FindRowContainer(row) is not { } container)
        {
            return LayerDropPosition.Above;
        }

        double height = container.Bounds.Height;
        double y = e.GetPosition(container).Y;

        if (!row.IsGroup)
        {
            return y < height / 2.0 ? LayerDropPosition.Above : LayerDropPosition.Below;
        }

        return y switch
        {
            _ when y < height * 0.25 => LayerDropPosition.Above,
            _ when y > height * 0.75 => LayerDropPosition.Below,
            _ => LayerDropPosition.Inside,
        };
    }

    /// <summary>Tests the pointer against one of the named buttons below the list.</summary>
    /// <remarks>
    /// The buttons are hit-tested by position rather than by handling drops on each button,
    /// because a Button consumes pointer input for its own press handling and never sees the
    /// drag events. Coordinates are asked for relative to the button itself, so the test is
    /// unaffected by where the panel sits or how it has been docked.
    /// </remarks>
    private bool IsOverButton(DragEventArgs e, string name)
    {
        Button button = this.GetControl<Button>(name);
        Point local = e.GetPosition(button);
        return local.X >= 0 && local.Y >= 0 && local.X < button.Bounds.Width && local.Y < button.Bounds.Height;
    }

    /// <summary>Tests whether a hit-tested visual is inside a row's preview image.</summary>
    /// <remarks>
    /// The preview is found by its tag rather than by its name: a name inside a data template
    /// belongs to every row at once, so it cannot identify one.
    /// </remarks>
    private static bool IsInsideThumbnail(Visual? source)
    {
        for (Visual? visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Border { Tag: "thumbnail" })
            {
                return true;
            }

            if (visual is ListBoxItem)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>Walks up from a hit-tested visual to the row view model it belongs to.</summary>
    private static LayerItemViewModel? FindRow(Visual? source)
    {
        for (Visual? visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is ListBoxItem { DataContext: LayerItemViewModel row })
            {
                return row;
            }
        }

        return null;
    }

    private ListBoxItem? FindRowContainer(LayerItemViewModel row)
    {
        ListBox list = this.GetControl<ListBox>("LayerList");
        return list.ContainerFromItem(row) as ListBoxItem;
    }

    private void ClearDropIndicators()
    {
        if (Model is not { } model)
        {
            return;
        }

        foreach (LayerItemViewModel row in model.Layers)
        {
            row.DropPosition = LayerDropPosition.None;
        }
    }

    // ---------------------------------------------------------------------------------
    // Renaming
    // ---------------------------------------------------------------------------------

    /// <summary>Focuses the rename box and selects the whole name the moment it appears.</summary>
    /// <remarks>
    /// Posted rather than run inline. The box appears during the second half of the
    /// double-click that asked for the rename, and that click then selects the word under
    /// the pointer — so selecting here directly is immediately undone, and typing replaces
    /// one word of the name instead of all of it. Deferring by one dispatcher pass lets the
    /// click finish first.
    /// </remarks>
    private static void OnRenameBoxAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextBox { IsVisible: true } box)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                box.Focus();
                box.SelectAll();
            },
            DispatcherPriority.Input);
    }

    private static void OnRenameBoxLostFocus(object? sender, RoutedEventArgs e) => EndRename(sender);

    private static void OnRenameBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                EndRename(sender);
                e.Handled = true;
                break;

            case Key.Escape:
                // Reverting the text before leaving edit mode discards the edit, because the
                // binding only writes the new name when the box loses focus with it changed.
                if (sender is TextBox box && box.DataContext is LayerItemViewModel row)
                {
                    box.Text = row.Name;
                    row.IsEditingName = false;
                }

                e.Handled = true;
                break;

            default:
                break;
        }
    }

    private static void EndRename(object? sender)
    {
        if (sender is TextBox { DataContext: LayerItemViewModel row })
        {
            row.IsEditingName = false;
        }
    }
}
