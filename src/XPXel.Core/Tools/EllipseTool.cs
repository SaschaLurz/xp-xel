using XPXel.Core.Drawing;
using XPXel.Core.Primitives;

namespace XPXel.Core.Tools;

/// <summary>Draws an ellipse inscribed in the rectangle spanned by a drag.</summary>
/// <remarks>
/// Outlined or filled according to the shape option in the tool options bar. Holding Shift
/// constrains it to a circle, which at small sizes is the only reliable way to get one: a
/// hand-dragged "circle" that is one pixel wider than it is tall does not look round.
/// </remarks>
public sealed class EllipseTool : ShapeToolBase
{
    /// <summary>The stable identifier of this tool.</summary>
    public const string ToolId = "xpxel.tool.ellipse";

    /// <inheritdoc />
    public override string Id => ToolId;

    /// <inheritdoc />
    public override string DisplayName => "Ellipse";

    /// <inheritdoc />
    public override string Description => "Drag to draw an ellipse. Hold Shift for a circle.";

    /// <inheritdoc />
    protected override void PlotShape(ImagePoint anchor, ImagePoint current, bool isFilled, Action<int, int> plot) =>
        ShapeRasterizer.PlotEllipse(ImageRect.FromPoints(anchor, current), isFilled, plot);
}
