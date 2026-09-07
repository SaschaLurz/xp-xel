using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace XPXel.UI.Views;

/// <summary>Code-behind for <see cref="SaveLayoutDialogView"/>. Layout and behaviour live in the XAML and the view model.</summary>
public partial class SaveLayoutDialogView : UserControl
{
    /// <summary>Initialises the view.</summary>
    public SaveLayoutDialogView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
