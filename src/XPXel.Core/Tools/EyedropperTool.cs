using XPXel.Core.Documents;
using XPXel.Core.Primitives;

namespace XPXel.Core.Tools;

/// <summary>
/// Picks a colour from the canvas into the primary or secondary colour slot.
/// </summary>
/// <remarks>
/// <para>
/// By default the tool samples the <em>flattened</em> image, so the artist gets the colour
/// they can actually see rather than whatever happens to be on the active layer. The canvas
/// supplies that flattened image through <see cref="ToolContext.CompositeSampler"/>; when no
/// sampler is available — in tests, or headless use — the tool falls back to the active
/// cel.
/// </para>
/// <para>
/// Holding Alt samples the active layer alone, which is how an artist checks what is
/// actually on a layer beneath something else.
/// </para>
/// <para>
/// This tool makes no document change and therefore creates no undo entry. Changing the
/// selected colour is a UI state change, and artists do not expect Ctrl+Z to revert it.
/// </para>
/// </remarks>
public sealed class EyedropperTool : ITool
{
    /// <summary>The stable identifier of this tool.</summary>
    public const string ToolId = "xpxel.tool.eyedropper";

    /// <inheritdoc />
    public string Id => ToolId;

    /// <inheritdoc />
    public string DisplayName => "Colour Picker";

    /// <inheritdoc />
    public string Description => "Pick a colour from the canvas. Hold Alt to sample the active layer only.";

    /// <inheritdoc />
    public void OnPointerPressed(ToolContext context, ToolPointerEventArgs e) => Pick(context, e);

    /// <inheritdoc />
    public void OnPointerMoved(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // Continue picking while a button is held, so the artist can scrub across the canvas
        // and watch the colour update.
        if (e.Button is ToolPointerButton.Left or ToolPointerButton.Right)
        {
            Pick(context, e);
        }
    }

    /// <inheritdoc />
    public void OnPointerReleased(ToolContext context, ToolPointerEventArgs e)
    {
        // Picking happens on press and drag; nothing to finish.
    }

    /// <inheritdoc />
    public void Cancel(ToolContext context)
    {
        // Stateless between events.
    }

    private static void Pick(ToolContext context, ToolPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(e);

        if (!context.Document.Bounds.Contains(e.Position))
        {
            return;
        }

        PixelColor? sampled = Sample(context, e);
        if (sampled is not PixelColor color)
        {
            return;
        }

        if (e.Button == ToolPointerButton.Right)
        {
            context.Colors.Secondary = color;
        }
        else
        {
            context.Colors.Primary = color;
        }
    }

    private static PixelColor? Sample(ToolContext context, ToolPointerEventArgs e)
    {
        if (!e.IsAltDown && context.CompositeSampler is { } sampler)
        {
            return sampler(e.Position.X, e.Position.Y);
        }

        if (context.Workspace.ActiveRasterLayer is not RasterLayer layer)
        {
            return null;
        }

        Cel? cel = context.Document.Cels.Find(layer.Id, context.Workspace.ActiveFrame.Id);
        if (cel is null)
        {
            return PixelColor.Transparent;
        }

        return cel.Pixels.GetPixelOrTransparent(
            e.Position.X - cel.Offset.X,
            e.Position.Y - cel.Offset.Y);
    }
}
