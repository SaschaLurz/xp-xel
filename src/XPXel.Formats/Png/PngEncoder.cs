using System.Buffers.Binary;
using System.IO.Compression;
using XPXel.Core.Imaging;

namespace XPXel.Formats.Png;

/// <summary>
/// Encodes a <see cref="PixelBuffer"/> as an 8-bit RGBA PNG.
/// </summary>
/// <remarks>
/// <para>
/// The encoder always writes colour type 6 (truecolour with alpha) at 8 bits per channel,
/// which is exactly the editor's internal representation and therefore lossless in every
/// case.
/// </para>
/// <para>
/// <b>Deliberate future improvement:</b> pixel art usually uses few colours, and a palette
/// (colour type 3) would often cut file size by more than half. Writing indexed PNGs means
/// collecting the distinct colours, bailing out above 256, and emitting PLTE and tRNS
/// chunks; the chunk writer already supports it. It is left out of the first version to keep
/// the encoder simple and its output trivially verifiable.
/// </para>
/// </remarks>
internal static class PngEncoder
{
    private const int BytesPerPixel = PixelBuffer.BytesPerPixel;

    /// <summary>Writes an image as a PNG.</summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="image">The image to encode.</param>
    public static void Encode(Stream stream, PixelBuffer image)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(image);

        PngChunkIO.WriteSignature(stream);
        WriteHeader(stream, image);
        WriteImageData(stream, image);
        PngChunkIO.WriteChunk(stream, "IEND", []);
    }

    private static void WriteHeader(Stream stream, PixelBuffer image)
    {
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)image.Width);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)image.Height);
        header[8] = 8;                                     // bit depth
        header[9] = (byte)PngColorType.TruecolourAlpha;    // colour type
        header[10] = 0;                                    // compression: deflate
        header[11] = 0;                                    // filter method: adaptive
        header[12] = 0;                                    // interlace: none

        PngChunkIO.WriteChunk(stream, "IHDR", header);
    }

    private static void WriteImageData(Stream stream, PixelBuffer image)
    {
        // A single IDAT chunk is legal and simplest. Splitting into several only matters for
        // streaming decoders working with limited memory.
        PngChunkIO.WriteChunk(stream, "IDAT", Compress(image));
    }

    /// <summary>
    /// Filters and deflates an image into the byte stream a PNG data chunk carries.
    /// </summary>
    /// <param name="image">The image to compress.</param>
    /// <returns>The compressed data, ready to place in an IDAT or fdAT chunk.</returns>
    /// <remarks>
    /// Split out from <see cref="WriteImageData"/> so the animated encoder can reuse it: an
    /// APNG frame is exactly this byte stream in an fdAT chunk instead of an IDAT one. Sharing
    /// it means a still PNG and one frame of an animation are compressed identically rather
    /// than by two implementations that could drift.
    /// </remarks>
    public static byte[] Compress(PixelBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);

        byte[] filtered = BuildFilteredScanlines(image);

        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(filtered);
        }

        return compressed.ToArray();
    }

    /// <summary>Writes the IHDR chunk describing an image.</summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="image">The image the header describes.</param>
    public static void WriteImageHeader(Stream stream, PixelBuffer image) => WriteHeader(stream, image);

    /// <summary>
    /// Applies PNG's per-scanline filters, choosing the best filter for each row.
    /// </summary>
    /// <remarks>
    /// Filtering does not compress anything by itself; it rewrites each byte as a difference
    /// from a neighbour so that deflate has long runs of near-zero values to work with. The
    /// selection heuristic — pick the filter whose output has the smallest sum of absolute
    /// signed values — is the one recommended by the PNG specification, and in practice
    /// lands within a few percent of an exhaustive search.
    /// </remarks>
    private static byte[] BuildFilteredScanlines(PixelBuffer image)
    {
        int stride = image.Width * BytesPerPixel;
        byte[] output = new byte[(long)(stride + 1) * image.Height];

        byte[] candidate = new byte[stride];
        byte[] best = new byte[stride];
        byte[] previousRow = new byte[stride];

        for (int y = 0; y < image.Height; y++)
        {
            ReadOnlySpan<byte> row = image.GetReadOnlyRowSpan(y);
            int bestFilter = 0;
            long bestScore = long.MaxValue;

            for (byte filter = 0; filter <= 4; filter++)
            {
                long score = ApplyFilter(filter, row, previousRow, candidate);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestFilter = filter;
                    candidate.CopyTo(best, 0);
                }
            }

            int offset = y * (stride + 1);
            output[offset] = (byte)bestFilter;
            best.AsSpan(0, stride).CopyTo(output.AsSpan(offset + 1, stride));

            row.CopyTo(previousRow);
        }

        return output;
    }

    /// <summary>
    /// Writes one filtered scanline into <paramref name="destination"/> and returns its
    /// heuristic score: the sum of the absolute values of the filtered bytes read as signed.
    /// </summary>
    private static long ApplyFilter(
        byte filter,
        ReadOnlySpan<byte> row,
        ReadOnlySpan<byte> previousRow,
        Span<byte> destination)
    {
        long score = 0;

        for (int i = 0; i < row.Length; i++)
        {
            int left = i >= BytesPerPixel ? row[i - BytesPerPixel] : 0;
            int above = previousRow[i];
            int upperLeft = i >= BytesPerPixel ? previousRow[i - BytesPerPixel] : 0;

            int value = filter switch
            {
                0 => row[i],
                1 => row[i] - left,
                2 => row[i] - above,
                3 => row[i] - ((left + above) / 2),
                4 => row[i] - PngDecoder.PaethPredictor(left, above, upperLeft),
                _ => row[i],
            };

            byte encoded = (byte)value;
            destination[i] = encoded;
            // Widen to int before taking the absolute value: Math.Abs(sbyte.MinValue)
            // overflows, and a filtered byte of 0x80 is entirely ordinary.
            score += Math.Abs((int)(sbyte)encoded);
        }

        return score;
    }
}
