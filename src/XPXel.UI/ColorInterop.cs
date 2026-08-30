using Avalonia.Media;
using XPXel.Core.Primitives;

namespace XPXel.UI;

/// <summary>
/// Converts between the editor's <see cref="PixelColor"/> and Avalonia's
/// <see cref="Color"/>.
/// </summary>
/// <remarks>
/// The two types hold identical information; they are separate because
/// <c>XPXel.Core</c> may not reference a UI framework. This is the single boundary where
/// the two meet, so if the representation ever changes there is exactly one place to update.
/// Both use straight, non-premultiplied alpha, so the conversion is a plain field copy.
/// </remarks>
public static class ColorInterop
{
    /// <summary>Converts an editor colour to an Avalonia colour.</summary>
    /// <param name="color">The colour to convert.</param>
    /// <returns>The equivalent Avalonia colour.</returns>
    public static Color ToAvalonia(this PixelColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    /// <summary>Converts an Avalonia colour to an editor colour.</summary>
    /// <param name="color">The colour to convert.</param>
    /// <returns>The equivalent editor colour.</returns>
    public static PixelColor ToPixelColor(this Color color) => new(color.R, color.G, color.B, color.A);

    /// <summary>Creates a solid brush for an editor colour.</summary>
    /// <param name="color">The colour to convert.</param>
    /// <returns>A brush painting that colour.</returns>
    public static IBrush ToBrush(this PixelColor color) => new SolidColorBrush(color.ToAvalonia());
}
