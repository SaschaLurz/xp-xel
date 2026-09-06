using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XPXel.Core.Primitives;

// Avalonia has an HsvColor of its own. The alias keeps XP-xel's unqualified here, since this
// file converts to and from PixelColor and never touches Avalonia's colour model.
using HsvColor = XPXel.Core.Primitives.HsvColor;

namespace XPXel.UI.ViewModels;

/// <summary>
/// One colour offered beside the colour being edited, with the reason it was offered.
/// </summary>
/// <param name="Suggestion">The colour and its description.</param>
public sealed record ColorSuggestionViewModel(ColorSuggestion Suggestion)
{
    /// <summary>The suggested colour.</summary>
    public PixelColor Color => Suggestion.Color;

    /// <summary>A brush showing it.</summary>
    public IBrush Brush => Suggestion.Color.ToBrush();

    /// <summary>The tooltip: what the suggestion is, and its hexadecimal value.</summary>
    public string Description => $"{Suggestion.Name} — {Suggestion.Color.ToHex()}";
}

/// <summary>
/// The colour editor shown when a swatch is right-clicked: the colour itself, sliders to
/// adjust it, and the colours that go with it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Adjustment happens in HSV, storage in RGB.</b> "A bit darker" is one slider here and
/// three coupled ones in the channel editor beside the palette; the two are complementary
/// rather than redundant, which is why both exist.
/// </para>
/// <para>
/// <b>The hue is remembered rather than re-read.</b> A grey has no hue — every hue produces
/// the same grey — so pulling saturation to zero and back would otherwise snap the colour to
/// red. Keeping the artist's hue as this view model's own state is what makes the three
/// sliders behave as three independent axes.
/// </para>
/// <para>
/// The editor never writes to the document or the colour slots on its own. It reports a
/// finished colour through <see cref="Applied"/> and <see cref="Saved"/>, and whoever opened
/// it decides what that means — which is what lets the same editor serve a palette slot and
/// the primary colour without knowing about either.
/// </para>
/// </remarks>
public sealed partial class ColorEditorViewModel : ViewModelBase
{
    private bool _isSynchronising;
    private PixelColor _color = PixelColor.Black;

    [ObservableProperty]
    private double _hue;

    [ObservableProperty]
    private double _saturation;

    [ObservableProperty]
    private double _value;

    [ObservableProperty]
    private double _alpha = 255;

    [ObservableProperty]
    private string _targetLabel = string.Empty;

    [ObservableProperty]
    private bool _canSave;

    /// <summary>Initialises the editor.</summary>
    public ColorEditorViewModel() => Load(PixelColor.Black, "Colour", canSave: false);

    /// <summary>Raised when the artist chooses to draw with the edited colour.</summary>
    public event EventHandler<PixelColor>? Applied;

    /// <summary>Raised when the artist chooses to write the edited colour back to its slot.</summary>
    public event EventHandler<PixelColor>? Saved;

    /// <summary>Raised when the artist chooses to remove the slot the editor was opened on.</summary>
    public event EventHandler? Removed;

    /// <summary>A shading ramp running through the current colour, darkest first.</summary>
    public ObservableCollection<ColorSuggestionViewModel> Ramp { get; } = [];

    /// <summary>The current colour's relations on the colour wheel.</summary>
    public ObservableCollection<ColorSuggestionViewModel> Harmonies { get; } = [];

    /// <summary>The colour as it currently stands.</summary>
    public PixelColor Color => _color;

    /// <summary>A brush showing the current colour.</summary>
    public IBrush PreviewBrush => _color.ToBrush();

    /// <summary>The current colour in hexadecimal, for the readout.</summary>
    public string HexText => _color.ToHex();

    /// <summary>Gets a value indicating whether any harmonies were worth offering.</summary>
    public bool HasHarmonies => Harmonies.Count > 0;

    /// <summary>Points the editor at a colour.</summary>
    /// <param name="color">The colour to edit.</param>
    /// <param name="label">What is being edited, for the heading — "Slot 3", say.</param>
    /// <param name="canSave">
    /// Whether the colour has a slot to be written back to. False when the editor was opened
    /// on something that has no home of its own, such as the primary colour.
    /// </param>
    public void Load(PixelColor color, string label, bool canSave)
    {
        _isSynchronising = true;
        try
        {
            _color = color;
            TargetLabel = label;
            CanSave = canSave;

            HsvColor hsv = HsvColor.FromColor(color);
            Hue = hsv.Hue;
            Saturation = hsv.Saturation * 100.0;
            Value = hsv.Value * 100.0;
            Alpha = color.A;
        }
        finally
        {
            _isSynchronising = false;
        }

        RefreshSuggestions();
        NotifyColorChanged();
    }

    /// <summary>Loads one of the suggested colours into the editor for further adjustment.</summary>
    /// <param name="suggestion">The suggestion the artist clicked.</param>
    /// <remarks>
    /// Clicking a suggestion edits rather than commits, so a ramp can be walked along —
    /// shadow, then its shadow — before anything is chosen.
    /// </remarks>
    [RelayCommand]
    private void PickSuggestion(ColorSuggestionViewModel? suggestion)
    {
        if (suggestion is not null)
        {
            Load(suggestion.Color, TargetLabel, CanSave);
        }
    }

    /// <summary>Draws with the edited colour without changing the slot it came from.</summary>
    [RelayCommand]
    private void Apply() => Applied?.Invoke(this, _color);

    /// <summary>Writes the edited colour back into the slot it was opened on.</summary>
    [RelayCommand]
    private void Save() => Saved?.Invoke(this, _color);

    /// <summary>Removes the slot the editor was opened on.</summary>
    [RelayCommand]
    private void Remove() => Removed?.Invoke(this, EventArgs.Empty);

    partial void OnHueChanged(double value) => ApplyFromSliders();

    partial void OnSaturationChanged(double value) => ApplyFromSliders();

    partial void OnValueChanged(double value) => ApplyFromSliders();

    partial void OnAlphaChanged(double value) => ApplyFromSliders();

    private void ApplyFromSliders()
    {
        if (_isSynchronising)
        {
            return;
        }

        _color = new HsvColor(Hue, Saturation / 100.0, Value / 100.0)
            .ToColor((byte)Math.Clamp(Alpha, 0, 255));

        RefreshSuggestions();
        NotifyColorChanged();
    }

    private void RefreshSuggestions()
    {
        Ramp.Clear();
        foreach (ColorSuggestion suggestion in ColorHarmony.GetShadingRamp(_color))
        {
            Ramp.Add(new ColorSuggestionViewModel(suggestion));
        }

        Harmonies.Clear();
        foreach (ColorSuggestion suggestion in ColorHarmony.GetHarmonies(_color))
        {
            Harmonies.Add(new ColorSuggestionViewModel(suggestion));
        }

        OnPropertyChanged(nameof(HasHarmonies));
    }

    private void NotifyColorChanged()
    {
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(PreviewBrush));
        OnPropertyChanged(nameof(HexText));
    }
}
