using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace XPXel.UI.Views;

/// <summary>Code-behind for <see cref="ExportAnimationDialogView"/>. Layout and behaviour live in the XAML and the view model.</summary>
public partial class ExportAnimationDialogView : UserControl
{
    /// <summary>Initialises the view.</summary>
    public ExportAnimationDialogView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
