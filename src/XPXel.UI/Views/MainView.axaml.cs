using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using XPXel.UI.Docking;
using XPXel.UI.ViewModels;

namespace XPXel.UI.Views;

/// <summary>
/// Code-behind for the application shell.
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately very little here. The only thing that genuinely belongs in
/// code-behind is the zoom shortcuts, and only because the plus and minus keys have to be
/// accepted in both their main-keyboard and numeric-keypad forms, which a single
/// <c>KeyBinding</c> gesture cannot express.
/// </para>
/// <para>
/// Note what is <i>no longer</i> here. Before documents became tabs this class held a
/// reference to the one canvas control and forwarded its zoom and hovered pixel to the view
/// model. With several canvases — one per tab, possibly in floating windows — looking one up
/// by name is meaningless. Each tab now owns its own canvas through
/// <see cref="EditorDocumentView"/>, and zoom travels as a request to the active document.
/// </para>
/// </remarks>
public partial class MainView : UserControl
{
    /// <summary>Initialises the shell.</summary>
    public MainView() => InitializeComponent();

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || ViewModel is not { } viewModel)
        {
            return;
        }

        CanvasZoomRequest request;
        switch (e.Key)
        {
            case Key.OemPlus or Key.Add:
                request = CanvasZoomRequest.In;
                break;

            case Key.OemMinus or Key.Subtract:
                request = CanvasZoomRequest.Out;
                break;

            case Key.D0 or Key.NumPad0:
                request = CanvasZoomRequest.ActualSize;
                break;

            case Key.D1 or Key.NumPad1:
                request = CanvasZoomRequest.Fit;
                break;

            default:
                return;
        }

        viewModel.ZoomCommand.Execute(request);
        e.Handled = true;
    }
}
