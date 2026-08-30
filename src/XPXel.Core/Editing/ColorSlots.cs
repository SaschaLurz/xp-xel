using XPXel.Core.ComponentModel;
using XPXel.Core.Primitives;

namespace XPXel.Core.Editing;

/// <summary>
/// The primary and secondary drawing colours.
/// </summary>
/// <remarks>
/// <para>
/// Every raster editor since Deluxe Paint has bound two colours to the two mouse buttons,
/// and pixel artists rely on it heavily: left button lays down the primary colour, right
/// button the secondary, so a two-tone shape can be drawn without moving to the palette.
/// Tools receive both through <c>ToolContext</c> and pick by button.
/// </para>
/// <para>
/// The colours are application state rather than document state — switching document tabs
/// should not change which colour is loaded — so this lives outside
/// <see cref="DocumentWorkspace"/>.
/// </para>
/// </remarks>
public sealed class ColorSlots : ObservableModel
{
    private PixelColor _primary = PixelColor.Black;
    private PixelColor _secondary = PixelColor.White;

    /// <summary>The colour applied by the left mouse button.</summary>
    public PixelColor Primary
    {
        get => _primary;
        set => SetProperty(ref _primary, value);
    }

    /// <summary>The colour applied by the right mouse button.</summary>
    public PixelColor Secondary
    {
        get => _secondary;
        set => SetProperty(ref _secondary, value);
    }

    /// <summary>Exchanges the two colours, the action conventionally bound to the X key.</summary>
    public void Swap() => (Primary, Secondary) = (Secondary, Primary);
}
