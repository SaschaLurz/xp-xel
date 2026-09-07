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
/// say. And every <b>bare</b> key — X to swap the colours, one letter per tool, Escape to
/// deselect and Delete to clear what is marked — because a <c>KeyBinding</c> fires wherever the
/// key is pressed, so binding "B" to the brush would switch tools in the middle of renaming a
/// layer to "Body", and binding Backspace would erase the artwork instead of a character.
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
            if (!HandleClipboardShortcut(viewModel, e))
            {
                HandleZoomShortcut(viewModel, e);
            }

            return;
        }

        if (e.KeyModifiers != KeyModifiers.None || IsTypingSomewhere())
        {
            return;
        }

        HandleBareKey(viewModel, e);
    }

    /// <summary>
    /// Handles Ctrl+C, Ctrl+X and Ctrl+V, unless something is being typed into.
    /// </summary>
    /// <returns><see langword="true"/> if the key was one of the three, handled or deliberately left alone.</returns>
    /// <remarks>
    /// <para>
    /// These are here rather than declared as <c>KeyBinding</c>s because a text box already has
    /// its own meaning for all three, and a binding fires wherever the key is pressed. Claiming
    /// Ctrl+V globally would make it impossible to paste a name into the layers panel, and
    /// Ctrl+C there would copy pixels instead of the word under the caret.
    /// </para>
    /// <para>
    /// A text layer being typed on the canvas is the one exception, and only for paste: there is
    /// no text box to hand the key to, and pasted words belong in the words being typed. Copy and
    /// cut are held back there entirely — neither has a meaning for a caret with nothing selected,
    /// and cutting pixels out from under someone mid-sentence is not what they asked for.
    /// </para>
    /// </remarks>
    private bool HandleClipboardShortcut(MainViewModel viewModel, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Control)
        {
            return false;
        }

        switch (e.Key)
        {
            case Key.C or Key.X when !IsTypingSomewhere():
                Execute(e.Key == Key.C ? viewModel.CopyCommand : viewModel.CutCommand, e);
                return true;

            case Key.V when !IsTypingIntoTextBox():
                Execute(viewModel.PasteCommand, e);
                return true;

            case Key.C or Key.X or Key.V:
                // Being typed into: the key belongs to the text box or the text layer, and is
                // claimed here only so that the zoom shortcuts are not offered it next.
                return true;

            default:
                return false;
        }
    }

    /// <summary>Runs a command if it is available, and marks the key as dealt with either way.</summary>
    private static void Execute(System.Windows.Input.ICommand command, KeyEventArgs e)
    {
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }

        e.Handled = true;
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
        // Escape and the two delete keys act on the selection, and they reach this handler only
        // when nothing nearer has claimed them: the canvas takes Escape first to cancel a move
        // or to put a crop frame back, and takes Backspace and Delete first while a text layer
        // is being typed into. What is left over is the case an artist means by these keys when
        // there are marching ants on screen — get rid of them, or get rid of what they mark.
        switch (e.Key)
        {
            case Key.Escape:
                viewModel.DeselectCommand.Execute(null);
                e.Handled = true;
                return;

            case Key.Delete or Key.Back:
                if (viewModel.EraseSelectionCommand.CanExecute(null))
                {
                    viewModel.EraseSelectionCommand.Execute(null);
                    e.Handled = true;
                }

                return;

            default:
                break;
        }

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
    private bool IsTypingSomewhere() =>
        IsTypingIntoTextBox()
        || _keyboardHost?.FocusManager?.GetFocusedElement() is PixelCanvasControl { IsEditingText: true };

    /// <summary>
    /// Whether an ordinary text box has the keyboard — a layer's name, an animation's name.
    /// </summary>
    /// <remarks>
    /// Narrower than <see cref="IsTypingSomewhere"/> on purpose. A text box has its own copy,
    /// cut and paste and must keep them; a text layer on the canvas has no text box at all, and
    /// paste there is the shell's job.
    /// </remarks>
    private bool IsTypingIntoTextBox() =>
        _keyboardHost?.FocusManager?.GetFocusedElement() is TextBox or AutoCompleteBox;
}
