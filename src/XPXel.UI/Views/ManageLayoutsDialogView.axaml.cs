using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace XPXel.UI.Views;

/// <summary>Code-behind for <see cref="ManageLayoutsDialogView"/>. Layout and behaviour live in the XAML and the view model.</summary>
public partial class ManageLayoutsDialogView : UserControl
{
    /// <summary>Initialises the view.</summary>
    public ManageLayoutsDialogView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
