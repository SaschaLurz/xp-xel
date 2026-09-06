using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using XPXel.UI.Controls;
using XPXel.UI.Docking;

namespace XPXel.UI.Views;

/// <summary>
/// Code-behind for a document tab: connects the tab's zoom requests and the space key to its
/// own canvas.
/// </summary>
/// <remarks>
/// <para>
/// The subscriptions are made and unmade with the visual tree rather than in the constructor.
/// Document tabs are created and destroyed as the artist opens, closes, floats and re-docks
/// them, and a handler left attached to a detached view would keep the whole document alive
/// and quietly apply zoom changes to a canvas nobody can see.
/// </para>
/// <para>
/// <b>Why the space key is watched at the window level.</b> Holding space to grab the canvas
/// has to work no matter which panel was clicked last — an artist who has just picked a
/// layer is precisely the one who wants to reposition the view. Listening only on the canvas
/// would make the shortcut depend on invisible focus state. The handler is a tunnelling one
/// on the window so it is seen before any control acts on it, and it steps aside whenever a
/// text box has focus, because there a space is a space.
/// </para>
/// </remarks>
public partial class EditorDocumentView : UserControl
{
    private EditorDocument? _subscribed;
    private TopLevel? _keyboardHost;

    /// <summary>Initialises the view.</summary>
    public EditorDocumentView()
    {
        InitializeComponent();

        AttachedToVisualTree += (_, _) =>
        {
            Subscribe(DataContext as EditorDocument);
            AttachKeyboard(TopLevel.GetTopLevel(this));
        };

        DetachedFromVisualTree += (_, _) =>
        {
            Subscribe(null);
            AttachKeyboard(null);
        };

        DataContextChanged += (_, _) => Subscribe(DataContext as EditorDocument);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Subscribe(EditorDocument? document)
    {
        if (ReferenceEquals(_subscribed, document))
        {
            return;
        }

        if (_subscribed is not null)
        {
            _subscribed.ZoomRequested -= OnZoomRequested;
        }

        _subscribed = document;

        if (_subscribed is not null)
        {
            _subscribed.ZoomRequested += OnZoomRequested;
        }
    }

    // ---------------------------------------------------------------------------------
    // Space to pan
    // ---------------------------------------------------------------------------------

    private void AttachKeyboard(TopLevel? host)
    {
        if (ReferenceEquals(_keyboardHost, host))
        {
            return;
        }

        if (_keyboardHost is not null)
        {
            _keyboardHost.RemoveHandler(KeyDownEvent, OnHostKeyDown);
            _keyboardHost.RemoveHandler(KeyUpEvent, OnHostKeyUp);
        }

        _keyboardHost = host;

        if (_keyboardHost is not null)
        {
            _keyboardHost.AddHandler(KeyDownEvent, OnHostKeyDown, RoutingStrategies.Tunnel);
            _keyboardHost.AddHandler(KeyUpEvent, OnHostKeyUp, RoutingStrategies.Tunnel);
        }
    }

    private void OnHostKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || !CanTakeSpaceKey())
        {
            return;
        }

        Canvas.SetSpacePanArmed(true);
        e.Handled = true;
    }

    private void OnHostKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space)
        {
            return;
        }

        // Disarming is not gated on focus. If the artist armed the pan and then tabbed into
        // a text box before letting go, the release must still be honoured or the canvas
        // stays stuck showing a hand.
        Canvas.SetSpacePanArmed(false);
    }

    /// <summary>
    /// Whether this document's canvas should claim the space key right now.
    /// </summary>
    /// <remarks>
    /// Three conditions. The tab has to actually be on screen, or a background tab would pan
    /// invisibly and every open document would fight over the key. No text box may have
    /// focus, because renaming a layer to "Front Arm" needs the space to reach the box. And
    /// the canvas must not be in the middle of a text edit, which is the same rule applied to
    /// the one text field that is not a text box.
    /// </remarks>
    private bool CanTakeSpaceKey()
    {
        if (!IsEffectivelyVisible || Canvas.IsEditingText)
        {
            return false;
        }

        object? focused = _keyboardHost?.FocusManager?.GetFocusedElement();
        return focused is not (TextBox or AutoCompleteBox);
    }

    private PixelCanvasControl Canvas => this.GetControl<PixelCanvasControl>("PixelCanvas");

    private void OnZoomRequested(object? sender, CanvasZoomRequest request)
    {
        PixelCanvasControl canvas = Canvas;

        switch (request)
        {
            case CanvasZoomRequest.In:
                canvas.ZoomIn();
                break;

            case CanvasZoomRequest.Out:
                canvas.ZoomOut();
                break;

            case CanvasZoomRequest.ActualSize:
                canvas.ZoomToActualSize();
                break;

            case CanvasZoomRequest.Fit:
                canvas.ZoomToFit();
                break;

            default:
                break;
        }
    }
}
