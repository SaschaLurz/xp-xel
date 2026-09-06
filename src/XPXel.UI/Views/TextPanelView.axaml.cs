using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace XPXel.UI.Views;

/// <summary>Code-behind for <see cref="TextPanelView"/>. Layout and behaviour live in the XAML and the view model.</summary>
public partial class TextPanelView : UserControl
{
    /// <summary>Initialises the view.</summary>
    public TextPanelView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
