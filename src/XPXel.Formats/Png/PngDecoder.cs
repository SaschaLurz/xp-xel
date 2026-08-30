using System.Buffers.Binary;
using System.IO.Compression;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;

namespace XPXel.Formats.Png;

/// <summary>
/// Decodes a PNG file into a <see cref="PixelBuffer"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Supported:</b> all five colour types (greyscale, truecolour, indexed, greyscale with
/// alpha, truecolour with alpha), bit depths 1, 2, 4, 8 and 16, palette transparency
/// (<c>tRNS</c>) and single-colour transparency. Everything is converted to the editor's
/// straight-alpha 8-bit RGBA representation.
/// </para>
/// <para>
/// <b>Not supported:</b> Adam7 interlacing, which is rejected with a clear message rather
/// than decoded incorrectly. Interlaced PNGs are vanishingly rare among pixel art assets;
/// adding support means running the same unfiltering over seven sub-images.
/// </para>
/// <para>
/// <b>Why hand-written rather than a library.</b> A native imaging library would drag in
/// per-platform binaries, which is exactly what breaks a WebAssembly build, and would also
/// need version-matching against whatever the renderer already bundles. PNG's decoder is
/// small and completely specified, .NET provides the zlib layer, and owning it means indexed
/// colour and custom chunks — both squarely on this project's roadmap — stay reachable.
/// </para>
/// </remarks>
internal static class PngDecoder
{
    /// <summary>Decodes a PNG stream.</summary>
    /// <param name="stream">The stream to read, positioned at the signature.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="ImageFormatException">The file is malformed or uses an unsupported feature.</exception>
    public static PixelBuffer Decode(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        PngChunkIO.ReadSignature(stream);

        PngHeader? header = null;
        byte[]? palette = null;
        byte[]? paletteAlpha = null;
        byte[]? colorKey = null;
        using MemoryStream compressed = new();

        while (true)
        {
            PngChunkIO.PngChunk chunk = PngChunkIO.ReadChunk(stream);

            switch (chunk.Type)
            {
                case "IHDR":
                    header = PngHeader.Parse(chunk.Data);
                    break;

                case "PLTE":
                    palette = chunk.Data;
                    break;

                case "tRNS":
                    // The meaning of tRNS depends on the colour type: for indexed images it
                    // is a per-entry alpha table, otherwise it names a single transparent
                    // colour.
                    if (header is { ColorType: PngColorType.Indexed })
                    {
                        paletteAlpha = chunk.Data;
                    }
                    else
                    {
                        colorKey = chunk.Data;
                    }

                    break;

                case "IDAT":
                    compressed.Write(chunk.Data);
                    break;

                case "IEND":
                    if (header is null)
                    {
                        throw new ImageFormatException("The PNG file has no IHDR chunk.");
                    }

                    compressed.Position = 0;
                    return BuildImage(header, compressed, palette, paletteAlpha, colorKey);

                default:
                    // Unrecognised chunks are skipped, which is what the specification
                    // requires and what keeps files written by other tools readable.
                    break;
            }
        }
    }

    private static PixelBuffer BuildImage(
        PngHeader header,
        Stream compressed,
        byte[]? palette,
        byte[]? paletteAlpha,
        byte[]? colorKey)
    {
        byte[] raw = Inflate(compressed, header.ExpectedRawLength);
        byte[] scanlines = Unfilter(raw, header);

        PixelBuffer buffer = new(new ImageSize(header.Width, header.Height));
        PixelReader reader = new(header, palette, paletteAlpha, colorKey);

        for (int y = 0; y < header.Height; y++)
        {
            ReadOnlySpan<byte> row = scanlines.AsSpan(y * header.BytesPerRow, header.BytesPerRow);
            Span<byte> target = buffer.GetRowSpan(y);

            for (int x = 0; x < header.Width; x++)
            {
                PixelColor color = reader.ReadPixel(row, x);
                int i = x * PixelBuffer.BytesPerPixel;
                target[i] = color.R;
                target[i + 1] = color.G;
                target[i + 2] = color.B;
                target[i + 3] = color.A;
            }
        }

        return buffer;
    }

    private static byte[] Inflate(Stream compressed, long expectedLength)
    {
        try
        {
            using ZLibStream zlib = new(compressed, CompressionMode.Decompress, leaveOpen: true);
            using MemoryStream output = new(capacity: (int)Math.Min(expectedLength, int.MaxValue));
            zlib.CopyTo(output);
            return output.ToArray();
        }
        catch (InvalidDataException ex)
        {
            throw new ImageFormatException("The PNG file's compressed image data is corrupt.", ex);
        }
    }

    /// <summary>
    /// Reverses the per-scanline filters PNG applies before compression.
    /// </summary>
    /// <remarks>
    /// Each row is prefixed with a filter byte and is decoded relative to the pixel to its
    /// left (<c>a</c>), the pixel above (<c>b</c>) and the pixel above-left (<c>c</c>).
    /// Filtering is what makes PNG compress well; it must be undone before the bytes mean
    /// anything.
    /// </remarks>
    private static byte[] Unfilter(byte[] raw, PngHeader header)
    {
        int stride = header.BytesPerRow;
        int filterBpp = header.FilterBytesPerPixel;
        long required = (long)(stride + 1) * header.Height;

        if (raw.LongLength < required)
        {
            throw new ImageFormatException(
                $"The PNG image data is truncated: expected {required} bytes but decompressed {raw.LongLength}.");
        }

        byte[] output = new byte[(long)stride * header.Height];

        for (int y = 0; y < header.Height; y++)
        {
            int sourceOffset = (y * (stride + 1)) + 1;
            int targetOffset = y * stride;
            byte filter = raw[sourceOffset - 1];

            Span<byte> current = output.AsSpan(targetOffset, stride);
            raw.AsSpan(sourceOffset, stride).CopyTo(current);
            ReadOnlySpan<byte> previous = y > 0 ? output.AsSpan(targetOffset - stride, stride) : [];

            switch (filter)
            {
                case 0: // None
                    break;

                case 1: // Sub
                    for (int i = filterBpp; i < stride; i++)
                    {
                        current[i] = (byte)(current[i] + current[i - filterBpp]);
                    }

                    break;

                case 2: // Up
                    if (!previous.IsEmpty)
                    {
                        for (int i = 0; i < stride; i++)
                        {
                            current[i] = (byte)(current[i] + previous[i]);
                        }
                    }

                    break;

                case 3: // Average
                    for (int i = 0; i < stride; i++)
                    {
                        int left = i >= filterBpp ? current[i - filterBpp] : 0;
                        int above = previous.IsEmpty ? 0 : previous[i];
                        current[i] = (byte)(current[i] + ((left + above) / 2));
                    }

                    break;

                case 4: // Paeth
                    for (int i = 0; i < stride; i++)
                    {
                        int left = i >= filterBpp ? current[i - filterBpp] : 0;
                        int above = previous.IsEmpty ? 0 : previous[i];
                        int upperLeft = !previous.IsEmpty && i >= filterBpp ? previous[i - filterBpp] : 0;
                        current[i] = (byte)(current[i] + PaethPredictor(left, above, upperLeft));
                    }

                    break;

                default:
                    throw new ImageFormatException($"The PNG file uses unknown scanline filter {filter}.");
            }
        }

        return output;
    }

    /// <summary>
    /// Chooses whichever of the three neighbouring bytes is closest to their linear
    /// prediction — the heuristic at the heart of PNG's most effective filter.
    /// </summary>
    internal static int PaethPredictor(int left, int above, int upperLeft)
    {
        int estimate = left + above - upperLeft;
        int distanceLeft = Math.Abs(estimate - left);
        int distanceAbove = Math.Abs(estimate - above);
        int distanceUpperLeft = Math.Abs(estimate - upperLeft);

        if (distanceLeft <= distanceAbove && distanceLeft <= distanceUpperLeft)
        {
            return left;
        }

        return distanceAbove <= distanceUpperLeft ? above : upperLeft;
    }

    /// <summary>
    /// Turns the bytes of one unfiltered scanline into colours, handling every combination
    /// of colour type and bit depth the decoder supports.
    /// </summary>
    private readonly struct PixelReader(PngHeader header, byte[]? palette, byte[]? paletteAlpha, byte[]? colorKey)
    {
        public PixelColor ReadPixel(ReadOnlySpan<byte> row, int x) => header.ColorType switch
        {
            PngColorType.Greyscale => ReadGreyscale(row, x),
            PngColorType.Truecolour => ReadTruecolour(row, x),
            PngColorType.Indexed => ReadIndexed(row, x),
            PngColorType.GreyscaleAlpha => ReadGreyscaleAlpha(row, x),
            PngColorType.TruecolourAlpha => ReadTruecolourAlpha(row, x),
            _ => throw new ImageFormatException($"Unsupported PNG colour type {(int)header.ColorType}."),
        };

        private PixelColor ReadGreyscale(ReadOnlySpan<byte> row, int x)
        {
            int raw = ReadSample(row, x, 0, channels: 1);
            byte grey = ScaleToByte(raw);
            byte alpha = MatchesColorKey(raw, raw, raw) ? (byte)0 : byte.MaxValue;
            return new PixelColor(grey, grey, grey, alpha);
        }

        private PixelColor ReadGreyscaleAlpha(ReadOnlySpan<byte> row, int x)
        {
            byte grey = ScaleToByte(ReadSample(row, x, 0, channels: 2));
            byte alpha = ScaleToByte(ReadSample(row, x, 1, channels: 2));
            return new PixelColor(grey, grey, grey, alpha);
        }

        private PixelColor ReadTruecolour(ReadOnlySpan<byte> row, int x)
        {
            int r = ReadSample(row, x, 0, channels: 3);
            int g = ReadSample(row, x, 1, channels: 3);
            int b = ReadSample(row, x, 2, channels: 3);
            byte alpha = MatchesColorKey(r, g, b) ? (byte)0 : byte.MaxValue;
            return new PixelColor(ScaleToByte(r), ScaleToByte(g), ScaleToByte(b), alpha);
        }

        private PixelColor ReadTruecolourAlpha(ReadOnlySpan<byte> row, int x) => new(
            ScaleToByte(ReadSample(row, x, 0, channels: 4)),
            ScaleToByte(ReadSample(row, x, 1, channels: 4)),
            ScaleToByte(ReadSample(row, x, 2, channels: 4)),
            ScaleToByte(ReadSample(row, x, 3, channels: 4)));

        private PixelColor ReadIndexed(ReadOnlySpan<byte> row, int x)
        {
            if (palette is null)
            {
                throw new ImageFormatException("The PNG file is indexed but carries no PLTE palette chunk.");
            }

            int index = ReadSample(row, x, 0, channels: 1);
            int offset = index * 3;
            if (offset + 2 >= palette.Length)
            {
                throw new ImageFormatException($"A pixel references palette entry {index}, which does not exist.");
            }

            byte alpha = paletteAlpha is not null && index < paletteAlpha.Length
                ? paletteAlpha[index]
                : byte.MaxValue;

            return new PixelColor(palette[offset], palette[offset + 1], palette[offset + 2], alpha);
        }

        /// <summary>
        /// Reads one channel of one pixel, unpacking sub-byte bit depths. Indexed images in
        /// particular are frequently stored at 1, 2 or 4 bits per pixel.
        /// </summary>
        private int ReadSample(ReadOnlySpan<byte> row, int x, int channel, int channels)
        {
            switch (header.BitDepth)
            {
                case 16:
                {
                    int offset = ((x * channels) + channel) * 2;
                    return BinaryPrimitives.ReadUInt16BigEndian(row[offset..]);
                }

                case 8:
                    return row[(x * channels) + channel];

                default:
                {
                    // Sub-byte samples are packed most-significant-bit first.
                    int bitsPerSample = header.BitDepth;
                    int sampleIndex = (x * channels) + channel;
                    int bitPosition = sampleIndex * bitsPerSample;
                    int byteIndex = bitPosition / 8;
                    int shift = 8 - bitsPerSample - (bitPosition % 8);
                    int mask = (1 << bitsPerSample) - 1;
                    return (row[byteIndex] >> shift) & mask;
                }
            }
        }

        /// <summary>
        /// Expands a sample of the file's bit depth to the full 0-255 range. Indexed images
        /// are excluded because their samples are palette indices, not intensities.
        /// </summary>
        private byte ScaleToByte(int sample) => header.BitDepth switch
        {
            16 => (byte)(sample >> 8),
            8 => (byte)sample,
            4 => (byte)(sample * 17),
            2 => (byte)(sample * 85),
            1 => sample != 0 ? byte.MaxValue : (byte)0,
            _ => (byte)sample,
        };

        private bool MatchesColorKey(int r, int g, int b)
        {
            if (colorKey is null)
            {
                return false;
            }

            if (header.ColorType == PngColorType.Greyscale && colorKey.Length >= 2)
            {
                return BinaryPrimitives.ReadUInt16BigEndian(colorKey) == r;
            }

            if (header.ColorType == PngColorType.Truecolour && colorKey.Length >= 6)
            {
                return BinaryPrimitives.ReadUInt16BigEndian(colorKey) == r
                    && BinaryPrimitives.ReadUInt16BigEndian(colorKey.AsSpan(2)) == g
                    && BinaryPrimitives.ReadUInt16BigEndian(colorKey.AsSpan(4)) == b;
            }

            return false;
        }
    }
}
