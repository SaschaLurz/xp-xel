using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using XPXel.Core.Primitives;

namespace XPXel.UI.Converters;

/// <summary>
/// Turns a <see cref="PixelColor"/> into a brush so palette swatches can bind straight to
/// the model's colours.
/// </summary>
/// <remarks>
/// Without this the palette would need a wrapper view model per swatch whose only job is to
/// hold a brush. A converter keeps the palette a plain list of colours, which is what it
/// actually is, and means adding or removing a colour needs no view-model bookkeeping.
/// </remarks>
public sealed class PixelColorToBrushConverter : IValueConverter
{
    /// <summary>A shared instance, referenced from XAML.</summary>
    public static PixelColorToBrushConverter Instance { get; } = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is PixelColor color ? new SolidColorBrush(color.ToAvalonia()) : Brushes.Transparent;

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is SolidColorBrush brush ? brush.Color.ToPixelColor() : PixelColor.Transparent;
}
