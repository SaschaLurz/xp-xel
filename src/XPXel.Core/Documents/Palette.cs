using System.Collections.ObjectModel;
using XPXel.Core.ComponentModel;
using XPXel.Core.Primitives;

namespace XPXel.Core.Documents;

/// <summary>
/// An ordered set of colours saved with a document.
/// </summary>
/// <remarks>
/// <para>
/// Working from a fixed, small palette is central to how pixel art is made — it is what
/// gives a sprite sheet visual coherence — so the palette is part of the document rather
/// than an application-level preference.
/// </para>
/// <para>
/// <b>Growth path.</b> Palette <em>files</em> (GIMP <c>.gpl</c>, Paint.NET <c>.txt</c>,
/// Aseprite <c>.aseprite</c> palettes, Lospec <c>.hex</c>) are not read yet. When they are,
/// they belong in XPXel.Formats behind a registry mirroring <c>ImageFormatRegistry</c>, not
/// here: this type stays a plain in-memory collection.
/// </para>
/// </remarks>
public sealed class Palette : ObservableModel
{
    private readonly ObservableCollection<PixelColor> _colors = [];
    private string _name;

    /// <summary>Initialises an empty palette.</summary>
    /// <param name="name">A display name for the palette.</param>
    public Palette(string name = "Palette")
    {
        _name = name ?? string.Empty;
        Colors = new ReadOnlyObservableCollection<PixelColor>(_colors);
    }

    /// <summary>The palette's display name.</summary>
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? string.Empty);
    }

    /// <summary>
    /// The colours, in the order they are shown in the palette panel. Bindable; modify
    /// through the methods on this class.
    /// </summary>
    public ReadOnlyObservableCollection<PixelColor> Colors { get; }

    /// <summary>Appends a colour to the end of the palette.</summary>
    /// <param name="color">The colour to add.</param>
    public void Add(PixelColor color) => _colors.Add(color);

    /// <summary>Inserts a colour at the given position.</summary>
    /// <param name="index">The position to insert at.</param>
    /// <param name="color">The colour to insert.</param>
    public void Insert(int index, PixelColor color) => _colors.Insert(index, color);

    /// <summary>Replaces the colour at the given position.</summary>
    /// <param name="index">The position to overwrite.</param>
    /// <param name="color">The new colour.</param>
    public void Replace(int index, PixelColor color) => _colors[index] = color;

    /// <summary>Removes the colour at the given position.</summary>
    /// <param name="index">The position to remove.</param>
    public void RemoveAt(int index) => _colors.RemoveAt(index);

    /// <summary>Removes every colour.</summary>
    public void Clear() => _colors.Clear();

    /// <summary>Replaces the entire contents of the palette in one operation.</summary>
    /// <param name="colors">The colours to load.</param>
    public void ReplaceAll(IEnumerable<PixelColor> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);

        _colors.Clear();
        foreach (PixelColor color in colors)
        {
            _colors.Add(color);
        }
    }

    /// <summary>Returns the position of a colour, or -1 if the palette does not contain it.</summary>
    /// <param name="color">The colour to locate.</param>
    /// <returns>The zero-based index, or -1.</returns>
    public int IndexOf(PixelColor color) => _colors.IndexOf(color);

    /// <summary>
    /// Creates the palette assigned to a new document: DawnBringer's 16-colour palette,
    /// a widely used public-domain starting point for pixel art that covers a full range of
    /// hues and values in very few entries.
    /// </summary>
    /// <returns>A new palette containing sixteen colours.</returns>
    public static Palette CreateDefault()
    {
        Palette palette = new("DB16");

        ReadOnlySpan<uint> rgb =
        [
            0x140C1C, 0x442434, 0x30346D, 0x4E4A4E,
            0x854C30, 0x346524, 0xD04648, 0x757161,
            0x597DCE, 0xD27D2C, 0x8595A1, 0x6DAA2C,
            0xD2AA99, 0x6DC2CA, 0xDAD45E, 0xDEEED6,
        ];

        foreach (uint value in rgb)
        {
            palette.Add(PixelColor.FromRgba((value << 8) | 0xFF));
        }

        return palette;
    }
}
