using XPXel.Core.Documents;
using XPXel.Core.Primitives;

namespace XPXel.Core.Text;

/// <summary>
/// Regenerates a <see cref="TextLayer"/>'s pixels from its text and style.
/// </summary>
/// <remarks>
/// <para>
/// This is the single bridge between the two halves of a text layer: the words, which are
/// document data, and the glyphs, which are a cache. Every path that changes a text layer —
/// typing, restyling, moving it, loading a project — ends here.
/// </para>
/// <para>
/// <b>The whole cel is redrawn, not a dirty rectangle.</b> Working out which pixels the
/// <em>previous</em> text covered would mean keeping the last rendered bounds in sync with
/// undo, redo and reload, and getting it wrong leaves ghost glyphs on the canvas. A text
/// block is small next to the cost of that bug, and the document is still told the exact
/// rectangle that changed, so the canvas re-uploads only what it must.
/// </para>
/// </remarks>
public static class TextLayerRenderer
{
    /// <summary>Redraws a text layer's cel for one frame.</summary>
    /// <param name="document">The document holding the layer.</param>
    /// <param name="layer">The layer to re-render.</param>
    /// <param name="frame">The frame whose cel should be written.</param>
    /// <param name="rasterizer">The font engine to render with.</param>
    /// <returns>The document-space rectangle the new glyphs occupy, which may be empty.</returns>
    public static ImageRect Render(
        PixelDocument document,
        TextLayer layer,
        Frame frame,
        ITextRasterizer rasterizer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(rasterizer);

        Cel cel = document.GetOrCreateCel(layer, frame);
        cel.Pixels.Clear();

        ImageRect drawn = ImageRect.Empty;

        if (rasterizer.Rasterize(layer.Text, layer.Style) is { } raster)
        {
            // The rasteriser reports its own origin relative to the block anchor, because a
            // descender or an italic slant can overhang the pen position.
            ImagePoint at = new(
                layer.Origin.X + raster.Origin.X - cel.Offset.X,
                layer.Origin.Y + raster.Origin.Y - cel.Offset.Y);

            raster.Pixels.CopyRegionTo(raster.Pixels.Bounds, cel.Pixels, at);
            drawn = new ImageRect(at, raster.Pixels.Size).Intersect(cel.Pixels.Bounds);
        }

        // The cel was cleared in full, so the whole cel is what may have changed on screen.
        document.NotifyPixelsChanged(cel.Bounds);

        return drawn.Translate(cel.Offset.X, cel.Offset.Y);
    }

    /// <summary>
    /// Re-renders every text layer in a document, for use after loading a project or after
    /// a font engine becomes available.
    /// </summary>
    /// <param name="document">The document to refresh.</param>
    /// <param name="rasterizer">The font engine to render with.</param>
    /// <remarks>
    /// A project file stores each text layer's rendered pixels as well as its words, so a
    /// freshly loaded document already looks right. This exists for the case where it might
    /// not: a file written on a machine that had a font this one does not, where honouring
    /// the artist's words with a substituted font is better than showing glyphs the document
    /// no longer describes.
    /// </remarks>
    public static void RenderAll(PixelDocument document, ITextRasterizer rasterizer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(rasterizer);

        foreach (LayerNode node in document.EnumerateLayers())
        {
            if (node is not TextLayer text)
            {
                continue;
            }

            foreach (Frame frame in document.Frames)
            {
                Render(document, text, frame, rasterizer);
            }
        }
    }
}
