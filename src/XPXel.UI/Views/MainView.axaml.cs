using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using XPXel.UI.Controls;
using XPXel.UI.Docking;
using XPXel.UI.ViewModels;

namespace XPXel.UI.Views;

/// <summary>
/// Code-behind for the application shell: the keyboard shortcuts that a
/// <c>KeyBinding</c> cannot express.
/// </summary>
/// <remarks>
/// <para>
/// Two kinds of shortcut end up here. The zoom keys, because plus and minus have to be
/// accepted in both their main-keyboard and numeric-keypad forms, which one gesture cannot
/// say. And every <b>bare</b> key — X to swap the colours, one letter per tool — because a
/// <c>KeyBinding</c> fires wherever the key is pressed, so binding "B" to the brush would
/// switch tools in the middle of renaming a layer to "Body".
/// </para>
/// <para>
/// <b>Why the handler is on the window rather than on this control.</b> A key event routes
/// from whatever has focus, and immediately after launch nothing inside the shell does — so
/// an override of <see cref="InputElement.OnKeyDown"/> here would silently do nothing until
/// the artist had clicked something. Listening on the top level means the shortcut works from
/// the moment the window opens, which is the same reason the space-to-pan key is watched there
/// (see <see cref="EditorDocumentView"/>).
/// </para>
/// <para>
/// <b>The guard that makes bare keys safe</b> is <see cref="IsTypingSomewhere"/>. It has to
/// ask two questions, because text can be entered in two quite different places: an ordinary
/// text box, which focus identifies, and a text layer being typed directly on the canvas,
/// where there is no focused text box at all and the canvas has to answer for itself.
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
    private TopLevel? _keyboardHost;

    /// <summary>Initialises the shell.</summary>
    public MainView()
    {
        InitializeComponent();

        AttachedToVisualTree += (_, _) => AttachKeyboard(TopLevel.GetTopLevel(this));
        DetachedFromVisualTree += (_, _) => AttachKeyboard(null);
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void AttachKeyboard(TopLevel? host)
    {
        if (ReferenceEquals(_keyboardHost, host))
        {
            return;
        }

        if (_keyboardHost is not null)
        {
            _keyboardHost.RemoveHandler(KeyDownEvent, OnHostKeyDown);
        }

        _keyboardHost = host;

        // Bubbling, not tunnelling: a control that genuinely acted on the key — a text box
        // taking a Backspace, the canvas taking an arrow key while text is being typed — has
        // already marked it handled by the time it reaches here, and handled events are not
        // delivered to this handler at all.
        _keyboardHost?.AddHandler(KeyDownEvent, OnHostKeyDown, RoutingStrategies.Bubble);
    }

    private void OnHostKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            HandleZoomShortcut(viewModel, e);
            return;
        }

        if (e.KeyModifiers != KeyModifiers.None || IsTypingSomewhere())
        {
            return;
        }

        HandleBareKey(viewModel, e);
    }

    private static void HandleZoomShortcut(MainViewModel viewModel, KeyEventArgs e)
    {
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

    private static void HandleBareKey(MainViewModel viewModel, KeyEventArgs e)
    {
        // The digit row picks a colour out of the palette's first ten, the way a game's
        // hotbar works. Checked before the letters because the two ranges do not overlap and
        // this is the more frequent of the two by a wide margin.
        if (TryHandleColorHotkey(viewModel, e))
        {
            return;
        }

        if (e.Key is < Key.A or > Key.Z)
        {
            return;
        }

        // X is the odd one out: it swaps the two drawing colours rather than picking a tool,
        // and it is checked first so that no tool can ever claim it.
        if (e.Key == Key.X)
        {
            viewModel.SwapColorsCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (viewModel.TrySelectToolByShortcut(e.Key.ToString()))
        {
            e.Handled = true;
        }
    }

    /// <summary>Handles the digit row and the number pad as palette shortcuts.</summary>
    /// <returns>
    /// <see langword="true"/> if the key was a digit at all, whether or not a colour claimed
    /// it. A digit past the end of the palette is still a digit and must not fall through to
    /// the tool letters.
    /// </returns>
    private static bool TryHandleColorHotkey(MainViewModel viewModel, KeyEventArgs e)
    {
        int digit = e.Key switch
        {
            >= Key.D0 and <= Key.D9 => e.Key - Key.D0,
            >= Key.NumPad0 and <= Key.NumPad9 => e.Key - Key.NumPad0,
            _ => -1,
        };

        if (digit < 0)
        {
            return false;
        }

        if (viewModel.TrySelectColorByHotkey(digit))
        {
            e.Handled = true;
        }

        return true;
    }

    /// <summary>
    /// Whether the keystroke is text the artist is entering rather than a shortcut.
    /// </summary>
    private bool IsTypingSomewhere()
    {
        object? focused = _keyboardHost?.FocusManager?.GetFocusedElement();

        return focused is TextBox or AutoCompleteBox or PixelCanvasControl { IsEditingText: true };
    }
}
