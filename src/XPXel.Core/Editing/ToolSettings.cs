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
/// bound to the UI and persisted between sessions, and so that the options bar has one object
/// to bind to whichever tool is in hand.
/// </para>
/// <para>
/// <b>Size is per tool; everything else is shared.</b> One number for every tool meant that
/// setting the eraser to twenty made the pen-xel twenty as well, and an artist who rubs out a
/// patch and goes back to drawing wants their one-pixel pen back, not a twenty-pixel one. Size
/// is the setting that differs by tool in every editor of this kind, so it is remembered per
/// tool here — <see cref="ActiveToolId"/> says whose. Shape, softness, tolerance and the rest
/// stay shared, because there the artist is describing how they want to work rather than what
/// each tool is for.
/// </para>
/// <para>
/// <b>Growth path.</b> When tools need genuinely private options (a gradient's shape, a
/// shape tool's fill mode), follow the same pattern <see cref="BrushSize"/> uses rather than
/// widening this class indefinitely.
/// </para>
/// </remarks>
public sealed class ToolSettings : ObservableModel
{
    /// <summary>The size each tool was last set to, keyed by tool id.</summary>
    /// <remarks>
    /// Sparse: a tool that has never been adjusted has no entry and reports
    /// <see cref="DefaultBrushSize"/>, which saves seeding the table from a list of tool ids
    /// that this class has no business knowing.
    /// </remarks>
    private readonly Dictionary<string, int> _brushSizes = new(StringComparer.Ordinal);

    private string _activeToolId = string.Empty;
    private BrushShape _brushShape = BrushShape.Square;
    private int _brushSoftness;
    private int _fillTolerance;
    private bool _isFillContiguous = true;
    private bool _isShapeFilled;

    /// <summary>
    /// A single pixel: what pixel art is actually drawn with, and what every tool starts at.
    /// </summary>
    public const int DefaultBrushSize = 1;

    /// <summary>
    /// Which tool the size settings currently describe.
    /// </summary>
    /// <remarks>
    /// Set by the toolbox as the artist changes tool. Everything that shows or edits a size —
    /// the options bar, the right-click panel, the brush preview, Settings — binds to
    /// <see cref="BrushSize"/> and needs to know nothing about this; changing it simply makes
    /// that property report a different number.
    /// </remarks>
    public string ActiveToolId
    {
        get => _activeToolId;
        set
        {
            string id = value ?? string.Empty;

            if (SetProperty(ref _activeToolId, id))
            {
                OnPropertyChanged(nameof(BrushSize));
            }
        }
    }

    /// <summary>
    /// The width of the tool in hand, in pixels, at least 1. Each tool remembers its own.
    /// </summary>
    public int BrushSize
    {
        get => _brushSizes.GetValueOrDefault(_activeToolId, DefaultBrushSize);
        set
        {
            int clamped = Math.Clamp(value, 1, 128);

            if (BrushSize == clamped)
            {
                return;
            }

            _brushSizes[_activeToolId] = clamped;
            OnPropertyChanged(nameof(BrushSize));
        }
    }

    /// <summary>Returns the size a particular tool is set to.</summary>
    /// <param name="toolId">The tool's stable identifier.</param>
    /// <returns>Its size, or <see cref="DefaultBrushSize"/> if it has never been adjusted.</returns>
    public int GetBrushSize(string toolId) =>
        _brushSizes.GetValueOrDefault(toolId ?? string.Empty, DefaultBrushSize);

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
