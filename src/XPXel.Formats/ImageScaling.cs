using XPXel.Core.Imaging;
using XPXel.Core.Primitives;

namespace XPXel.Formats;

/// <summary>
/// Integer magnification for export.
/// </summary>
/// <remarks>
/// Nearest-neighbour is not a shortcut here, it is the only correct choice: every source
/// pixel must become an exact square block of identical pixels. Any interpolating filter
/// would blur the hard edges that are the entire point of the medium. The scale factor is
/// restricted to whole numbers for the same reason — a fractional scale cannot map pixels to
/// pixels evenly and always produces uneven block sizes.
/// </remarks>
internal static class ImageScaling
{
    /// <summary>
    /// Returns a magnified copy of an image, or the original when no scaling is needed.
    /// </summary>
    /// <param name="source">The image to magnify.</param>
    /// <param name="factor">The whole-number scale factor; 1 returns the source unchanged.</param>
    /// <returns>The magnified image.</returns>
    public static PixelBuffer ScaleNearestNeighbour(PixelBuffer source, int factor)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (factor <= 1)
        {
            return source;
        }

        PixelBuffer result = new(new ImageSize(source.Width * factor, source.Height * factor));

        for (int y = 0; y < source.Height; y++)
        {
            ReadOnlySpan<byte> sourceRow = source.GetReadOnlyRowSpan(y);

            // Build the first magnified row, then copy it to the remaining rows of the block
            // rather than recomputing the horizontal expansion factor times over.
            Span<byte> firstTargetRow = result.GetRowSpan(y * factor);
            for (int x = 0; x < source.Width; x++)
            {
                ReadOnlySpan<byte> pixel = sourceRow.Slice(x * PixelBuffer.BytesPerPixel, PixelBuffer.BytesPerPixel);
                for (int repeat = 0; repeat < factor; repeat++)
                {
                    int offset = ((x * factor) + repeat) * PixelBuffer.BytesPerPixel;
                    pixel.CopyTo(firstTargetRow[offset..]);
                }
            }

            for (int repeat = 1; repeat < factor; repeat++)
            {
                firstTargetRow.CopyTo(result.GetRowSpan((y * factor) + repeat));
            }
        }

        return result;
    }
}
