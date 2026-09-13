using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace XPXel.UI.Views;

/// <summary>Code-behind for <see cref="SettingsDialogView"/>. Layout and behaviour live in the XAML and the view model.</summary>
public partial class SettingsDialogView : UserControl
{
    /// <summary>Initialises the view.</summary>
    public SettingsDialogView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
