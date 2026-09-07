using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace XPXel.UI.Views;

/// <summary>
/// Code-behind for <see cref="ColorPanelView"/>.
/// </summary>
/// <remarks>
/// Nothing but the loader. The panel used to open its colour editor as a flyout against
/// whichever swatch was right-clicked, which needed code here to point one shared popup at one
/// swatch at a time. The editor now lives in the panel itself and edits whatever is selected,
/// so the whole arrangement is ordinary binding and the code went away with it.
/// </remarks>
public partial class ColorPanelView : UserControl
{
    /// <summary>Initialises the view.</summary>
    public ColorPanelView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
