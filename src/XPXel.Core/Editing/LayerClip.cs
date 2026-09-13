using XPXel.Core.Documents;
using XPXel.Core.Imaging;

namespace XPXel.Core.Editing;

/// <summary>
/// A whole layer taken out of a document and held so it can be put back into one — its name,
/// its appearance, whether it is shared across frames, and the drawing on each frame.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not a <see cref="PixelClip"/>.</b> A pixel clip is what is marked on the
/// frame in front of you. This is the layer itself, across the whole timeline, which is what
/// somebody means when they say "copy that layer" — and there was previously no way to say it
/// at all. Trying to do it with Ctrl+C and Ctrl+V meant pasting frame by frame and getting a
/// new layer each time.
/// </para>
/// <para>
/// <b>Artwork is held by frame position, not by frame identity.</b> The clip may be pasted into
/// a different document, or into the same one after frames have been added and removed, and a
/// frame's identity means nothing in either case. Positions do: a four-frame layer pasted into a
/// two-frame document brings the first two drawings and drops the rest, which is the only answer
/// that is useful rather than merely correct.
/// </para>
/// <para>
/// <b>Raster layers only.</b> A folder is a structure rather than a drawing and a text layer's
/// pixels are a cache regenerated from its words, so neither survives being lifted out of the
/// document that owns it. Both are refused rather than silently flattened.
/// </para>
/// </remarks>
public sealed class LayerClip
{
    private readonly PixelBuffer?[] _frames;

    private LayerClip(LayerNode source, PixelBuffer?[] frames, bool isContinuous)
    {
        _frames = frames;

        Name = source.Name;
        Opacity = source.Opacity;
        BlendMode = source.BlendMode;
        IsVisible = source.IsVisible;
        TagColor = source.TagColor;
        IsContinuous = isContinuous;
    }

    /// <summary>The name the copy will carry.</summary>
    public string Name { get; }

    /// <summary>The opacity the copy will carry.</summary>
    public double Opacity { get; }

    /// <summary>The blend mode the copy will carry.</summary>
    public BlendMode BlendMode { get; }

    /// <summary>Whether the copy will be visible.</summary>
    public bool IsVisible { get; }

    /// <summary>The colour label the copy will carry, if any.</summary>
    public Primitives.PixelColor? TagColor { get; }

    /// <summary>Whether the copy holds one drawing shared by every frame.</summary>
    public bool IsContinuous { get; }

    /// <summary>Takes a copy of a layer, artwork on every frame included.</summary>
    /// <param name="document">The document the layer belongs to.</param>
    /// <param name="layer">The layer to copy.</param>
    /// <returns>The clip, or <see langword="null"/> if the layer cannot be copied.</returns>
    public static LayerClip? From(PixelDocument document, LayerNode layer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layer);

        if (layer is not RasterLayer)
        {
            return null;
        }

        PixelBuffer?[] frames = new PixelBuffer?[document.Frames.Count];

        for (int i = 0; i < document.Frames.Count; i++)
        {
            // Cloned now rather than referenced, so that drawing on the original afterwards
            // does not change what is waiting on the clipboard.
            frames[i] = document.Cels.Find(layer.Id, document.Frames[i].Id)?.Pixels.Clone();
        }

        return new LayerClip(layer, frames, layer.IsContinuous);
    }

    /// <summary>
    /// Builds a fresh layer and its cels for a document, ready to be inserted.
    /// </summary>
    /// <param name="document">The document the copy will belong to.</param>
    /// <returns>
    /// The new layer, the cels to add with it, and whether it should be made continuous once
    /// it is in — which the caller records as its own undoable step.
    /// </returns>
    /// <remarks>
    /// A new identity every time, so pasting twice produces two layers rather than one layer
    /// that cannot decide where it is.
    /// </remarks>
    public (RasterLayer Layer, List<Cel> Cels, bool IsContinuous) Rebuild(PixelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        RasterLayer layer = new(Guid.NewGuid(), Name)
        {
            Opacity = Opacity,
            BlendMode = BlendMode,
            IsVisible = IsVisible,
            TagColor = TagColor,
        };

        List<Cel> cels = [];
        int count = Math.Min(_frames.Length, document.Frames.Count);

        for (int i = 0; i < count; i++)
        {
            if (_frames[i] is { } pixels)
            {
                cels.Add(new Cel(layer.Id, document.Frames[i].Id, pixels.Clone()));
            }
        }

        return (layer, cels, IsContinuous);
    }
}
