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
/// The colour panel: the document's palette, and a colour editor wired straight into
/// whichever swatch of it is selected.
/// </summary>
/// <remarks>
/// <para>
/// <b>The palette comes first, and the editor edits a slot.</b> Choosing a colour is a
/// two-part act — find roughly the right one, then adjust it — and the palette is where the
/// first part happens, so it sits at the top where it is always in reach. The editor below
/// is not a separate place to make colours: it is a view of the selected swatch, and moving a
/// slider in it changes that swatch and the drawing colour together, as the slider moves.
/// There is no "save to palette" step because there is nothing to save; the swatch is already
/// the thing being edited.
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
/// <b>A drawing colour that is not in the palette is still editable.</b> The eyedropper, the
/// X key and a shading ramp can all leave the drawing colour on something the palette has
/// never held. The editor follows it anyway, no swatch is outlined, and the panel offers to
/// add it — which is the honest state of affairs rather than silently overwriting whichever
/// swatch happened to be selected a moment ago.
/// </para>
/// <para>
/// <b>Beware the synchronisation loop.</b> The editor, the palette and the colour slots all
/// describe the same colour, and each notifies the others. <c>_isSynchronising</c> is what
/// keeps that from recursing; anything added here that touches the colour must respect it.
/// </para>
/// </remarks>
public sealed partial class PaletteViewModel : ViewModelBase, IDisposable
{
    private readonly ColorSlots _colors;
    private bool _isSynchronising;

    [ObservableProperty]
    private Palette? _palette;

    /// <summary>The swatch the editor is pointed at, or <see langword="null"/> for none.</summary>
    [ObservableProperty]
    private PaletteSwatchViewModel? _selectedSwatch;

    private INotifyCollectionChanged? _observedColors;

    /// <summary>Initialises the panel.</summary>
    /// <param name="colors">The application's primary and secondary colours.</param>
    public PaletteViewModel(ColorSlots colors)
    {
        ArgumentNullException.ThrowIfNull(colors);

        _colors = colors;
        _colors.PropertyChanged += OnColorSlotsChanged;

        Editor.Changed += OnEditorChanged;

        PullFromModel();
    }

    /// <summary>A brush showing the primary colour.</summary>
    public IBrush PrimaryBrush => _colors.Primary.ToBrush();

    /// <summary>A brush showing the secondary colour.</summary>
    public IBrush SecondaryBrush => _colors.Secondary.ToBrush();

    /// <summary>The palette's colours, in order, each knowing its own position.</summary>
    public ObservableCollection<PaletteSwatchViewModel> Swatches { get; } = [];

    /// <summary>
    /// The colour editor shown beneath the palette.
    /// </summary>
    /// <remarks>
    /// One instance for the panel rather than one per swatch: only one colour is ever being
    /// edited, and each editor recomputes a ramp and five harmonies on every slider move,
    /// which is not work to do twenty times over for swatches nobody is looking at.
    /// </remarks>
    public ColorEditorViewModel Editor { get; } = new();

    /// <summary>Gets a value indicating whether a palette slot is being edited.</summary>
    /// <remarks>
    /// When it is not, the drawing colour came from somewhere else — the eyedropper, a
    /// harmony, the other colour slot — and the panel offers to add it to the palette rather
    /// than showing a remove button for a slot that does not exist.
    /// </remarks>
    public bool IsEditingSwatch => SelectedSwatch is not null;

    /// <summary>What the editor is currently editing, for the heading above it.</summary>
    public string EditorHeading => SelectedSwatch is not { } swatch
        ? "Not in the palette"
        : swatch.HasHotkey ? $"Slot {swatch.HotkeyLabel}" : $"Colour {swatch.Index + 1}";

    /// <summary>Points the panel at a different document's palette, or at none.</summary>
    /// <param name="workspace">The document to present.</param>
    public void Attach(DocumentWorkspace? workspace)
    {
        Palette = workspace?.Document.Palette;

        ObserveColors(Palette?.Colors as INotifyCollectionChanged);
        RebuildSwatches();
        PullFromModel();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _colors.PropertyChanged -= OnColorSlotsChanged;
        ObserveColors(null);

        Editor.Changed -= OnEditorChanged;
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

        if (index >= Swatches.Count)
        {
            return false;
        }

        Select(Swatches[index]);
        return true;
    }

    /// <summary>Makes a swatch the one the editor and the brush are both pointed at.</summary>
    /// <param name="swatch">The swatch that was clicked, or <see langword="null"/> to select none.</param>
    public void Select(PaletteSwatchViewModel? swatch)
    {
        // Guarded, because assigning the drawing colour comes straight back as a change
        // notification, and the panel answering that would re-derive the selection from the
        // colour — landing on the first swatch holding it rather than on the one just clicked.
        _isSynchronising = true;
        try
        {
            SetSelectedSwatch(swatch);

            if (swatch is not null)
            {
                _colors.Primary = swatch.Color;
            }
        }
        finally
        {
            _isSynchronising = false;
        }

        if (swatch is not null)
        {
            LoadEditor(swatch.Color);
        }
    }

    /// <summary>Exchanges the primary and secondary colours.</summary>
    [RelayCommand]
    private void SwapColors() => _colors.Swap();

    /// <summary>Loads a swatch's colour into the brush and the editor.</summary>
    /// <param name="swatch">The swatch that was clicked.</param>
    [RelayCommand]
    private void SelectSwatch(PaletteSwatchViewModel? swatch) => Select(swatch);

    /// <summary>
    /// Appends the current drawing colour to the palette and selects it there.
    /// </summary>
    /// <remarks>
    /// A colour already in the palette is selected rather than added a second time — two
    /// swatches of the same colour are two ways to reach one colour and one more thing to
    /// keep tidy.
    /// </remarks>
    [RelayCommand]
    private void AddCurrentToPalette()
    {
        if (Palette is not { } palette)
        {
            return;
        }

        int existing = palette.IndexOf(_colors.Primary);

        if (existing < 0)
        {
            palette.Add(_colors.Primary);
            existing = palette.Colors.Count - 1;
        }

        if (existing < Swatches.Count)
        {
            Select(Swatches[existing]);
        }
    }

    /// <summary>Takes the selected colour out of the palette.</summary>
    /// <remarks>
    /// The drawing colour is left exactly as it was. Removing a swatch says "I do not need
    /// this in the palette", not "stop drawing with this", and an artist mid-stroke would not
    /// thank the panel for deciding otherwise.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(IsEditingSwatch))]
    private void RemoveSelectedFromPalette()
    {
        if (Palette is { } palette && SelectedSwatch is { } swatch && swatch.Index < palette.Colors.Count)
        {
            palette.RemoveAt(swatch.Index);
        }
    }

    /// <summary>
    /// Carries a change made in the editor out to the palette slot and the drawing colour.
    /// </summary>
    private void OnEditorChanged(object? sender, PixelColor color)
    {
        if (_isSynchronising)
        {
            return;
        }

        _isSynchronising = true;
        try
        {
            _colors.Primary = color;

            if (Palette is { } palette && SelectedSwatch is { } swatch && swatch.Index < palette.Colors.Count)
            {
                palette.Replace(swatch.Index, color);
            }

            OnPropertyChanged(nameof(PrimaryBrush));
        }
        finally
        {
            _isSynchronising = false;
        }
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

    /// <summary>Keeps the swatches in step with the palette behind them.</summary>
    /// <remarks>
    /// A colour replaced in place updates the one swatch that holds it. Every other change
    /// moves positions around — and the position is the number printed on a swatch and the key
    /// that selects it — so the list is rebuilt, which is the only way to be sure the digits
    /// and the keyboard still agree.
    /// </remarks>
    private void OnPaletteColorsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.Action == NotifyCollectionChangedAction.Replace
            && e.NewStartingIndex >= 0
            && e.NewStartingIndex < Swatches.Count
            && Palette is { } palette)
        {
            Swatches[e.NewStartingIndex].Color = palette.Colors[e.NewStartingIndex];
            return;
        }

        RebuildSwatches();
    }

    /// <summary>
    /// Rebuilds every swatch rather than patching the one that changed.
    /// </summary>
    /// <remarks>
    /// A swatch carries its own position, so removing a colour renumbers everything after it
    /// — and the number is the key that selects it. Rebuilding is the only way to be sure the
    /// printed digits and the keyboard agree, and a palette has tens of entries, not
    /// thousands. The selection is re-derived afterwards from the drawing colour, so a
    /// rebuild does not silently leave the editor pointed at a slot that has moved.
    /// </remarks>
    private void RebuildSwatches()
    {
        Swatches.Clear();

        if (Palette is { } palette)
        {
            for (int i = 0; i < palette.Colors.Count; i++)
            {
                Swatches.Add(new PaletteSwatchViewModel(i, palette.Colors[i]));
            }
        }

        SetSelectedSwatch(FindSwatch(_colors.Primary));
    }

    /// <summary>The first swatch holding a colour, or <see langword="null"/> if none does.</summary>
    private PaletteSwatchViewModel? FindSwatch(PixelColor color) =>
        Swatches.FirstOrDefault(s => s.Color == color);

    /// <summary>Moves the selection outline, and tells the panel what it is now editing.</summary>
    private void SetSelectedSwatch(PaletteSwatchViewModel? swatch)
    {
        if (ReferenceEquals(SelectedSwatch, swatch))
        {
            return;
        }

        if (SelectedSwatch is { } previous)
        {
            previous.IsSelected = false;
        }

        SelectedSwatch = swatch;

        if (swatch is not null)
        {
            swatch.IsSelected = true;
        }

        OnPropertyChanged(nameof(IsEditingSwatch));
        OnPropertyChanged(nameof(EditorHeading));
        RemoveSelectedFromPaletteCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Shows a colour in the editor without echoing it straight back out again.</summary>
    private void LoadEditor(PixelColor color)
    {
        bool wasSynchronising = _isSynchronising;
        _isSynchronising = true;
        try
        {
            Editor.Load(color, EditorHeading, canSave: SelectedSwatch is not null);
        }
        finally
        {
            _isSynchronising = wasSynchronising;
        }

        OnPropertyChanged(nameof(PrimaryBrush));
    }

    /// <summary>Brings the panel into line with the drawing colour it was handed.</summary>
    private void PullFromModel()
    {
        SetSelectedSwatch(FindSwatch(_colors.Primary));
        LoadEditor(_colors.Primary);
        OnPropertyChanged(nameof(SecondaryBrush));
    }

    private void OnColorSlotsChanged(object? sender, PropertyChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (_isSynchronising)
        {
            return;
        }

        if (e.PropertyName == nameof(ColorSlots.Primary))
        {
            // The colour came from somewhere outside this panel — the eyedropper, a swap, an
            // undo — so the editor follows it, and the outline moves to the swatch that holds
            // it if the palette has one.
            PullFromModel();
        }
        else if (e.PropertyName == nameof(ColorSlots.Secondary))
        {
            OnPropertyChanged(nameof(SecondaryBrush));
        }
    }
}
