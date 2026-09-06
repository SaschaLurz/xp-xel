using XPXel.Core.History;
using XPXel.Core.Primitives;

namespace XPXel.Core.Tools;

/// <summary>
/// Clears pixels back to fully transparent along the pointer's path.
/// </summary>
/// <remarks>
/// <para>
/// Erasing is the same operation as drawing, with transparent as the colour — which is why
/// this tool is almost empty. It inherits brush size, brush shape, stroke interpolation and
/// undo behaviour from <see cref="StrokeToolBase"/> and therefore always stays consistent
/// with the brush.
/// </para>
/// <para>
/// <b>The one thing it cannot inherit is soft erasing.</b> The base class fades a colour in
/// by compositing it at partial alpha, but "transparent at 40% alpha" composites to no change
/// at all — a soft eraser built that way would simply do nothing at its rim. Erasing softly
/// means taking alpha <em>away</em>, so the override below scales the pixel's own alpha down
/// instead.
/// </para>
/// </remarks>
public sealed class EraserTool : StrokeToolBase
{
    /// <summary>The stable identifier of this tool.</summary>
    public const string ToolId = "xpxel.tool.eraser";

    /// <inheritdoc />
    public override string Id => ToolId;

    /// <inheritdoc />
    public override string DisplayName => "Eraser";

    /// <inheritdoc />
    public override string Description => "Clear pixels back to transparent.";

    /// <inheritdoc />
    protected override bool SupportsBrushDynamics => true;

    /// <inheritdoc />
    protected override PixelColor ResolveStrokeColor(ToolContext context, ToolPointerButton button) =>
        PixelColor.Transparent;

    /// <inheritdoc />
    protected override void ApplyStrokePixel(
        RasterEditSession session,
        int x,
        int y,
        PixelColor color,
        double coverage)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (coverage >= 1.0)
        {
            session.SetPixel(x, y, PixelColor.Transparent);
            return;
        }

        // Against the pre-stroke value, so overlapping stamps do not eat further into the
        // pixel each time the pointer passes over it.
        PixelColor original = session.GetOriginalPixel(x, y);
        session.SetPixel(x, y, original.WithAlpha((byte)Math.Round(original.A * (1.0 - coverage))));
    }
}
