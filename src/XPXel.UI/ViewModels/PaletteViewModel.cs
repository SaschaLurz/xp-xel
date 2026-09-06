using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
/// <b>The palette's first ten colours are a hotbar.</b> They are numbered on screen and
/// answer to the number keys, the way a game's inventory bar does, because reaching for a
/// colour is the most frequent thing an artist does and a trip across the window is a poor
/// price for it. Nothing in the model separates a hotbar slot from any other palette entry —
/// the numbers are simply what the first ten positions are worth — so the whole thing is
/// saved with the document and needs no storage of its own.
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

    private INotifyCollectionChanged? _observedColors;
    private PaletteSwatchViewModel? _editing;

    /// <summary>Initialises the panel.</summary>
    /// <param name="colors">The application's primary and secondary colours.</param>
    public PaletteViewModel(ColorSlots colors)
    {
        ArgumentNullException.ThrowIfNull(colors);

        _colors = colors;
        _colors.PropertyChanged += OnColorSlotsChanged;

        Editor.Applied += OnEditorApplied;
        Editor.Saved += OnEditorSaved;
        Editor.Removed += OnEditorRemoved;

        PullFromModel();
    }

    /// <summary>A brush showing the primary colour.</summary>
    public IBrush PrimaryBrush => _colors.Primary.ToBrush();

    /// <summary>A brush showing the secondary colour.</summary>
    public IBrush SecondaryBrush => _colors.Secondary.ToBrush();

    /// <summary>The palette's colours, in order, each knowing its own position.</summary>
    public ObservableCollection<PaletteSwatchViewModel> Swatches { get; } = [];

    /// <summary>
    /// The editor behind the right-click flyout.
    /// </summary>
    /// <remarks>
    /// One shared instance rather than one per swatch: only one flyout can be open at a time,
    /// and each editor recomputes a ramp and five harmonies on every slider move, which is
    /// not work to do twenty times over for swatches nobody is looking at.
    /// </remarks>
    public ColorEditorViewModel Editor { get; } = new();

    /// <summary>Points the panel at a different document's palette, or at none.</summary>
    /// <param name="workspace">The document to present.</param>
    public void Attach(DocumentWorkspace? workspace)
    {
        Palette = workspace?.Document.Palette;

        ObserveColors(Palette?.Colors as INotifyCollectionChanged);
        RebuildSwatches();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _colors.PropertyChanged -= OnColorSlotsChanged;
        ObserveColors(null);

        Editor.Applied -= OnEditorApplied;
        Editor.Saved -= OnEditorSaved;
        Editor.Removed -= OnEditorRemoved;
    }

    /// <summary>Selects the colour a number key names, if the palette has one there.</summary>
    /// <param name="digit">The digit that was pressed, 0 to 9.</param>
    /// <returns><see langword="true"/> if a colour claimed the key.</returns>
    /// <remarks>
    /// <c>1</c> to <c>9</c> take the first nine slots and <c>0</c> takes the tenth, which is
    /// how a hotbar has been numbered since long before this application. A digit past the
    /// end of the palette does nothing rather than clamping to the last colour: silently
    /// picking one the artist did not point at is worse than ignoring the key.
    /// </remarks>
    public bool TrySelectByHotkey(int digit)
    {
        if (digit is < 0 or > 9)
        {
            return false;
        }

        int index = digit == 0 ? Core.Documents.Palette.HotbarSize - 1 : digit - 1;
        if (Palette is not { } palette || index >= palette.Colors.Count)
        {
            return false;
        }

        _colors.Primary = palette.Colors[index];
        return true;
    }

    /// <summary>Points the right-click editor at a swatch.</summary>
    /// <param name="swatch">The swatch that was right-clicked.</param>
    public void BeginEdit(PaletteSwatchViewModel swatch)
    {
        ArgumentNullException.ThrowIfNull(swatch);

        _editing = swatch;

        string label = swatch.HasHotkey ? $"Slot {swatch.HotkeyLabel}" : $"Colour {swatch.Index + 1}";
        Editor.Load(swatch.Color, label, canSave: true);
    }

    /// <summary>Points the right-click editor at the primary colour, which has no slot.</summary>
    public void BeginEditPrimary()
    {
        _editing = null;
        Editor.Load(_colors.Primary, "Primary colour", canSave: false);
    }

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

    /// <summary>Loads a swatch's colour into the primary slot.</summary>
    /// <param name="swatch">The swatch that was clicked.</param>
    [RelayCommand]
    private void SelectSwatch(PaletteSwatchViewModel? swatch)
    {
        if (swatch is not null)
        {
            _colors.Primary = swatch.Color;
        }
    }

    private void OnEditorApplied(object? sender, PixelColor color) => _colors.Primary = color;

    private void OnEditorSaved(object? sender, PixelColor color)
    {
        if (Palette is { } palette && _editing is { } swatch && swatch.Index < palette.Colors.Count)
        {
            palette.Replace(swatch.Index, color);
        }

        // Writing a colour into a slot is also how an artist says "this is the one I want",
        // so it becomes the drawing colour as well rather than needing a second click.
        _colors.Primary = color;
    }

    private void OnEditorRemoved(object? sender, EventArgs e)
    {
        if (Palette is { } palette && _editing is { } swatch && swatch.Index < palette.Colors.Count)
        {
            palette.RemoveAt(swatch.Index);
        }

        _editing = null;
    }

    private void ObserveColors(INotifyCollectionChanged? colors)
    {
        if (ReferenceEquals(_observedColors, colors))
        {
            return;
        }

        if (_observedColors is not null)
        {
            _observedColors.CollectionChanged -= OnPaletteColorsChanged;
        }

        _observedColors = colors;

        if (_observedColors is not null)
        {
            _observedColors.CollectionChanged += OnPaletteColorsChanged;
        }
    }

    private void OnPaletteColorsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RebuildSwatches();

    /// <summary>
    /// Rebuilds every swatch rather than patching the one that changed.
    /// </summary>
    /// <remarks>
    /// A swatch carries its own position, so removing a colour renumbers everything after it
    /// — and the number is the key that selects it. Rebuilding is the only way to be sure the
    /// printed digits and the keyboard agree, and a palette has tens of entries, not
    /// thousands.
    /// </remarks>
    private void RebuildSwatches()
    {
        Swatches.Clear();

        if (Palette is not { } palette)
        {
            return;
        }

        for (int i = 0; i < palette.Colors.Count; i++)
        {
            Swatches.Add(new PaletteSwatchViewModel(i, palette.Colors[i]));
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
