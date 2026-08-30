using System.Buffers.Binary;

namespace XPXel.Formats.Png;

/// <summary>The five pixel layouts PNG defines, with the specification's numeric values.</summary>
internal enum PngColorType : byte
{
    /// <summary>One intensity channel.</summary>
    Greyscale = 0,

    /// <summary>Red, green and blue channels.</summary>
    Truecolour = 2,

    /// <summary>One channel of palette indices, accompanied by a PLTE chunk.</summary>
    Indexed = 3,

    /// <summary>Intensity plus alpha.</summary>
    GreyscaleAlpha = 4,

    /// <summary>Red, green, blue and alpha — what XP-xel writes.</summary>
    TruecolourAlpha = 6,
}

/// <summary>
/// The contents of a PNG <c>IHDR</c> chunk, plus the row geometry derived from it.
/// </summary>
/// <remarks>
/// Everything the decoder needs to interpret the compressed pixel data comes from these
/// thirteen bytes, so parsing and validating them in one place keeps the rest of the decoder
/// free of defensive checks.
/// </remarks>
internal sealed class PngHeader
{
    private PngHeader(int width, int height, byte bitDepth, PngColorType colorType, byte interlace)
    {
        Width = width;
        Height = height;
        BitDepth = bitDepth;
        ColorType = colorType;
        Interlace = interlace;

        Channels = colorType switch
        {
            PngColorType.Greyscale => 1,
            PngColorType.Truecolour => 3,
            PngColorType.Indexed => 1,
            PngColorType.GreyscaleAlpha => 2,
            PngColorType.TruecolourAlpha => 4,
            _ => throw new ImageFormatException($"Unsupported PNG colour type {(int)colorType}."),
        };

        int bitsPerPixel = Channels * bitDepth;

        // Rows are padded to whole bytes, which matters for bit depths below 8.
        BytesPerRow = ((Width * bitsPerPixel) + 7) / 8;

        // Filters operate on whole bytes, so for sub-byte depths the "previous pixel" is
        // simply the previous byte.
        FilterBytesPerPixel = Math.Max(1, bitsPerPixel / 8);

        ExpectedRawLength = (long)(BytesPerRow + 1) * Height;
    }

    public int Width { get; }

    public int Height { get; }

    public byte BitDepth { get; }

    public PngColorType ColorType { get; }

    public byte Interlace { get; }

    /// <summary>The number of samples per pixel implied by the colour type.</summary>
    public int Channels { get; }

    /// <summary>The number of bytes in one unfiltered scanline, excluding the filter byte.</summary>
    public int BytesPerRow { get; }

    /// <summary>The byte distance the scanline filters treat as "one pixel to the left".</summary>
    public int FilterBytesPerPixel { get; }

    /// <summary>The total decompressed size, used to pre-size the inflate buffer.</summary>
    public long ExpectedRawLength { get; }

    /// <summary>Parses and validates an <c>IHDR</c> payload.</summary>
    /// <param name="data">The thirteen bytes of the chunk.</param>
    /// <returns>The parsed header.</returns>
    /// <exception cref="ImageFormatException">The header is malformed or unsupported.</exception>
    public static PngHeader Parse(byte[] data)
    {
        if (data.Length < 13)
        {
            throw new ImageFormatException("The PNG IHDR chunk is too short.");
        }

        uint width = BinaryPrimitives.ReadUInt32BigEndian(data);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
        byte bitDepth = data[8];
        byte colorType = data[9];
        byte compression = data[10];
        byte filter = data[11];
        byte interlace = data[12];

        if (width == 0 || height == 0)
        {
            throw new ImageFormatException("The PNG image declares a zero width or height.");
        }

        // Guard against a header that would make the decoder allocate absurd buffers.
        if (width > int.MaxValue / 4 || height > int.MaxValue / 4)
        {
            throw new ImageFormatException($"The PNG image dimensions {width}x{height} are implausibly large.");
        }

        if (compression != 0)
        {
            throw new ImageFormatException($"The PNG file uses unknown compression method {compression}.");
        }

        if (filter != 0)
        {
            throw new ImageFormatException($"The PNG file uses unknown filter method {filter}.");
        }

        if (interlace != 0)
        {
            throw new ImageFormatException(
                "Interlaced (Adam7) PNG files are not supported yet. Re-save the image without interlacing.");
        }

        if (!Enum.IsDefined(typeof(PngColorType), colorType))
        {
            throw new ImageFormatException($"The PNG file uses unknown colour type {colorType}.");
        }

        PngColorType parsedColorType = (PngColorType)colorType;
        if (!IsBitDepthValid(parsedColorType, bitDepth))
        {
            throw new ImageFormatException(
                $"A bit depth of {bitDepth} is not valid for PNG colour type {colorType}.");
        }

        return new PngHeader((int)width, (int)height, bitDepth, parsedColorType, interlace);
    }

    /// <summary>The bit depths the PNG specification permits for each colour type.</summary>
    private static bool IsBitDepthValid(PngColorType colorType, byte bitDepth) => colorType switch
    {
        PngColorType.Greyscale => bitDepth is 1 or 2 or 4 or 8 or 16,
        PngColorType.Indexed => bitDepth is 1 or 2 or 4 or 8,
        PngColorType.Truecolour or PngColorType.GreyscaleAlpha or PngColorType.TruecolourAlpha => bitDepth is 8 or 16,
        _ => false,
    };
}
