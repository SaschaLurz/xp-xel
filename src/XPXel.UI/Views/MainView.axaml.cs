using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using XPXel.UI.Controls;
using XPXel.UI.ViewModels;

namespace XPXel.UI.Views;

/// <summary>
/// Code-behind for the application shell.
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately very little here. Two things genuinely belong in code-behind rather
/// than in the view model:
/// </para>
/// <list type="bullet">
///   <item><description>
///     The zoom commands, which act on the canvas control's own view state. A view model has
///     no business knowing the size of the viewport.
///   </description></item>
///   <item><description>
///     Forwarding the canvas's hovered pixel and zoom to the view model for the status bar.
///     These are readings <em>from</em> a control, so a binding would have to flow the wrong
///     way; observing the control's property changes is clearer than fighting binding modes.
///   </description></item>
/// </list>
/// </remarks>
public partial class MainView : UserControl
{
    private readonly PixelCanvasControl _canvas;

    /// <summary>Initialises the shell.</summary>
    public MainView()
    {
        InitializeComponent();

        // Looked up by name rather than relying on a generated field. The control is named
        // PixelCanvas rather than Canvas because a field called Canvas would collide with
        // Avalonia's own Canvas panel type.
        _canvas = this.GetControl<PixelCanvasControl>("PixelCanvas");
        _canvas.PropertyChanged += OnCanvasPropertyChanged;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnCanvasPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        if (e.Property == PixelCanvasControl.HoveredPixelProperty)
        {
            viewModel.HoveredPixel = _canvas.HoveredPixel;
        }
        else if (e.Property == PixelCanvasControl.ZoomProperty)
        {
            viewModel.Zoom = _canvas.Zoom;
        }
    }

    /// <summary>
    /// Handles the zoom shortcuts. They are not declared as KeyBindings because they act on
    /// the canvas rather than on a view-model command, and because the plus and minus keys
    /// have to be accepted in both their main-keyboard and numeric-keypad forms — which
    /// a single gesture cannot express.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.OemPlus or Key.Add:
                _canvas.ZoomIn();
                break;

            case Key.OemMinus or Key.Subtract:
                _canvas.ZoomOut();
                break;

            case Key.D0 or Key.NumPad0:
                _canvas.ZoomToActualSize();
                break;

            case Key.D1 or Key.NumPad1:
                _canvas.ZoomToFit();
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    private void OnZoomInClick(object? sender, RoutedEventArgs e) => _canvas.ZoomIn();

    private void OnZoomOutClick(object? sender, RoutedEventArgs e) => _canvas.ZoomOut();

    private void OnZoomActualClick(object? sender, RoutedEventArgs e) => _canvas.ZoomToActualSize();

    private void OnZoomFitClick(object? sender, RoutedEventArgs e) => _canvas.ZoomToFit();
}
