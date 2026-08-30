using System.Buffers.Binary;
using System.Text;

namespace XPXel.Formats.Png;

/// <summary>
/// Low-level PNG container plumbing: the file signature, chunk framing, and the CRC-32 that
/// every chunk carries.
/// </summary>
/// <remarks>
/// <para>
/// A PNG file is an 8-byte signature followed by a sequence of chunks, each of which is a
/// big-endian 32-bit length, a four-character type, that many bytes of data, and a
/// big-endian CRC-32 over the type and data. Everything above this file — image headers,
/// palettes, pixel data — is expressed in terms of those chunks.
/// </para>
/// <para>
/// Keeping the framing separate from the image semantics is what will make optional chunks
/// easy to add later: animation (<c>acTL</c>/<c>fcTL</c>/<c>fdAT</c>, the APNG extension)
/// and text metadata (<c>tEXt</c>) both slot in as new chunk types without touching the
/// reader or writer here.
/// </para>
/// </remarks>
internal static class PngChunkIO
{
    /// <summary>
    /// The eight bytes every PNG file begins with. The non-ASCII first byte and the
    /// CR/LF/CTRL-Z sequence exist to detect files damaged by text-mode transfers.
    /// </summary>
    public static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Represents one chunk read from a stream.</summary>
    /// <param name="Type">The four-character chunk type, such as <c>IHDR</c>.</param>
    /// <param name="Data">The chunk's payload.</param>
    public readonly record struct PngChunk(string Type, byte[] Data);

    /// <summary>Determines whether the given bytes begin with the PNG signature.</summary>
    /// <param name="header">The candidate leading bytes.</param>
    /// <returns><see langword="true"/> if the content is a PNG.</returns>
    public static bool HasSignature(ReadOnlySpan<byte> header) =>
        header.Length >= Signature.Length && header[..Signature.Length].SequenceEqual(Signature);

    /// <summary>Reads and validates the file signature.</summary>
    /// <param name="stream">The stream to read from.</param>
    /// <exception cref="ImageFormatException">The signature is missing or wrong.</exception>
    public static void ReadSignature(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadExactly(stream, buffer);

        if (!HasSignature(buffer))
        {
            throw new ImageFormatException("The file does not start with a PNG signature.");
        }
    }

    /// <summary>Writes the file signature.</summary>
    /// <param name="stream">The stream to write to.</param>
    public static void WriteSignature(Stream stream) => stream.Write(Signature);

    /// <summary>Reads the next chunk.</summary>
    /// <param name="stream">The stream to read from.</param>
    /// <returns>The chunk.</returns>
    /// <exception cref="ImageFormatException">The chunk is truncated or its CRC does not match.</exception>
    public static PngChunk ReadChunk(Stream stream)
    {
        Span<byte> header = stackalloc byte[8];
        ReadExactly(stream, header);

        uint length = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);
        if (length > int.MaxValue)
        {
            throw new ImageFormatException($"A PNG chunk declares an implausible length of {length} bytes.");
        }

        string type = Encoding.ASCII.GetString(header[4..8]);
        byte[] data = new byte[length];
        ReadExactly(stream, data);

        Span<byte> crcBytes = stackalloc byte[4];
        ReadExactly(stream, crcBytes);
        uint expected = BinaryPrimitives.ReadUInt32BigEndian(crcBytes);

        uint actual = Crc32(header[4..8], data);
        if (actual != expected)
        {
            throw new ImageFormatException(
                $"The '{type}' chunk is corrupt: expected CRC {expected:X8} but computed {actual:X8}.");
        }

        return new PngChunk(type, data);
    }

    /// <summary>Writes a chunk, computing its CRC.</summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="type">The four-character chunk type.</param>
    /// <param name="data">The chunk payload.</param>
    public static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> typeBytes = stackalloc byte[4];
        Encoding.ASCII.GetBytes(type, typeBytes);

        Span<byte> lengthBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lengthBytes, (uint)data.Length);
        stream.Write(lengthBytes);
        stream.Write(typeBytes);
        stream.Write(data);

        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, Crc32(typeBytes, data));
        stream.Write(crcBytes);
    }

    /// <summary>Computes the PNG CRC-32 over a chunk's type followed by its data.</summary>
    /// <param name="type">The four type bytes.</param>
    /// <param name="data">The chunk payload.</param>
    /// <returns>The CRC-32 value.</returns>
    public static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        crc = Accumulate(crc, type);
        crc = Accumulate(crc, data);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint Accumulate(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    /// <summary>
    /// Builds the standard CRC-32 lookup table for the reversed polynomial 0xEDB88320,
    /// exactly as the PNG specification's reference implementation does.
    /// </summary>
    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];

        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        try
        {
            stream.ReadExactly(buffer);
        }
        catch (EndOfStreamException ex)
        {
            throw new ImageFormatException("The PNG file ends unexpectedly.", ex);
        }
    }
}
