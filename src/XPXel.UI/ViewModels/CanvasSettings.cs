using CommunityToolkit.Mvvm.ComponentModel;
using XPXel.Core.Animation;

namespace XPXel.UI.ViewModels;

/// <summary>
/// Canvas view options that are shared by every open document.
/// </summary>
/// <remarks>
/// This exists for the same reason <see cref="XPXel.Core.Editing.ToolSettings"/> does: some
/// state belongs to the application rather than to one image. An artist who turns the pixel
/// grid on expects it to stay on when they switch to another tab, exactly as they expect to
/// keep the brush size they just chose.
///
/// A single shared instance is registered in <c>AppServices</c> and handed to every
/// document, so there is nothing to synchronise.
/// </remarks>
public sealed partial class CanvasSettings : ObservableObject
{
    /// <summary>Initialises the shared view options.</summary>
    /// <param name="onionSkin">
    /// The onion skin settings, which are edited from the timeline and read by every canvas.
    /// </param>
    public CanvasSettings(OnionSkinSettings onionSkin)
    {
        ArgumentNullException.ThrowIfNull(onionSkin);
        OnionSkin = onionSkin;
    }

    /// <summary>Whether the pixel grid is drawn once the zoom is high enough to warrant it.</summary>
    [ObservableProperty]
    private bool _showPixelGrid = true;

    /// <summary>
    /// Whether the numbered rulers are shown along the top and left edges of the canvas.
    /// </summary>
    /// <remarks>
    /// On by default: knowing which pixel you are on is what the rulers are for, and an
    /// artist who wants the extra forty pixels of canvas can say so. They are a view option
    /// rather than a per-document one for the same reason the pixel grid is — it is a way of
    /// working, not a property of the picture.
    /// </remarks>
    [ObservableProperty]
    private bool _showRulers = true;

    /// <summary>
    /// Whether a dot of the drawing colour follows the pointer on the canvas.
    /// </summary>
    /// <remarks>
    /// On by default, because the colour a stroke is about to lay down is the one thing the
    /// canvas cannot otherwise tell you without a look across at the colour panel. It is
    /// deliberately small and offset away from the pointer so it never covers the pixel being
    /// aimed at — but a marker that follows the cursor is exactly the kind of thing some
    /// people cannot stand, so it can be switched off.
    /// </remarks>
    [ObservableProperty]
    private bool _showColorSwatchCursor = true;

    /// <summary>
    /// How the neighbouring frames are ghosted under the current one.
    /// </summary>
    /// <remarks>
    /// Reached through here rather than bound to directly so that a document tab needs one
    /// object for every shared view option, which is what keeps the canvas's bindings the
    /// same shape whether the option is a checkbox or a settings object.
    /// </remarks>
    public OnionSkinSettings OnionSkin { get; }
}
