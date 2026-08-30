using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XPXel.Core.Primitives;

namespace XPXel.UI.ViewModels.Dialogs;

/// <summary>
/// Asks for the canvas size of a new document.
/// </summary>
/// <remarks>
/// The preset sizes are the ones sprite work actually starts from: 16 and 32 for characters
/// and tiles, 64 and 128 for larger sprites and portraits. Offering them saves typing and,
/// more usefully, nudges newcomers towards sizes that suit the medium — a first-time user
/// who starts at 1920x1080 is not going to end up making pixel art.
/// </remarks>
public sealed partial class NewDocumentDialogViewModel : DialogViewModel
{
    /// <summary>The largest canvas dimension the dialog accepts.</summary>
    public const int MaximumDimension = 4096;

    [ObservableProperty]
    private int _width = 32;

    [ObservableProperty]
    private int _height = 32;

    /// <summary>Initialises the dialog.</summary>
    public NewDocumentDialogViewModel()
        : base("New Document")
    {
    }

    /// <summary>The canvas sizes offered as one-click presets.</summary>
    public static IReadOnlyList<int> Presets { get; } = [8, 16, 32, 48, 64, 128, 256];

    /// <summary>Sets both dimensions from a preset.</summary>
    /// <param name="size">The square size to apply.</param>
    [RelayCommand]
    private void ApplyPreset(int size)
    {
        Width = size;
        Height = size;
    }

    /// <summary>Closes the dialog, returning the chosen size.</summary>
    [RelayCommand]
    private void Create() => Close(new ImageSize(
        Math.Clamp(Width, 1, MaximumDimension),
        Math.Clamp(Height, 1, MaximumDimension)));
}
