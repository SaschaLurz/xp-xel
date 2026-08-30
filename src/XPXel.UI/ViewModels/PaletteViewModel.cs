using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.Primitives;

namespace XPXel.UI.ViewModels;

/// <summary>
/// The colour panel: the two active colours, a channel editor for the primary one, and the
/// document's palette.
/// </summary>
/// <remarks>
/// <para>
/// The two active colours live in <see cref="ColorSlots"/>, which is application state,
/// while the palette belongs to the document. This view model presents both together
/// because that is how they are used — pick from the palette, adjust, put it back.
/// </para>
/// <para>
/// <b>The editor is channel sliders plus a hexadecimal field, not a colour wheel.</b> That
/// is a deliberate v0.1 choice rather than a limitation of the design: exact numeric values
/// are what pixel artists actually work with, hex is how palettes are shared, and both are
/// unambiguous. A spectrum picker is a worthwhile addition later and would slot in beside
/// these controls, writing to the same <see cref="ColorSlots"/>.
/// </para>
/// <para>
/// <b>Beware the synchronisation loop.</b> The channels, the hex field and the model all
/// describe the same colour. Writing any one of them notifies the others, which would
/// recurse without the <c>_isSynchronising</c> guard. Anything added here that touches the
/// colour must go through <see cref="ApplyPrimary"/>.
/// </para>
/// </remarks>
public sealed partial class PaletteViewModel : ViewModelBase, IDisposable
{
    private readonly ColorSlots _colors;
    private bool _isSynchronising;

    [ObservableProperty]
    private double _red;

    [ObservableProperty]
    private double _green;

    [ObservableProperty]
    private double _blue;

    [ObservableProperty]
    private double _alpha;

    [ObservableProperty]
    private string _hex = "#000000";

    [ObservableProperty]
    private Palette? _palette;

    /// <summary>Initialises the panel.</summary>
    /// <param name="colors">The application's primary and secondary colours.</param>
    public PaletteViewModel(ColorSlots colors)
    {
        ArgumentNullException.ThrowIfNull(colors);

        _colors = colors;
        _colors.PropertyChanged += OnColorSlotsChanged;

        PullFromModel();
    }

    /// <summary>A brush showing the primary colour.</summary>
    public IBrush PrimaryBrush => _colors.Primary.ToBrush();

    /// <summary>A brush showing the secondary colour.</summary>
    public IBrush SecondaryBrush => _colors.Secondary.ToBrush();

    /// <summary>Points the panel at a different document's palette, or at none.</summary>
    /// <param name="workspace">The document to present.</param>
    public void Attach(DocumentWorkspace? workspace) => Palette = workspace?.Document.Palette;

    /// <inheritdoc />
    public void Dispose() => _colors.PropertyChanged -= OnColorSlotsChanged;

    /// <summary>Loads a palette colour into the primary slot.</summary>
    /// <param name="color">The colour to load.</param>
    [RelayCommand]
    private void SelectPrimary(PixelColor color) => _colors.Primary = color;

    /// <summary>Loads a palette colour into the secondary slot.</summary>
    /// <param name="color">The colour to load.</param>
    [RelayCommand]
    private void SelectSecondary(PixelColor color) => _colors.Secondary = color;

    /// <summary>Exchanges the primary and secondary colours.</summary>
    [RelayCommand]
    private void SwapColors() => _colors.Swap();

    /// <summary>Appends the primary colour to the palette, if it is not already present.</summary>
    [RelayCommand]
    private void AddPrimaryToPalette()
    {
        if (Palette is { } palette && palette.IndexOf(_colors.Primary) < 0)
        {
            palette.Add(_colors.Primary);
        }
    }

    /// <summary>Removes a colour from the palette.</summary>
    /// <param name="color">The colour to remove.</param>
    [RelayCommand]
    private void RemoveFromPalette(PixelColor color)
    {
        if (Palette is { } palette)
        {
            int index = palette.IndexOf(color);
            if (index >= 0)
            {
                palette.RemoveAt(index);
            }
        }
    }

    partial void OnRedChanged(double value) => ApplyPrimaryFromChannels();

    partial void OnGreenChanged(double value) => ApplyPrimaryFromChannels();

    partial void OnBlueChanged(double value) => ApplyPrimaryFromChannels();

    partial void OnAlphaChanged(double value) => ApplyPrimaryFromChannels();

    partial void OnHexChanged(string value)
    {
        // Typing is incremental, so an unparseable value is normal and simply ignored until
        // it becomes valid rather than being rejected with an error.
        if (!_isSynchronising && PixelColor.TryParseHex(value, out PixelColor parsed))
        {
            ApplyPrimary(parsed);
        }
    }

    private void ApplyPrimaryFromChannels() => ApplyPrimary(new PixelColor(
        (byte)Math.Clamp(Red, 0, 255),
        (byte)Math.Clamp(Green, 0, 255),
        (byte)Math.Clamp(Blue, 0, 255),
        (byte)Math.Clamp(Alpha, 0, 255)));

    private void ApplyPrimary(PixelColor color)
    {
        if (_isSynchronising)
        {
            return;
        }

        _isSynchronising = true;
        try
        {
            _colors.Primary = color;
            PushToUi(color);
        }
        finally
        {
            _isSynchronising = false;
        }
    }

    private void PullFromModel()
    {
        _isSynchronising = true;
        try
        {
            PushToUi(_colors.Primary);
        }
        finally
        {
            _isSynchronising = false;
        }
    }

    private void PushToUi(PixelColor color)
    {
        Red = color.R;
        Green = color.G;
        Blue = color.B;
        Alpha = color.A;
        Hex = color.ToHex();

        OnPropertyChanged(nameof(PrimaryBrush));
        OnPropertyChanged(nameof(SecondaryBrush));
    }

    private void OnColorSlotsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isSynchronising)
        {
            return;
        }

        if (e.PropertyName == nameof(ColorSlots.Primary))
        {
            PullFromModel();
        }
        else if (e.PropertyName == nameof(ColorSlots.Secondary))
        {
            OnPropertyChanged(nameof(SecondaryBrush));
        }
    }
}
