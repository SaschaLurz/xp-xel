namespace XPXel.Formats.Gif;

/// <summary>
/// GIF's variable-width LZW compressor.
/// </summary>
/// <remarks>
/// <para>
/// LZW builds a dictionary as it goes: it reads the longest run of pixels it already has a
/// code for, emits that code, and adds the run plus one more pixel as a new entry. Both ends
/// build the same dictionary from the same data, so nothing about it is transmitted. On flat
/// pixel art, where long runs of one colour are the norm, it compresses extremely well.
/// </para>
/// <para>
/// <b>Three details make GIF's variant its own.</b> Codes are written least-significant-bit
/// first, packed across byte boundaries. The code width grows from the colour depth upward as
/// the dictionary fills, and the decoder grows it in lockstep. And the output is chopped into
/// sub-blocks of at most 255 bytes, each introduced by its own length, terminated by a zero.
/// Getting any of the three wrong produces a file that opens as a garbled mess rather than an
/// error, which is why they are called out here.
/// </para>
/// </remarks>
internal static class GifLzw
{
    /// <summary>The largest code the dictionary may reach before it must be reset.</summary>
    private const int MaximumCode = 4095;

    /// <summary>The widest a code may get, which is what <see cref="MaximumCode"/> needs.</summary>
    private const int MaximumCodeBits = 12;

    /// <summary>Compresses palette indices into a GIF image data block.</summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="indices">One palette index per pixel, in row order.</param>
    /// <param name="bitsPerColor">The palette's depth, which sets the starting code width.</param>
    public static void Compress(Stream stream, ReadOnlySpan<byte> indices, int bitsPerColor)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // The minimum code size is at least 2 even for a two-colour image: a one-bit code
        // leaves no room for the clear and end markers.
        int minimumCodeSize = Math.Max(2, bitsPerColor);
        int clearCode = 1 << minimumCodeSize;
        int endCode = clearCode + 1;

        stream.WriteByte((byte)minimumCodeSize);

        using SubBlockWriter output = new(stream);
        BitWriter bits = new(output);

        Dictionary<(int Prefix, byte Suffix), int> dictionary = [];
        int nextCode = endCode + 1;
        int codeWidth = minimumCodeSize + 1;

        bits.Write(clearCode, codeWidth);

        if (indices.Length == 0)
        {
            bits.Write(endCode, codeWidth);
            bits.Flush();
            return;
        }

        int current = indices[0];

        for (int i = 1; i < indices.Length; i++)
        {
            byte next = indices[i];

            if (dictionary.TryGetValue((current, next), out int existing))
            {
                current = existing;
                continue;
            }

            bits.Write(current, codeWidth);

            if (nextCode <= MaximumCode)
            {
                dictionary[(current, next)] = nextCode++;

                // The width grows the moment the dictionary outgrows it, and the decoder does
                // the same on its own count — nothing signals it.
                if (nextCode > 1 << codeWidth && codeWidth < MaximumCodeBits)
                {
                    codeWidth++;
                }
            }
            else
            {
                // The dictionary is full. Telling the decoder to reset is the only way on,
                // and it costs less than continuing with a dictionary that can no longer
                // learn anything about the rest of the image.
                bits.Write(clearCode, codeWidth);
                dictionary.Clear();
                nextCode = endCode + 1;
                codeWidth = minimumCodeSize + 1;
            }

            current = next;
        }

        bits.Write(current, codeWidth);
        bits.Write(endCode, codeWidth);
        bits.Flush();
    }

    /// <summary>Packs codes of varying width into a byte stream, least significant bit first.</summary>
    private sealed class BitWriter(SubBlockWriter output)
    {
        private int _buffer;
        private int _count;

        public void Write(int code, int width)
        {
            _buffer |= code << _count;
            _count += width;

            while (_count >= 8)
            {
                output.WriteByte((byte)(_buffer & 0xFF));
                _buffer >>= 8;
                _count -= 8;
            }
        }

        public void Flush()
        {
            if (_count > 0)
            {
                output.WriteByte((byte)(_buffer & 0xFF));
                _buffer = 0;
                _count = 0;
            }

            output.Finish();
        }
    }

    /// <summary>
    /// Buffers bytes into GIF's length-prefixed sub-blocks of at most 255 bytes.
    /// </summary>
    private sealed class SubBlockWriter(Stream stream) : IDisposable
    {
        private readonly byte[] _block = new byte[255];
        private int _length;
        private bool _finished;

        public void WriteByte(byte value)
        {
            _block[_length++] = value;

            if (_length == _block.Length)
            {
                FlushBlock();
            }
        }

        /// <summary>Writes any partial block and the terminating zero.</summary>
        public void Finish()
        {
            if (_finished)
            {
                return;
            }

            FlushBlock();
            stream.WriteByte(0);
            _finished = true;
        }

        public void Dispose() => Finish();

        private void FlushBlock()
        {
            if (_length == 0)
            {
                return;
            }

            stream.WriteByte((byte)_length);
            stream.Write(_block, 0, _length);
            _length = 0;
        }
    }
}
