using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace XPXel.UI.Views;

/// <summary>Code-behind for <see cref="HistoryPanelView"/>. Everything lives in the XAML and the view model.</summary>
public partial class HistoryPanelView : UserControl
{
    /// <summary>Initialises the view.</summary>
    public HistoryPanelView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
