using XPXel.Core.Primitives;

namespace XPXel.Core.Tools;

/// <summary>
/// Clears pixels back to fully transparent along the pointer's path.
/// </summary>
/// <remarks>
/// Erasing is the same operation as drawing, with transparent as the colour — which is why
/// this tool is four lines. It inherits brush size, brush shape, stroke interpolation and
/// undo behaviour from <see cref="StrokeToolBase"/> and therefore always stays consistent
/// with the pencil.
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
    protected override PixelColor ResolveStrokeColor(ToolContext context, ToolPointerButton button) =>
        PixelColor.Transparent;
}
