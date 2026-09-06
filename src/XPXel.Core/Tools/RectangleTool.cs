using XPXel.Core.Drawing;
using XPXel.Core.Primitives;

namespace XPXel.Core.Tools;

/// <summary>Draws a rectangle between the two corners of a drag.</summary>
/// <remarks>
/// Outlined or filled according to the shape option in the tool options bar. Holding Shift
/// constrains it to a square about the corner the drag started from.
/// </remarks>
public sealed class RectangleTool : ShapeToolBase
{
    /// <summary>The stable identifier of this tool.</summary>
    public const string ToolId = "xpxel.tool.rectangle";

    /// <inheritdoc />
    public override string Id => ToolId;

    /// <inheritdoc />
    public override string DisplayName => "Rectangle";

    /// <inheritdoc />
    public override string Description => "Drag to draw a rectangle. Hold Shift for a square.";

    /// <inheritdoc />
    protected override void PlotShape(ImagePoint anchor, ImagePoint current, bool isFilled, Action<int, int> plot) =>
        ShapeRasterizer.PlotRectangle(ImageRect.FromPoints(anchor, current), isFilled, plot);
}
