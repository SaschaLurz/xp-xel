using XPXel.Core.Imaging;
using XPXel.Core.Primitives;

namespace XPXel.Formats.Tests;

/// <summary>
/// A minimal GIF89a decoder, written for the tests alone.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the tests carry a decoder.</b> An LZW stream with a wrong code width or a
/// mis-framed sub-block does not fail loudly — it decodes into plausible-looking garbage, or
/// opens in one viewer and not another. Asserting on the encoder's bytes would only re-state
/// the encoder; decoding them back and comparing pixels is the only check that actually
/// proves the format was written correctly.
/// </para>
/// <para>
/// It is deliberately written from the specification rather than by mirroring the encoder,
/// because a decoder that shares the encoder's misunderstanding would agree with it happily.
/// The one place that matters most is the code width: the decoder builds its dictionary one
/// entry behind the encoder, so it must widen its codes when the next code reaches the
/// current limit, where the encoder widens when it passes it. Getting that wrong desynchronises
/// the two a few hundred pixels in.
/// </para>
/// <para>
/// It handles only what the encoder writes: a global colour table, no interlacing, no local
/// tables. Anything else throws rather than guessing.
/// </para>
/// </remarks>
internal static class TestGifDecoder
{
    /// <summary>Decodes every frame of an animated GIF.</summary>
    /// <param name="content">The file's bytes.</param>
    /// <returns>The frames, in file order.</returns>
    public static IReadOnlyList<PixelBuffer> Decode(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        int position = 0;

        string signature = System.Text.Encoding.ASCII.GetString(content, 0, 6);
        if (signature is not ("GIF89a" or "GIF87a"))
        {
            throw new InvalidOperationException($"Not a GIF: signature was '{signature}'.");
        }

        position += 6;

        int width = ReadUInt16(content, ref position);
        int height = ReadUInt16(content, ref position);
        byte packed = content[position++];
        position++; // background colour index
        position++; // pixel aspect ratio

        PixelColor[] palette = [];

        if ((packed & 0x80) != 0)
        {
            int size = 1 << ((packed & 0x07) + 1);
            palette = ReadColorTable(content, ref position, size);
        }

        List<PixelBuffer> frames = [];
        int transparentIndex = -1;

        while (position < content.Length)
        {
            byte block = content[position++];

            if (block == 0x3B)
            {
                break;
            }

            if (block == 0x21)
            {
                byte label = content[position++];

                if (label == 0xF9)
                {
                    position++; // block size, always 4
                    byte flags = content[position++];
                    position += 2; // delay
                    byte index = content[position++];
                    position++; // block terminator

                    transparentIndex = (flags & 0x01) != 0 ? index : -1;
                }
                else
                {
                    SkipSubBlocks(content, ref position);
                }

                continue;
            }

            if (block != 0x2C)
            {
                throw new InvalidOperationException($"Unexpected GIF block 0x{block:X2} at {position - 1}.");
            }

            position += 4; // left and top, always zero here
            int frameWidth = ReadUInt16(content, ref position);
            int frameHeight = ReadUInt16(content, ref position);
            byte imageFlags = content[position++];

            if ((imageFlags & 0x80) != 0 || (imageFlags & 0x40) != 0)
            {
                throw new InvalidOperationException("Local colour tables and interlacing are not decoded.");
            }

            byte[] indices = DecodeImageData(content, ref position, frameWidth * frameHeight);
            frames.Add(BuildFrame(indices, frameWidth, frameHeight, palette, transparentIndex));
        }

        _ = width;
        _ = height;

        return frames;
    }

    private static PixelBuffer BuildFrame(
        byte[] indices,
        int width,
        int height,
        PixelColor[] palette,
        int transparentIndex)
    {
        PixelBuffer buffer = new(width, height);

        for (int i = 0; i < indices.Length; i++)
        {
            int index = indices[i];
            if (index == transparentIndex || index >= palette.Length)
            {
                continue;
            }

            buffer.SetPixel(i % width, i / width, palette[index]);
        }

        return buffer;
    }

    private static PixelColor[] ReadColorTable(byte[] content, ref int position, int size)
    {
        PixelColor[] table = new PixelColor[size];

        for (int i = 0; i < size; i++)
        {
            table[i] = new PixelColor(content[position], content[position + 1], content[position + 2]);
            position += 3;
        }

        return table;
    }

    /// <summary>Reassembles the sub-blocks of one image and runs LZW over them.</summary>
    private static byte[] DecodeImageData(byte[] content, ref int position, int pixelCount)
    {
        int minimumCodeSize = content[position++];

        List<byte> compressed = [];

        while (true)
        {
            int length = content[position++];
            if (length == 0)
            {
                break;
            }

            compressed.AddRange(content.AsSpan(position, length).ToArray());
            position += length;
        }

        return Inflate([.. compressed], minimumCodeSize, pixelCount);
    }

    private static byte[] Inflate(byte[] data, int minimumCodeSize, int pixelCount)
    {
        int clearCode = 1 << minimumCodeSize;
        int endCode = clearCode + 1;

        List<byte[]> dictionary = [];
        void Reset()
        {
            dictionary.Clear();
            for (int i = 0; i < clearCode; i++)
            {
                dictionary.Add([(byte)i]);
            }

            dictionary.Add([]); // clear
            dictionary.Add([]); // end
        }

        Reset();

        int codeSize = minimumCodeSize + 1;
        List<byte> output = new(pixelCount);

        int bitPosition = 0;
        byte[]? previous = null;

        while (output.Count < pixelCount)
        {
            int code = ReadCode(data, ref bitPosition, codeSize);
            if (code < 0)
            {
                break;
            }

            if (code == clearCode)
            {
                Reset();
                codeSize = minimumCodeSize + 1;
                previous = null;
                continue;
            }

            if (code == endCode)
            {
                break;
            }

            byte[] entry;

            if (code < dictionary.Count)
            {
                entry = dictionary[code];
            }
            else if (previous is not null)
            {
                // The encoder is allowed to emit a code it has only just defined, which the
                // decoder has not seen yet. The entry is always the previous one plus its own
                // first byte.
                entry = [.. previous, previous[0]];
            }
            else
            {
                throw new InvalidOperationException("The LZW stream starts with an undefined code.");
            }

            output.AddRange(entry);

            if (previous is not null && dictionary.Count < 4096)
            {
                dictionary.Add([.. previous, entry[0]]);

                // One entry behind the encoder, so the widening test is "the next code has
                // reached the limit", not "has passed it".
                if (dictionary.Count >= 1 << codeSize && codeSize < 12)
                {
                    codeSize++;
                }
            }

            previous = entry;
        }

        return [.. output.Take(pixelCount)];
    }

    /// <summary>Reads one code, least significant bit first, packed across byte boundaries.</summary>
    private static int ReadCode(byte[] data, ref int bitPosition, int codeSize)
    {
        int code = 0;

        for (int i = 0; i < codeSize; i++)
        {
            int byteIndex = bitPosition >> 3;
            if (byteIndex >= data.Length)
            {
                return -1;
            }

            int bit = (data[byteIndex] >> (bitPosition & 7)) & 1;
            code |= bit << i;
            bitPosition++;
        }

        return code;
    }

    private static void SkipSubBlocks(byte[] content, ref int position)
    {
        while (true)
        {
            int length = content[position++];
            if (length == 0)
            {
                return;
            }

            position += length;
        }
    }

    private static int ReadUInt16(byte[] content, ref int position)
    {
        int value = content[position] | (content[position + 1] << 8);
        position += 2;
        return value;
    }
}
