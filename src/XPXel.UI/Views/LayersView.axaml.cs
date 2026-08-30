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
/// Code-behind for the layers panel: drag-and-drop restacking, dropping a layer on the
/// "new layer" button to duplicate it, and the double-click-to-rename interaction.
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

    private Point _pressOrigin;
    private LayerItemViewModel? _pressedRow;

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

        // A double-click on the name starts a rename. This is checked here rather than with
        // a DoubleTapped handler on the TextBlock because the ListBox consumes the second
        // tap to extend selection before it reaches the row's own template.
        if (e.ClickCount == 2 && _pressedRow is not null && e.Source is not CheckBox)
        {
            _pressedRow.IsEditingName = true;
            _pressedRow = null;
            _pressArgs = null;
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
        // re-entering this handler with the same row would start a second drag.
        _pressedRow = null;
        _pressArgs = null;

        DataTransfer payload = new();
        payload.Add(DataTransferItem.Create(LayerRowFormat, row));

        try
        {
            // Move and Copy are both offered because the same drag can end on another row
            // (a restack) or on the new-layer button (a duplicate).
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

        // Dropping on the "new layer" button duplicates rather than moves, so the cursor
        // should say copy. This is the whole gesture the artist asked for: drag a layer onto
        // the button that makes layers, get a copy of it.
        if (IsOverNewLayerButton(e))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        if (FindRow(e.Source as Visual) is { } target && !ReferenceEquals(target, dragged))
        {
            target.DropPosition = IsAboveMidpoint(e, target) ? LayerDropPosition.Above : LayerDropPosition.Below;
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

        if (IsOverNewLayerButton(e))
        {
            model.DuplicateLayerCommand.Execute(dragged);
            e.Handled = true;
            return;
        }

        if (FindRow(e.Source as Visual) is { } target && !ReferenceEquals(target, dragged))
        {
            model.ReorderLayer(dragged, target, IsAboveMidpoint(e, target));
        }

        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => ClearDropIndicators();

    /// <summary>
    /// Decides whether the pointer is in the upper or lower half of a row, which is what
    /// makes the drop land above or below it.
    /// </summary>
    private bool IsAboveMidpoint(DragEventArgs e, LayerItemViewModel row)
    {
        if (FindRowContainer(row) is not { } container)
        {
            return true;
        }

        return e.GetPosition(container).Y < container.Bounds.Height / 2.0;
    }

    private bool IsOverNewLayerButton(DragEventArgs e)
    {
        Button button = this.GetControl<Button>("NewLayerButton");
        Point local = e.GetPosition(button);
        return local.X >= 0 && local.Y >= 0 && local.X < button.Bounds.Width && local.Y < button.Bounds.Height;
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
