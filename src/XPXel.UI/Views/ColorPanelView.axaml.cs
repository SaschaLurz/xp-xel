using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace XPXel.UI.Views;

/// <summary>Code-behind for <see cref="ColorPanelView"/>. Layout and behaviour live in the XAML and the view model.</summary>
public partial class ColorPanelView : UserControl
{
    /// <summary>Initialises the view.</summary>
    public ColorPanelView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
