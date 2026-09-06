using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using XPXel.UI.ViewModels;

namespace XPXel.UI.Views;

/// <summary>
/// Code-behind for <see cref="ColorPanelView"/>: opening the colour editor on a right-click.
/// </summary>
/// <remarks>
/// <para>
/// The editor is one <see cref="Flyout"/> declared in the panel's resources and shown against
/// whichever swatch was clicked, rather than a <c>ContextFlyout</c> on each swatch. A palette
/// can hold dozens of colours and only one editor can be open, so building one per swatch
/// would allocate a popup, a ramp and five harmonies apiece for the sake of a menu the artist
/// opens on one of them.
/// </para>
/// <para>
/// Because the flyout is shared, its content cannot bind its way to the editor: it is shown
/// against a swatch, whose data context is that swatch rather than the panel. The data
/// context is therefore assigned here, at the moment the editor is pointed at something.
/// </para>
/// </remarks>
public partial class ColorPanelView : UserControl
{
    /// <summary>The key under which the editor flyout is declared in the panel's resources.</summary>
    private const string EditorFlyoutKey = "ColorEditorFlyout";

    /// <summary>Initialises the view.</summary>
    public ColorPanelView() => InitializeComponent();

    private PaletteViewModel? ViewModel => DataContext as PaletteViewModel;

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Opens the editor on a palette swatch.</summary>
    private void OnSwatchPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right
            || sender is not Control { DataContext: PaletteSwatchViewModel swatch } anchor
            || ViewModel is not { } model)
        {
            return;
        }

        model.BeginEdit(swatch);
        ShowEditor(anchor, model);
        e.Handled = true;
    }

    /// <summary>Opens the editor on the primary colour, which has no palette slot of its own.</summary>
    private void OnPrimaryPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right
            || sender is not Control anchor
            || ViewModel is not { } model)
        {
            return;
        }

        model.BeginEditPrimary();
        ShowEditor(anchor, model);
        e.Handled = true;
    }

    private void ShowEditor(Control anchor, PaletteViewModel model)
    {
        if (!Resources.TryGetResource(EditorFlyoutKey, ActualThemeVariant, out object? found)
            || found is not Flyout flyout)
        {
            return;
        }

        if (flyout.Content is Control content)
        {
            content.DataContext = model.Editor;
        }

        flyout.ShowAt(anchor, showAtPointer: true);
    }
}
