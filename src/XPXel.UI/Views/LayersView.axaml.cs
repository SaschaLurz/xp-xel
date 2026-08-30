using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace XPXel.UI.Views;

/// <summary>Code-behind for <see cref="LayersView"/>. Layout and behaviour live in the XAML and the view model.</summary>
public partial class LayersView : UserControl
{
    /// <summary>Initialises the view.</summary>
    public LayersView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
