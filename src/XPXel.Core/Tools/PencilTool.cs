using XPXel.Core.Drawing;
using XPXel.Core.Primitives;

namespace XPXel.Core.Tools;

/// <summary>
/// Paints the drawing colour along the pointer's path — the fundamental pixel art tool.
/// </summary>
/// <remarks>
/// <para>
/// The pencil <em>replaces</em> pixels rather than blending onto them. That is deliberate
/// and is the defining difference between a pixel art pencil and a paint program's brush:
/// dragging back and forth over the same spot must not darken it, and placing a 50%-alpha
/// pixel must store exactly 50% alpha. Anti-aliasing is likewise absent by design.
/// </para>
/// <para>
/// <b>It is always square, and never soft.</b> The brush shape setting and the softness and
/// stabilisation settings are all ignored here, so that whatever else has been fiddled with,
/// the pencil is the one tool an artist can reach for and know exactly what it will do: put
/// the chosen colour in the pixel under the pointer, or in the square block of pixels the size
/// says. Turning the size up makes a bigger pixel, not a rounder one.
/// </para>
/// </remarks>
public sealed class PencilTool : StrokeToolBase
{
    /// <summary>The stable identifier of this tool.</summary>
    public const string ToolId = "xpxel.tool.pencil";

    /// <inheritdoc />
    public override string Id => ToolId;

    /// <inheritdoc />
    /// <remarks>
    /// Not a typo. It only ever draws pixels, so it is the one tool in the box that gets to
    /// wear the application's name.
    /// </remarks>
    public override string DisplayName => "Pen-xel";

    /// <inheritdoc />
    public override string Description =>
        "Draw clean, hard pixels in the drawing colour. Always square, whatever the size.";

    /// <inheritdoc />
    protected override BrushShape ResolveBrushShape(ToolContext context) => BrushShape.Square;

    /// <inheritdoc />
    protected override PixelColor ResolveStrokeColor(ToolContext context, ToolPointerButton button)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Colors.Primary;
    }
}
