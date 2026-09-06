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
    /// How many of the leading colours are reachable by a number key.
    /// </summary>
    /// <remarks>
    /// Ten, because that is how many digits a keyboard has in a row: the first nine answer to
    /// <c>1</c> through <c>9</c> and the tenth to <c>0</c>, the arrangement every game with a
    /// hotbar uses. Colours past the tenth are still perfectly usable, they simply have to be
    /// clicked.
    /// </remarks>
    public const int HotbarSize = 10;

    /// <summary>
    /// Creates the palette assigned to a new document: black, white and the rainbow.
    /// </summary>
    /// <returns>A new palette containing <see cref="HotbarSize"/> colours.</returns>
    /// <remarks>
    /// These fill the number keys, so the order is the one a person would recite rather than
    /// anything perceptual: black, white, then round the wheel from red. A curated pixel-art
    /// palette such as DawnBringer's DB16 is a better place to *finish* a sprite, but it is a
    /// poor place to start when the artist is still learning which key is which — every
    /// entry is a muted, unfamiliar hue with no name.
    /// </remarks>
    public static Palette CreateDefault()
    {
        Palette palette = new("Default");

        ReadOnlySpan<uint> rgb =
        [
            0x000000, 0xFFFFFF,
            0xE43B44, 0xF77622, 0xFEE761, 0x63C74D,
            0x2CE8F5, 0x0099DB, 0x8B5FBF, 0xEC3F8B,
        ];

        foreach (uint value in rgb)
        {
            palette.Add(PixelColor.FromRgba((value << 8) | 0xFF));
        }

        return palette;
    }
}
