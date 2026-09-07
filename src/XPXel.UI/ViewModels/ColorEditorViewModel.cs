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
/// The colour editor beneath the palette: the colour being edited, a spectrum and sliders to
/// adjust it, and the colours that go with it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Adjustment happens in HSV, storage in RGB.</b> "A bit darker" is one slider here and
/// three coupled ones in a red-green-blue editor, which is why the panel offers these and a
/// hexadecimal field rather than a row of channels: hue, saturation and value are the axes an
/// artist actually reaches for, and hex is the exact value when an exact value is the point.
/// </para>
/// <para>
/// <b>The hue is remembered rather than re-read.</b> A grey has no hue — every hue produces
/// the same grey — so pulling saturation to zero and back would otherwise snap the colour to
/// red. Keeping the artist's hue as this view model's own state is what makes the three
/// sliders behave as three independent axes.
/// </para>
/// <para>
/// The editor never writes to the document or the colour slots on its own. Every change the
/// artist makes is announced through <see cref="Changed"/>, and whoever is listening decides
/// what that means — which is what lets the same editor serve a palette slot and the plain
/// drawing colour without knowing about either.
/// </para>
/// <para>
/// <b>There is no "apply" step, and that is the point.</b> The editor sits under the palette
/// and edits whichever swatch is selected, live: a slider moved here changes the swatch and the
/// drawing colour as it moves. A button to confirm would put a decision between the artist and
/// a result they can already see, and the way back from a wrong colour is the same as the way
/// there — keep moving the slider.
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

    /// <summary>
    /// Raised whenever the artist changes the colour, as they change it.
    /// </summary>
    /// <remarks>
    /// Not raised by <see cref="Load"/>, which is the other direction: that is the owner
    /// telling the editor what to show, and echoing it straight back would be a loop.
    /// </remarks>
    public event EventHandler<PixelColor>? Changed;

    /// <summary>A shading ramp running through the current colour, darkest first.</summary>
    public ObservableCollection<ColorSuggestionViewModel> Ramp { get; } = [];

    /// <summary>The current colour's relations on the colour wheel.</summary>
    public ObservableCollection<ColorSuggestionViewModel> Harmonies { get; } = [];

    /// <summary>The colour as it currently stands.</summary>
    public PixelColor Color => _color;

    /// <summary>A brush showing the current colour.</summary>
    public IBrush PreviewBrush => _color.ToBrush();

    /// <summary>
    /// The current colour in hexadecimal, readable and writable.
    /// </summary>
    /// <remarks>
    /// Writable because hexadecimal is how palettes are shared: an artist working from a
    /// reference or a published palette has the number, not a position on a wheel. Anything
    /// that does not parse is ignored rather than rejected, so a half-typed value on the way to
    /// a valid one does not fight the person typing it.
    /// </remarks>
    public string HexText
    {
        get => _color.ToHex();
        set
        {
            if (PixelColor.TryParseHex(value, out PixelColor parsed) && parsed != _color)
            {
                Load(parsed, TargetLabel, CanSave);
                Changed?.Invoke(this, _color);
            }
        }
    }

    /// <summary>
    /// The saturation as a fraction, which is what the spectrum control works in.
    /// </summary>
    /// <remarks>
    /// The sliders show percentages because that is what an artist reads, and the spectrum
    /// works in fractions because that is what the colour model uses. Rather than pick one and
    /// convert at every call site, the two views of the same number live side by side here.
    /// </remarks>
    public double SaturationFraction
    {
        get => Saturation / 100.0;
        set => Saturation = Math.Clamp(value, 0.0, 1.0) * 100.0;
    }

    /// <summary>The value as a fraction, for the spectrum control.</summary>
    public double ValueFraction
    {
        get => Value / 100.0;
        set => Value = Math.Clamp(value, 0.0, 1.0) * 100.0;
    }

    /// <summary>Gets a value indicating whether any harmonies were worth offering.</summary>
    public bool HasHarmonies => Harmonies.Count > 0;

    /// <summary>Points the editor at a colour.</summary>
    /// <param name="color">The colour to edit.</param>
    /// <param name="label">What is being edited, for the heading — "Slot 3", say.</param>
    /// <param name="canSave">
    /// Whether the colour has a palette slot behind it. False when the editor is showing a
    /// drawing colour that is not in the palette — picked off the canvas, say — in which case
    /// the panel offers to add it rather than pretending it is already there.
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

    /// <summary>Takes one of the suggested colours as the colour being edited.</summary>
    /// <param name="suggestion">The suggestion the artist clicked.</param>
    /// <remarks>
    /// The ramp and the harmonies recompute around the new colour, so a ramp can still be
    /// walked along — a shadow, then its shadow — with each step going straight to the slot
    /// and the brush the way every other change here does.
    /// </remarks>
    [RelayCommand]
    private void PickSuggestion(ColorSuggestionViewModel? suggestion)
    {
        if (suggestion is not null)
        {
            Load(suggestion.Color, TargetLabel, CanSave);
            Changed?.Invoke(this, _color);
        }
    }

    partial void OnHueChanged(double value) => ApplyFromSliders();

    partial void OnSaturationChanged(double value)
    {
        OnPropertyChanged(nameof(SaturationFraction));
        ApplyFromSliders();
    }

    partial void OnValueChanged(double value)
    {
        OnPropertyChanged(nameof(ValueFraction));
        ApplyFromSliders();
    }

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
        Changed?.Invoke(this, _color);
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
        OnPropertyChanged(nameof(SaturationFraction));
        OnPropertyChanged(nameof(ValueFraction));
    }
}
