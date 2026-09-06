using System.Buffers.Binary;
using System.Text;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;

namespace XPXel.Formats.Gif;

/// <summary>
/// Encodes a sequence of frames as an animated GIF.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written in managed code, in the same spirit and for the same reasons as the PNG
/// codec: a native imaging library would mean per-platform binaries, which is exactly what
/// breaks the WebAssembly build. GIF89a is a small, completely specified format, and .NET
/// supplies nothing that writes one.
/// </para>
/// <para>
/// <b>What a GIF costs.</b> At most 256 colours, shared across the whole animation, and no
/// partial transparency — a pixel is either fully there or fully gone. Flat pixel art usually
/// loses nothing at all; soft brush work and gradients do. That trade is the entire reason
/// APNG is offered beside it.
/// </para>
/// <para>
/// <b>Timing is coarser than the editor's.</b> GIF stores each delay in hundredths of a
/// second, so a 100 ms frame is exact but a 33 ms one is not. Delays are rounded to the
/// nearest centisecond and never to zero, because a zero delay is interpreted by browsers as
/// "as fast as possible" and turns a deliberate animation into a flicker.
/// </para>
/// </remarks>
internal static class GifEncoder
{
    /// <summary>The smallest delay that will be written, in centiseconds.</summary>
    /// <remarks>
    /// Two, not one. Historically browsers clamped anything under 20 ms up to 100 ms; modern
    /// ones clamp to 20 ms, and writing 2 keeps the animation playing at the speed it was
    /// authored at rather than at ten times slower on older viewers.
    /// </remarks>
    private const int MinimumDelayCentiseconds = 2;

    /// <summary>Writes an animated GIF.</summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="frames">The frames, all the same size.</param>
    /// <param name="delaysMilliseconds">How long each frame is held.</param>
    /// <param name="loopCount">How many times to play, or 0 for forever.</param>
    /// <exception cref="ImageFormatException">There is nothing to write, or the frames disagree on size.</exception>
    public static void Encode(
        Stream stream,
        IReadOnlyList<PixelBuffer> frames,
        IReadOnlyList<int> delaysMilliseconds,
        int loopCount = 0)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(delaysMilliseconds);

        if (frames.Count == 0)
        {
            throw new ImageFormatException("A GIF needs at least one frame.");
        }

        ImageSize size = frames[0].Size;
        if (frames.Any(f => f.Size != size))
        {
            throw new ImageFormatException("Every frame of a GIF must be the same size.");
        }

        if (size.Width > ushort.MaxValue || size.Height > ushort.MaxValue)
        {
            throw new ImageFormatException($"A GIF cannot be larger than {ushort.MaxValue} pixels on a side.");
        }

        GifPalette palette = GifPalette.Build(frames);

        WriteHeader(stream, size, palette);
        WriteLoopExtension(stream, loopCount);

        for (int i = 0; i < frames.Count; i++)
        {
            int delay = i < delaysMilliseconds.Count ? delaysMilliseconds[i] : 100;
            WriteFrame(stream, frames[i], size, palette, delay);
        }

        stream.WriteByte(0x3B); // trailer
    }

    private static void WriteHeader(Stream stream, ImageSize size, GifPalette palette)
    {
        stream.Write("GIF89a"u8);

        Span<byte> screen = stackalloc byte[7];
        BinaryPrimitives.WriteUInt16LittleEndian(screen, (ushort)size.Width);
        BinaryPrimitives.WriteUInt16LittleEndian(screen[2..], (ushort)size.Height);

        // Bit 7: a global colour table follows. Bits 0-2: its size, as a power of two minus
        // one. The resolution and sort bits in between are ignored by every decoder.
        screen[4] = (byte)(0x80 | (palette.BitsPerColor - 1));
        screen[5] = 0; // background colour index
        screen[6] = 0; // pixel aspect ratio: square

        stream.Write(screen);
        WriteColorTable(stream, palette);
    }

    private static void WriteColorTable(Stream stream, GifPalette palette)
    {
        // The table is always a full power of two, padded with black beyond the colours that
        // are actually used; a decoder reads exactly that many entries.
        byte[] table = new byte[palette.TableSize * 3];

        for (int i = 0; i < palette.Colors.Count; i++)
        {
            PixelColor color = palette.Colors[i];
            table[i * 3] = color.R;
            table[(i * 3) + 1] = color.G;
            table[(i * 3) + 2] = color.B;
        }

        stream.Write(table);
    }

    /// <summary>
    /// Writes the NETSCAPE2.0 application extension, which is how a GIF says it loops.
    /// </summary>
    /// <remarks>
    /// Looping was never part of the GIF specification. It arrived as a vendor extension in
    /// Netscape Navigator and became universal by adoption, which is why a block that means
    /// "repeat forever" is spelled as an application identifier and a magic sub-block.
    /// </remarks>
    private static void WriteLoopExtension(Stream stream, int loopCount)
    {
        stream.WriteByte(0x21); // extension introducer
        stream.WriteByte(0xFF); // application extension
        stream.WriteByte(11);   // block size: identifier plus authentication code
        stream.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));

        stream.WriteByte(3);    // sub-block size
        stream.WriteByte(1);    // sub-block id: looping

        Span<byte> repeats = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(repeats, (ushort)Math.Clamp(loopCount, 0, ushort.MaxValue));
        stream.Write(repeats);

        stream.WriteByte(0);    // block terminator
    }

    private static void WriteFrame(
        Stream stream,
        PixelBuffer frame,
        ImageSize size,
        GifPalette palette,
        int delayMilliseconds)
    {
        WriteGraphicControl(stream, palette, delayMilliseconds);

        Span<byte> descriptor = stackalloc byte[9];
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor, 0);                  // left
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor[2..], 0);             // top
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor[4..], (ushort)size.Width);
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor[6..], (ushort)size.Height);
        descriptor[8] = 0; // no local colour table, not interlaced

        stream.WriteByte(0x2C); // image separator
        stream.Write(descriptor);

        byte[] indices = MapToIndices(frame, palette);
        GifLzw.Compress(stream, indices, palette.BitsPerColor);
    }

    private static void WriteGraphicControl(Stream stream, GifPalette palette, int delayMilliseconds)
    {
        int centiseconds = Math.Max(MinimumDelayCentiseconds, (int)Math.Round(delayMilliseconds / 10.0));

        stream.WriteByte(0x21); // extension introducer
        stream.WriteByte(0xF9); // graphic control extension
        stream.WriteByte(4);    // block size

        // Disposal method 2 — restore to background — clears the frame before the next is
        // drawn. Without it a transparent pixel shows whatever the previous frame left there,
        // so a character moving across the canvas smears instead of moving.
        stream.WriteByte(0b0000_1001); // disposal 2, transparency flag set

        Span<byte> delay = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(delay, (ushort)Math.Min(centiseconds, ushort.MaxValue));
        stream.Write(delay);

        stream.WriteByte((byte)palette.TransparentIndex);
        stream.WriteByte(0); // block terminator
    }

    private static byte[] MapToIndices(PixelBuffer frame, GifPalette palette)
    {
        ReadOnlySpan<byte> pixels = frame.AsReadOnlySpan();
        byte[] indices = new byte[frame.Width * frame.Height];

        for (int i = 0, p = 0; p < pixels.Length; i++, p += PixelBuffer.BytesPerPixel)
        {
            indices[i] = (byte)palette.IndexOf(
                new PixelColor(pixels[p], pixels[p + 1], pixels[p + 2], pixels[p + 3]));
        }

        return indices;
    }
}
