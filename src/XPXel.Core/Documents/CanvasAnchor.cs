namespace XPXel.Core.Documents;

/// <summary>
/// Which part of the existing artwork stays put when the canvas is resized.
/// </summary>
/// <remarks>
/// <para>
/// Growing a 32×32 sprite to 64×64 has nine sensible answers and no default that is right
/// every time: a character standing on the bottom edge wants to stay on the bottom edge, an
/// icon wants to stay centred, and a tile being extended to the right wants its left edge
/// pinned. The anchor is how the artist says which.
/// </para>
/// <para>
/// The same choice governs cropping, where it decides which side the pixels are taken from.
/// </para>
/// </remarks>
public enum CanvasAnchor
{
    /// <summary>The artwork's top-left corner stays put.</summary>
    TopLeft = 0,

    /// <summary>The top edge stays put, and the artwork centres horizontally.</summary>
    Top = 1,

    /// <summary>The top-right corner stays put.</summary>
    TopRight = 2,

    /// <summary>The left edge stays put, and the artwork centres vertically.</summary>
    Left = 3,

    /// <summary>The artwork stays centred in both directions. The usual choice.</summary>
    Center = 4,

    /// <summary>The right edge stays put, and the artwork centres vertically.</summary>
    Right = 5,

    /// <summary>The bottom-left corner stays put.</summary>
    BottomLeft = 6,

    /// <summary>The bottom edge stays put, and the artwork centres horizontally.</summary>
    Bottom = 7,

    /// <summary>The bottom-right corner stays put.</summary>
    BottomRight = 8,
}
