using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XPXel.Core.Documents;
using XPXel.Core.Primitives;

namespace XPXel.UI.ViewModels.Dialogs;

/// <summary>
/// What the artist asked the canvas to become.
/// </summary>
/// <param name="Size">The new canvas size.</param>
/// <param name="Anchor">Which part of the existing artwork keeps its place.</param>
public sealed record CanvasResizeRequest(ImageSize Size, CanvasAnchor Anchor);

/// <summary>
/// Asks for a new canvas size, in either direction.
/// </summary>
/// <remarks>
/// <para>
/// <b>One dialog for cropping and for enlarging.</b> Photoshop splits these into "Image Size"
/// and "Canvas Size" and Aseprite into "Canvas Size" and "Crop", but from the artist's side
/// they are the same decision — how big is the picture, and where does what I already drew
/// sit inside it. Asking for a width, a height and an anchor covers both: smaller numbers
/// crop, larger numbers pad, and one number of each is perfectly normal.
/// </para>
/// <para>
/// This deliberately does <i>not</i> scale the artwork. Resampling pixel art is a destructive
/// operation with its own set of choices, and an artist who types 64 into a 32-wide document
/// here means "give me more room", not "double every pixel".
/// </para>
/// </remarks>
public sealed partial class CanvasSizeDialogViewModel : DialogViewModel
{
    /// <summary>The largest canvas dimension the dialog accepts.</summary>
    public const int MaximumDimension = 4096;

    private readonly ImageSize _current;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    private int _width;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    private int _height;

    [ObservableProperty]
    private CanvasAnchor _anchor = CanvasAnchor.Center;

    /// <summary>Initialises the dialog with the document's present size.</summary>
    /// <param name="current">The canvas size to start from.</param>
    public CanvasSizeDialogViewModel(ImageSize current)
        : base("Canvas Size")
    {
        _current = current;
        _width = current.Width;
        _height = current.Height;

        Anchors =
        [
            new CanvasAnchorOption(CanvasAnchor.TopLeft, "Top left"),
            new CanvasAnchorOption(CanvasAnchor.Top, "Top"),
            new CanvasAnchorOption(CanvasAnchor.TopRight, "Top right"),
            new CanvasAnchorOption(CanvasAnchor.Left, "Left"),
            new CanvasAnchorOption(CanvasAnchor.Center, "Centre"),
            new CanvasAnchorOption(CanvasAnchor.Right, "Right"),
            new CanvasAnchorOption(CanvasAnchor.BottomLeft, "Bottom left"),
            new CanvasAnchorOption(CanvasAnchor.Bottom, "Bottom"),
            new CanvasAnchorOption(CanvasAnchor.BottomRight, "Bottom right"),
        ];

        SetAnchor(Anchor);
    }

    /// <summary>The nine anchor choices, in reading order so a 3×3 grid lays them out right.</summary>
    public IReadOnlyList<CanvasAnchorOption> Anchors { get; }

    /// <summary>The sizes offered as one-click presets, matching the new-document dialog.</summary>
    public static IReadOnlyList<int> Presets { get; } = [8, 16, 32, 48, 64, 128, 256];

    /// <summary>
    /// A plain-language statement of what pressing Resize will do, so the artist can see
    /// they are about to lose pixels before they lose them.
    /// </summary>
    public string SummaryText
    {
        get
        {
            int width = Clamp(Width);
            int height = Clamp(Height);

            if (width == _current.Width && height == _current.Height)
            {
                return $"{_current.Width} × {_current.Height} — unchanged";
            }

            string change = width < _current.Width || height < _current.Height
                ? width > _current.Width || height > _current.Height ? "cropped and padded" : "cropped"
                : "padded";

            return $"{_current.Width} × {_current.Height} → {width} × {height} ({change})";
        }
    }

    /// <summary>Chooses which part of the artwork keeps its place.</summary>
    /// <param name="anchor">The anchor to select.</param>
    [RelayCommand]
    private void SetAnchor(CanvasAnchor anchor)
    {
        Anchor = anchor;

        // The options carry their own selected flag rather than the view comparing values,
        // because comparing an enum to a constant in XAML needs a converter per comparison.
        foreach (CanvasAnchorOption option in Anchors)
        {
            option.IsSelected = option.Anchor == anchor;
        }
    }

    /// <summary>Sets both dimensions from a preset.</summary>
    /// <param name="size">The square size to apply.</param>
    [RelayCommand]
    private void ApplyPreset(int size)
    {
        Width = size;
        Height = size;
    }

    /// <summary>Restores the document's current size.</summary>
    [RelayCommand]
    private void Reset()
    {
        Width = _current.Width;
        Height = _current.Height;
    }

    /// <summary>Closes the dialog, returning the requested size and anchor.</summary>
    [RelayCommand]
    private void Resize() => Close(new CanvasResizeRequest(new ImageSize(Clamp(Width), Clamp(Height)), Anchor));

    private static int Clamp(int value) => Math.Clamp(value, 1, MaximumDimension);
}

/// <summary>
/// One cell of the anchor grid.
/// </summary>
/// <remarks>
/// A tiny view model rather than nine properties on the dialog: the grid is then an
/// <c>ItemsControl</c> over a list, which lays out in reading order for free and gains a new
/// cell if the enum ever does.
/// </remarks>
public sealed class CanvasAnchorOption : ViewModelBase
{
    private bool _isSelected;

    /// <summary>Initialises the option.</summary>
    /// <param name="anchor">The anchor this cell selects.</param>
    /// <param name="description">A human-readable name, shown as a tooltip.</param>
    public CanvasAnchorOption(CanvasAnchor anchor, string description)
    {
        Anchor = anchor;
        Description = description;
    }

    /// <summary>The anchor this cell selects.</summary>
    public CanvasAnchor Anchor { get; }

    /// <summary>A human-readable name, shown as a tooltip.</summary>
    public string Description { get; }

    /// <summary>Whether this is the chosen anchor.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
