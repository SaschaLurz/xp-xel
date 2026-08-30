namespace XPXel.Core.Imaging;

/// <summary>
/// How a layer's colours combine with the layers beneath it.
/// </summary>
/// <remarks>
/// <para>
/// The names and the mathematics follow the W3C Compositing and Blending specification,
/// which is also what Photoshop, GIMP, Krita and Aseprite implement. Matching the standard
/// means artwork blended in XP-xel looks the same after a round-trip through another tool.
/// </para>
/// <para>
/// <b>Adding a mode.</b> Add a value here, then register its per-channel function in
/// <see cref="BlendFunctions"/>. Nothing else in the codebase needs to change: the
/// compositor resolves modes through the registry, and the layer panel enumerates this
/// enum to build its dropdown. Do not renumber or reuse existing values — they are
/// persisted by name in <c>.xpxel</c> project files, but numeric stability keeps
/// debugging sane.
/// </para>
/// </remarks>
public enum BlendMode
{
    /// <summary>Plain source-over compositing. The default for every new layer.</summary>
    Normal = 0,

    /// <summary>Multiplies the channels, always darkening. White is a no-op.</summary>
    Multiply = 1,

    /// <summary>Inverse of <see cref="Multiply"/>, always lightening. Black is a no-op.</summary>
    Screen = 2,

    /// <summary>Multiplies dark backdrop areas and screens light ones, increasing contrast.</summary>
    Overlay = 3,

    /// <summary>Keeps the darker of the two channel values.</summary>
    Darken = 4,

    /// <summary>Keeps the lighter of the two channel values.</summary>
    Lighten = 5,

    /// <summary>Brightens the backdrop to reflect the source. Useful for glow effects.</summary>
    ColorDodge = 6,

    /// <summary>Darkens the backdrop to reflect the source.</summary>
    ColorBurn = 7,

    /// <summary>Like <see cref="Overlay"/> but driven by the source rather than the backdrop.</summary>
    HardLight = 8,

    /// <summary>A gentler <see cref="HardLight"/>, similar to a diffuse spotlight.</summary>
    SoftLight = 9,

    /// <summary>Absolute difference of the channels. Applying it twice restores the original.</summary>
    Difference = 10,

    /// <summary>Like <see cref="Difference"/> but with lower contrast.</summary>
    Exclusion = 11,

    /// <summary>Adds the channels and clamps. Common for light and fire effects in game art.</summary>
    Addition = 12,

    /// <summary>Subtracts the source from the backdrop and clamps at zero.</summary>
    Subtract = 13,
}
