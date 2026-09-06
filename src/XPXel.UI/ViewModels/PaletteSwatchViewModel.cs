using Avalonia.Media;
using XPXel.Core.Documents;
using XPXel.Core.Primitives;

namespace XPXel.UI.ViewModels;

/// <summary>
/// One swatch in the colour panel's palette.
/// </summary>
/// <remarks>
/// <para>
/// The palette is a plain list of colours in the model, so the number printed on a swatch —
/// the key that selects it — has to come from somewhere. A view model per swatch is that
/// somewhere: it carries the position, which a <c>DataTemplate</c> over a list of colours
/// cannot see.
/// </para>
/// <para>
/// Swatches are immutable and rebuilt when the palette changes, rather than tracking their
/// colour. A palette has a couple of dozen entries and changes when a person clicks
/// something, so the rebuild is imperceptible and it removes any chance of a swatch showing
/// a colour the palette no longer holds.
/// </para>
/// </remarks>
public sealed class PaletteSwatchViewModel
{
    /// <summary>Initialises the swatch.</summary>
    /// <param name="index">The colour's position in the palette, counting from zero.</param>
    /// <param name="color">The colour.</param>
    public PaletteSwatchViewModel(int index, PixelColor color)
    {
        Index = index;
        Color = color;
    }

    /// <summary>The colour's position in the palette, counting from zero.</summary>
    public int Index { get; }

    /// <summary>The colour this swatch holds.</summary>
    public PixelColor Color { get; }

    /// <summary>A brush showing the colour.</summary>
    public IBrush Brush => Color.ToBrush();

    /// <summary>
    /// The key that selects this swatch, or an empty string past the tenth.
    /// </summary>
    /// <remarks>
    /// The first nine answer to <c>1</c>–<c>9</c> and the tenth to <c>0</c>, which is where
    /// the digit row runs out and why the hotbar is ten long.
    /// </remarks>
    public string HotkeyLabel => Index switch
    {
        < 0 => string.Empty,
        < Palette.HotbarSize - 1 => (Index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
        Palette.HotbarSize - 1 => "0",
        _ => string.Empty,
    };

    /// <summary>Gets a value indicating whether a number key reaches this swatch.</summary>
    public bool HasHotkey => HotkeyLabel.Length > 0;

    /// <summary>The tooltip: the colour, and the key that picks it if it has one.</summary>
    public string Description => HasHotkey
        ? $"{Color.ToHex()}  ({HotkeyLabel})  —  right-click to edit"
        : $"{Color.ToHex()}  —  right-click to edit";

    /// <summary>
    /// A colour that reads against this swatch, for the number printed on it.
    /// </summary>
    /// <remarks>
    /// A fixed label colour is invisible on half a rainbow. Choosing black or white by the
    /// swatch's perceived brightness — the Rec. 601 luma weights, which approximate how much
    /// each channel contributes to how bright a colour looks — keeps every number legible,
    /// including on the black and white swatches at the start of the row.
    /// </remarks>
    public IBrush LabelBrush =>
        (0.299 * Color.R) + (0.587 * Color.G) + (0.114 * Color.B) > 140 || Color.A < 96
            ? Brushes.Black
            : Brushes.White;
}
