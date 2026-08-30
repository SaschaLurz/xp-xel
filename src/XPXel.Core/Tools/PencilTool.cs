using XPXel.Core.Primitives;

namespace XPXel.Core.Tools;

/// <summary>
/// Paints the drawing colour along the pointer's path — the fundamental pixel art tool.
/// </summary>
/// <remarks>
/// The pencil <em>replaces</em> pixels rather than blending onto them. That is deliberate
/// and is the defining difference between a pixel art pencil and a paint program's brush:
/// dragging back and forth over the same spot must not darken it, and placing a 50%-alpha
/// pixel must store exactly 50% alpha. Anti-aliasing is likewise absent by design.
/// </remarks>
public sealed class PencilTool : StrokeToolBase
{
    /// <summary>The stable identifier of this tool.</summary>
    public const string ToolId = "xpxel.tool.pencil";

    /// <inheritdoc />
    public override string Id => ToolId;

    /// <inheritdoc />
    public override string DisplayName => "Pencil";

    /// <inheritdoc />
    public override string Description => "Draw single pixels with the primary or secondary colour.";

    /// <inheritdoc />
    protected override PixelColor ResolveStrokeColor(ToolContext context, ToolPointerButton button)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.GetColorForButton(button);
    }
}
