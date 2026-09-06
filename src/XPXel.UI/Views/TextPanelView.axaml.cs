using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using XPXel.UI.ViewModels;

namespace XPXel.UI.Views;

/// <summary>Code-behind for <see cref="TextPanelView"/>. Layout and behaviour live in the XAML and the view model.</summary>
public partial class TextPanelView : UserControl
{
    /// <summary>Initialises the view.</summary>
    public TextPanelView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Closes the editing episode when the artist leaves the text box.
    /// </summary>
    /// <remarks>
    /// The box writes through on every keystroke so the canvas keeps up, which means something
    /// has to decide where one edit ends and the next begins. Leaving the box is that moment,
    /// and it is a view concern — the view model is never told about focus — which is why the
    /// one line lives here.
    /// </remarks>
    private void OnTextLostFocus(object? sender, RoutedEventArgs e) =>
        (DataContext as TextPanelViewModel)?.CommitPendingText();
}
