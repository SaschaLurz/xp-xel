using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace XPXel.UI.Views;

/// <summary>Code-behind for <see cref="NewDocumentDialogView"/>. Layout and behaviour live in the XAML and the view model.</summary>
public partial class NewDocumentDialogView : UserControl
{
    /// <summary>Initialises the view.</summary>
    public NewDocumentDialogView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
