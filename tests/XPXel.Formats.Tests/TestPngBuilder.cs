using System.Buffers.Binary;
using System.IO.Compression;
using XPXel.Formats.Png;

namespace XPXel.Formats.Tests;

/// <summary>
/// Builds PNG files by hand so the decoder can be tested against colour types and bit depths
/// that XP-xel's own encoder never produces.
/// </summary>
/// <remarks>
/// Round-tripping through the encoder only proves the two halves agree with each other. The
/// files real users open come from Aseprite, Photoshop, GIMP and asset packs, and those use
/// indexed colour, greyscale and colour-key transparency freely. This helper lets those
/// cases be covered without checking binary fixtures into the repository.
/// </remarks>
internal static class TestPngBuilder
{
    /// <summary>Assembles a PNG file from raw, unfiltered scanlines.</summary>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <param name="bitDepth">Bits per sample.</param>
    /// <param name="colorType">The PNG colour type byte.</param>
    /// <param name="scanlines">
    /// The unfiltered rows, concatenated. Each row must already be padded to a whole number
    /// of bytes; a filter byte of 0 is prepended automatically.
    /// </param>
    /// <param name="palette">The PLTE payload, for indexed images.</param>
    /// <param name="transparency">The tRNS payload.</param>
    /// <param name="interlace">The interlace method; 1 produces a file the decoder rejects.</param>
    /// <returns>The complete PNG file.</returns>
    public static byte[] Build(
        int width,
        int height,
        byte bitDepth,
        byte colorType,
        byte[] scanlines,
        byte[]? palette = null,
        byte[]? transparency = null,
        byte interlace = 0)
    {
        using MemoryStream file = new();
        PngChunkIO.WriteSignature(file);

        byte[] header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = bitDepth;
        header[9] = colorType;
        header[10] = 0;
        header[11] = 0;
        header[12] = interlace;
        PngChunkIO.WriteChunk(file, "IHDR", header);

        if (palette is not null)
        {
            PngChunkIO.WriteChunk(file, "PLTE", palette);
        }

        if (transparency is not null)
        {
            PngChunkIO.WriteChunk(file, "tRNS", transparency);
        }

        int bytesPerRow = scanlines.Length / height;
        byte[] filtered = new byte[(bytesPerRow + 1) * height];
        for (int y = 0; y < height; y++)
        {
            filtered[y * (bytesPerRow + 1)] = 0; // filter: None
            scanlines.AsSpan(y * bytesPerRow, bytesPerRow)
                     .CopyTo(filtered.AsSpan((y * (bytesPerRow + 1)) + 1, bytesPerRow));
        }

        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(filtered);
        }

        PngChunkIO.WriteChunk(file, "IDAT", compressed.ToArray());
        PngChunkIO.WriteChunk(file, "IEND", []);

        return file.ToArray();
    }
}
