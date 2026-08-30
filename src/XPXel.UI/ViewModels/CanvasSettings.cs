using CommunityToolkit.Mvvm.ComponentModel;

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
    /// <summary>Whether the pixel grid is drawn once the zoom is high enough to warrant it.</summary>
    [ObservableProperty]
    private bool _showPixelGrid = true;
}
