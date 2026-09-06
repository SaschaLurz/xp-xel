using System.Buffers.Binary;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;

namespace XPXel.Formats.Png;

/// <summary>
/// Encodes a sequence of frames as an animated PNG.
/// </summary>
/// <remarks>
/// <para>
/// <b>APNG is the cheapest format to add here, and the least lossy.</b> It is not a separate
/// file format at all: it is an ordinary PNG with three extra chunk types, so it inherits the
/// existing encoder's filtering, compression and full 8-bit-per-channel RGBA. Unlike GIF it
/// loses nothing — every colour and every level of transparency survives — and every current
/// browser plays it.
/// </para>
/// <para>
/// <b>The three chunks.</b> <c>acTL</c> announces the frame and loop counts. <c>fcTL</c>
/// precedes each frame with its position, size, delay and disposal. <c>fdAT</c> carries the
/// pixels of every frame after the first; the first frame's pixels live in the ordinary
/// <c>IDAT</c>, which is what makes an APNG display as a still image in a decoder that has
/// never heard of the extension.
/// </para>
/// <para>
/// <b>Sequence numbers are shared between fcTL and fdAT and must increase by exactly one
/// across both.</b> That single counter is the most common thing to get wrong, and a file
/// with a gap in it is rejected outright rather than degraded.
/// </para>
/// </remarks>
internal static class ApngEncoder
{
    /// <summary>Writes an animated PNG.</summary>
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
            throw new ImageFormatException("An animated PNG needs at least one frame.");
        }

        ImageSize size = frames[0].Size;
        if (frames.Any(f => f.Size != size))
        {
            throw new ImageFormatException("Every frame of an animated PNG must be the same size.");
        }

        PngChunkIO.WriteSignature(stream);
        PngEncoder.WriteImageHeader(stream, frames[0]);

        WriteAnimationControl(stream, frames.Count, loopCount);

        int sequence = 0;

        // The first frame is announced by an fcTL and then stored in a plain IDAT, so a
        // decoder that ignores the animation chunks still shows it as a normal PNG.
        WriteFrameControl(stream, sequence++, size, DelayOf(delaysMilliseconds, 0));
        PngChunkIO.WriteChunk(stream, "IDAT", PngEncoder.Compress(frames[0]));

        for (int i = 1; i < frames.Count; i++)
        {
            WriteFrameControl(stream, sequence++, size, DelayOf(delaysMilliseconds, i));
            WriteFrameData(stream, sequence++, frames[i]);
        }

        PngChunkIO.WriteChunk(stream, "IEND", []);
    }

    private static void WriteAnimationControl(Stream stream, int frameCount, int loopCount)
    {
        Span<byte> data = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(data, (uint)frameCount);
        BinaryPrimitives.WriteUInt32BigEndian(data[4..], (uint)Math.Max(0, loopCount));

        PngChunkIO.WriteChunk(stream, "acTL", data);
    }

    private static void WriteFrameControl(Stream stream, int sequence, ImageSize size, int delayMilliseconds)
    {
        Span<byte> data = stackalloc byte[26];

        BinaryPrimitives.WriteUInt32BigEndian(data, (uint)sequence);
        BinaryPrimitives.WriteUInt32BigEndian(data[4..], (uint)size.Width);
        BinaryPrimitives.WriteUInt32BigEndian(data[8..], (uint)size.Height);
        BinaryPrimitives.WriteUInt32BigEndian(data[12..], 0); // x offset
        BinaryPrimitives.WriteUInt32BigEndian(data[16..], 0); // y offset

        // The delay is a rational number of seconds. A denominator of 1000 makes the
        // numerator the duration in milliseconds exactly, so nothing is rounded — which is
        // the practical advantage APNG has over GIF's hundredths of a second.
        BinaryPrimitives.WriteUInt16BigEndian(data[20..], (ushort)Math.Clamp(delayMilliseconds, 1, ushort.MaxValue));
        BinaryPrimitives.WriteUInt16BigEndian(data[22..], 1000);

        // Dispose 1 — clear to transparent before the next frame — because every frame here
        // is a full canvas and must not show through to the one before it.
        data[24] = 1;

        // Blend 0 — replace rather than composite over — for the same reason.
        data[25] = 0;

        PngChunkIO.WriteChunk(stream, "fcTL", data);
    }

    private static void WriteFrameData(Stream stream, int sequence, PixelBuffer frame)
    {
        byte[] compressed = PngEncoder.Compress(frame);

        // An fdAT is a sequence number followed by exactly what an IDAT would have carried.
        byte[] data = new byte[compressed.Length + 4];
        BinaryPrimitives.WriteUInt32BigEndian(data, (uint)sequence);
        compressed.CopyTo(data, 4);

        PngChunkIO.WriteChunk(stream, "fdAT", data);
    }

    private static int DelayOf(IReadOnlyList<int> delays, int index) =>
        index < delays.Count ? Math.Max(1, delays[index]) : 100;
}
