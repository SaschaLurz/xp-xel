using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace XPXel.UI.Views;

/// <summary>
/// The desktop window. A frame around <see cref="MainView"/> and nothing more.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Initialises the window.</summary>
    public MainWindow() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
