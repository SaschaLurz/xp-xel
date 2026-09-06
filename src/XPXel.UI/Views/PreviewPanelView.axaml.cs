using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace XPXel.UI.Views;

/// <summary>Code-behind for <see cref="PreviewPanelView"/>. Layout and behaviour live in the XAML and the view model.</summary>
public partial class PreviewPanelView : UserControl
{
    /// <summary>Initialises the view.</summary>
    public PreviewPanelView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
