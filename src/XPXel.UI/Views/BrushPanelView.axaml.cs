using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace XPXel.UI.Views;

/// <summary>Code-behind for <see cref="BrushPanelView"/>. Layout and behaviour live in the XAML and the view model.</summary>
public partial class BrushPanelView : UserControl
{
    /// <summary>Initialises the view.</summary>
    public BrushPanelView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
