using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace XPXel.UI.Views;

/// <summary>
/// Code-behind for the timeline: it keeps the pinned layer-name column scrolled in step with
/// the cel grid beside it.
/// </summary>
/// <remarks>
/// <para>
/// The two have to be separate scrollers because the names must stay put while the frames
/// scroll sideways, and a single scroller cannot pin one column. Vertically they must agree
/// exactly, or a row label ends up beside the wrong row — which is the kind of bug that makes
/// an artist delete the wrong drawing.
/// </para>
/// <para>
/// The grid is the one that leads: it is what the artist scrolls with the wheel, and the name
/// column follows it. Driving it in both directions would set up a feedback loop between the
/// two offsets.
/// </para>
/// <para>
/// <b>The controls are looked up by name rather than through the fields Avalonia's name
/// generator produces.</b> Those fields are only assigned by the generator's own
/// <c>InitializeComponent</c>, and every view in this project writes its own — so the fields
/// exist, stay null, and throw the first time the panel is shown. Looking them up is what the
/// rest of the project does for the same reason.
/// </para>
/// </remarks>
public partial class TimelineView : UserControl
{
    private ScrollViewer? _grid;
    private ScrollViewer? _layerNames;

    /// <summary>Initialises the view.</summary>
    public TimelineView()
    {
        InitializeComponent();

        AttachedToVisualTree += (_, _) => Subscribe(true);
        DetachedFromVisualTree += (_, _) => Subscribe(false);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Subscribe(bool subscribe)
    {
        if (_grid is not null)
        {
            _grid.PropertyChanged -= OnGridScrolled;
        }

        if (!subscribe)
        {
            _grid = null;
            _layerNames = null;
            return;
        }

        _grid = this.FindControl<ScrollViewer>("FrameGrid");
        _layerNames = this.FindControl<ScrollViewer>("LayerNameColumn");

        if (_grid is not null)
        {
            _grid.PropertyChanged += OnGridScrolled;
        }
    }

    private void OnGridScrolled(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ScrollViewer.OffsetProperty || _grid is null || _layerNames is null)
        {
            return;
        }

        // Only the vertical offset is copied. The name column has nothing to scroll sideways,
        // and pushing a horizontal offset into it would slide the names out of view.
        _layerNames.Offset = _layerNames.Offset.WithY(_grid.Offset.Y);
    }
}
