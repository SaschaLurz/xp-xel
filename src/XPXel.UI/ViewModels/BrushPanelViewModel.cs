using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XPXel.Core.Drawing;
using XPXel.Core.Editing;
using XPXel.UI.Rendering;

namespace XPXel.UI.ViewModels;

/// <summary>
/// One brush size offered as a single-click button.
/// </summary>
/// <param name="Size">The size in pixels.</param>
/// <param name="Apply">Applies it.</param>
/// <remarks>
/// Each preset carries its own command rather than the buttons reaching up through the visual
/// tree for a shared one with the size as a parameter. That indirection is a common XAML
/// pattern and it is also a common source of a button that silently does nothing, because a
/// mistyped ancestor lookup or a parameter that fails to convert both fail quietly at run time.
/// A closure per preset cannot fail that way.
/// </remarks>
public sealed record BrushSizePreset(int Size, ICommand Apply);

/// <summary>
/// The brush panel: size, shape, edge softness and stroke stabilisation, with a live preview
/// of the tip those four settings produce.
/// </summary>
/// <remarks>
/// <para>
/// <b>This panel edits application state, not document state.</b> The settings live in
/// <see cref="ToolSettings"/>, which is a singleton, so a brush chosen while working on one
/// image is still the brush after switching tabs — an artist picks a brush once.
/// </para>
/// <para>
/// <b>Softness and stabilisation do different jobs</b>, which is why they are two sliders
/// rather than one "smoothness". Softness fades the edge of a single stamp; stabilisation
/// smooths the path the stamps are placed along. The panel keeps them apart and says so, so
/// that an artist reaching for one does not get the other.
/// </para>
/// <para>
/// Only the brush and the eraser honour these; the pencil ignores them by design. The panel
/// says that rather than hiding the sliders when the pencil is selected, because a control
/// that disappears is harder to understand than one that explains itself.
/// </para>
/// </remarks>
public sealed partial class BrushPanelViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// The sizes offered as one-click buttons: powers of two plus the odd sizes in between,
    /// which are the ones that stamp symmetrically around a pixel and so are what an artist
    /// reaches for most.
    /// </summary>
    private static readonly int[] PresetSizes = [1, 2, 3, 5, 8, 12, 16, 24, 32, 48, 64];

    private readonly ColorSlots _colors;

    [ObservableProperty]
    private Bitmap? _preview;

    /// <summary>Initialises the panel.</summary>
    /// <param name="settings">The shared tool options this panel edits.</param>
    /// <param name="colors">The drawing colours, so the preview shows the real one.</param>
    public BrushPanelViewModel(ToolSettings settings, ColorSlots colors, ToolboxViewModel toolbox)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(colors);
        ArgumentNullException.ThrowIfNull(toolbox);

        Settings = settings;
        _colors = colors;
        Toolbox = toolbox;

        SizePresets =
        [
            .. PresetSizes.Select(size => new BrushSizePreset(size, new RelayCommand(() => Settings.BrushSize = size))),
        ];

        Settings.PropertyChanged += OnSettingsChanged;
        _colors.PropertyChanged += OnColorsChanged;

        UpdatePreview();
    }

    /// <summary>The settings this panel edits, bound to directly by the view.</summary>
    public ToolSettings Settings { get; }

    /// <summary>
    /// The toolbox, so the panel can hide the settings the active tool does not read.
    /// </summary>
    /// <remarks>
    /// A slider a tool ignores is worse than no slider: the artist drags it, nothing happens,
    /// and the tool looks broken. The pencil takes a size and nothing else, so with the pencil
    /// selected that is all this panel offers.
    /// </remarks>
    public ToolboxViewModel Toolbox { get; }

    /// <summary>The brush shapes offered by the shape selector.</summary>
    public static IReadOnlyList<BrushShape> Shapes { get; } = Enum.GetValues<BrushShape>();

    /// <summary>The sizes offered as one-click buttons beneath the size slider.</summary>
    public IReadOnlyList<BrushSizePreset> SizePresets { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        Settings.PropertyChanged -= OnSettingsChanged;
        _colors.PropertyChanged -= OnColorsChanged;
    }

    /// <summary>Returns every setting on this panel to its default.</summary>
    [RelayCommand]
    private void Reset()
    {
        Settings.BrushSize = 1;
        Settings.BrushShape = BrushShape.Square;
        Settings.BrushSoftness = 0;
        Settings.BrushStabilization = 0;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ToolSettings.BrushSize)
            or nameof(ToolSettings.BrushShape)
            or nameof(ToolSettings.BrushSoftness))
        {
            UpdatePreview();
        }
    }

    private void OnColorsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ColorSlots.Primary))
        {
            UpdatePreview();
        }
    }

    private void UpdatePreview()
    {
        Bitmap? previous = Preview;

        Preview = BrushPreviewRenderer.Render(
            Settings.BrushSize,
            Settings.BrushShape,
            Settings.BrushSoftness,
            _colors.Primary);

        // The old bitmap is a GPU-backed resource. Dragging the size slider produces one per
        // step, and waiting for finalisation to release them would let a few hundred pile up
        // during a single drag.
        previous?.Dispose();
    }
}
