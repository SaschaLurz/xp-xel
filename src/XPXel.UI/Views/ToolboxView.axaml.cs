using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace XPXel.UI.Views;

/// <summary>Code-behind for <see cref="ToolboxView"/>. Layout and behaviour live in the XAML and the view model.</summary>
public partial class ToolboxView : UserControl
{
    /// <summary>Initialises the view.</summary>
    public ToolboxView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
