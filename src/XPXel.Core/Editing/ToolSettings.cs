using XPXel.Core.ComponentModel;
using XPXel.Core.Drawing;
using XPXel.Core.Text;

namespace XPXel.Core.Editing;

/// <summary>
/// The adjustable options shared by the drawing tools, shown in the tool options bar.
/// </summary>
/// <remarks>
/// <para>
/// Settings are kept in one place rather than as fields on each tool so that they can be
/// bound to the UI, persisted between sessions, and shared where that is what the artist
/// expects — brush size, for instance, should not reset when switching from pencil to
/// eraser.
/// </para>
/// <para>
/// <b>Growth path.</b> When tools need genuinely private options (a gradient's shape, a
/// shape tool's fill mode), give each tool its own settings object retrieved by tool id
/// rather than widening this class indefinitely.
/// </para>
/// </remarks>
public sealed class ToolSettings : ObservableModel
{
    private int _brushSize = 1;
    private BrushShape _brushShape = BrushShape.Square;
    private int _brushSoftness;
    private int _fillTolerance;
    private bool _isFillContiguous = true;
    private bool _isShapeFilled;

    /// <summary>
    /// The width of the brush in pixels, at least 1. A size of 1 — a single pixel — is the
    /// default because it is what pixel art is actually drawn with.
    /// </summary>
    public int BrushSize
    {
        get => _brushSize;
        set => SetProperty(ref _brushSize, Math.Clamp(value, 1, 128));
    }

    /// <summary>The shape stamped at each point along a stroke.</summary>
    public BrushShape BrushShape
    {
        get => _brushShape;
        set => SetProperty(ref _brushShape, value);
    }

    /// <summary>
    /// How far in from the rim a brush fades, as a percentage. Zero — a hard edge — is the
    /// default, because that is the only setting that produces the exact colours pixel art
    /// is made of.
    /// </summary>
    /// <remarks>
    /// Honoured by tools that opt into it. The pencil deliberately ignores it: a pencil that
    /// laid down half-transparent pixels would stop being a pencil.
    /// </remarks>
    public int BrushSoftness
    {
        get => _brushSoftness;
        set => SetProperty(ref _brushSoftness, Math.Clamp(value, 0, 100));
    }

    /// <summary>
    /// How far a pixel's colour may differ from the clicked colour and still be filled,
    /// 0 to 255. Zero — an exact match — is the default, since pixel art normally uses flat,
    /// exact colours.
    /// </summary>
    public int FillTolerance
    {
        get => _fillTolerance;
        set => SetProperty(ref _fillTolerance, Math.Clamp(value, 0, 255));
    }

    /// <summary>
    /// Whether the bucket fills only the connected region under the cursor
    /// (<see langword="true"/>) or every matching pixel on the layer
    /// (<see langword="false"/>).
    /// </summary>
    public bool IsFillContiguous
    {
        get => _isFillContiguous;
        set => SetProperty(ref _isFillContiguous, value);
    }

    /// <summary>
    /// Whether the rectangle and ellipse tools fill their shape or draw a one-pixel outline.
    /// Outline is the default, since a filled shape is an outline plus a bucket fill but not
    /// the other way round.
    /// </summary>
    public bool IsShapeFilled
    {
        get => _isShapeFilled;
        set => SetProperty(ref _isShapeFilled, value);
    }

    /// <summary>
    /// The typography a newly created text layer starts with.
    /// </summary>
    /// <remarks>
    /// These are defaults, not live document state. Once a text layer exists it owns its own
    /// <see cref="TextStyle"/>, and the text panel edits that instead — which is what lets
    /// two text layers in one document use different fonts.
    /// </remarks>
    public TextStyle TextDefaults { get; } = new();
}
