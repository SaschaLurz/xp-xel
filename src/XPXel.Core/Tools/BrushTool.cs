using XPXel.Core.Primitives;

namespace XPXel.Core.Tools;

/// <summary>
/// Paints with a shaped, optionally soft-edged brush — the counterpart to the pencil, for
/// when the mark matters more than the individual pixel.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a separate tool rather than a pencil option.</b> The pencil's contract is
/// that it writes one exact colour into one exact pixel: dragging back and forth over a spot
/// must not darken it, and a 50%-alpha colour must store exactly 50% alpha. A soft, stabilised
/// brush breaks every part of that on purpose. Two tools with two honest contracts is clearer
/// than one tool that quietly stops being predictable when a slider is moved.
/// </para>
/// <para>
/// It defaults to a round shape because that is what distinguishes it in use; the shape,
/// size, softness and stabilisation all come from the shared
/// <see cref="Editing.ToolSettings"/>, so a size chosen here is still in force after
/// switching to the eraser.
/// </para>
/// </remarks>
public sealed class BrushTool : StrokeToolBase
{
    /// <summary>The stable identifier of this tool.</summary>
    public const string ToolId = "xpxel.tool.brush";

    /// <inheritdoc />
    public override string Id => ToolId;

    /// <inheritdoc />
    public override string DisplayName => "Brush";

    /// <inheritdoc />
    public override string Description =>
        "Paint with a round brush. Size, softness and stabilisation are set in the Brush panel.";

    /// <inheritdoc />
    protected override bool SupportsBrushDynamics => true;

    /// <inheritdoc />
    protected override PixelColor ResolveStrokeColor(ToolContext context, ToolPointerButton button)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.GetColorForButton(button);
    }
}
