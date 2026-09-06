using XPXel.Core.Drawing;
using XPXel.Core.Primitives;

namespace XPXel.Core.Tools;

/// <summary>Draws a straight line between the two ends of a drag.</summary>
/// <remarks>
/// The same Bresenham rasteriser that joins consecutive pointer samples in a freehand stroke
/// draws the line here, so a deliberate line and a fast drag produce the same kind of pixels.
/// Holding Shift snaps the far end to the nearest 45° step, which is how an exactly
/// horizontal, vertical or diagonal edge gets drawn.
/// </remarks>
public sealed class LineTool : ShapeToolBase
{
    /// <summary>The stable identifier of this tool.</summary>
    public const string ToolId = "xpxel.tool.line";

    /// <inheritdoc />
    public override string Id => ToolId;

    /// <inheritdoc />
    public override string DisplayName => "Line";

    /// <inheritdoc />
    public override string Description => "Drag to draw a straight line. Hold Shift to snap to 45°.";

    /// <inheritdoc />
    protected override bool SupportsFill => false;

    /// <inheritdoc />
    protected override ImagePoint Constrain(ImagePoint anchor, ImagePoint current) =>
        ShapeRasterizer.ConstrainToAxis(anchor, current);

    /// <inheritdoc />
    protected override void PlotShape(ImagePoint anchor, ImagePoint current, bool isFilled, Action<int, int> plot) =>
        LineRasterizer.PlotLine(anchor, current, plot);
}
