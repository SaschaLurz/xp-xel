using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace XPXel.UI.Views;

/// <summary>Code-behind for <see cref="CanvasSizeDialogView"/>. Layout and behaviour live in the XAML and the view model.</summary>
public partial class CanvasSizeDialogView : UserControl
{
    /// <summary>Initialises the view.</summary>
    public CanvasSizeDialogView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
