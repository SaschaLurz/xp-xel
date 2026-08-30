using XPXel.Core.Documents;
using XPXel.Core.Imaging;
using XPXel.Core.Rendering;

namespace XPXel.Formats.Png;

/// <summary>
/// Reads and writes PNG files — the format XP-xel ships with, and the one that matters most
/// for exchanging pixel art with game engines and the web.
/// </summary>
/// <remarks>
/// <para>
/// PNG is a flat format: importing produces a document with a single layer, and exporting
/// flattens the layer stack. The descriptor advertises that through
/// <see cref="FormatCapabilities"/> so the UI can warn before layers are lost, rather than
/// this class knowing anything about dialogs.
/// </para>
/// <para>
/// The actual codec lives in <see cref="PngDecoder"/> and <see cref="PngEncoder"/>; this
/// class is the adapter between them and the document model.
/// </para>
/// </remarks>
public sealed class PngCodec : IImageImporter, IImageExporter
{
    /// <summary>The format this codec handles.</summary>
    public static FormatDescriptor Format { get; } = new(
        id: "xpxel.format.png",
        displayName: "PNG Image",
        extensions: [".png"],
        mimeType: "image/png",
        capabilities: FormatCapabilities.Alpha | FormatCapabilities.IndexedColor);

    /// <inheritdoc />
    public FormatDescriptor Descriptor => Format;

    /// <inheritdoc />
    public bool CanRead(ReadOnlySpan<byte> header) => PngChunkIO.HasSignature(header);

    /// <inheritdoc />
    public PixelDocument Read(Stream stream, string documentName)
    {
        ArgumentNullException.ThrowIfNull(stream);

        PixelBuffer image = PngDecoder.Decode(stream);

        PixelDocument document = new(image.Size);
        RasterLayer layer = document.CreateRasterLayer(
            string.IsNullOrWhiteSpace(documentName) ? "Layer 1" : documentName);

        // Hand the decoded buffer straight to the cel rather than copying it: the decoder
        // allocated it for exactly this purpose and nothing else references it.
        document.Cels.Set(new Cel(layer.Id, document.Frames[0].Id, image));

        return document;
    }

    /// <inheritdoc />
    public void Write(Stream stream, PixelDocument document, ExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        Frame frame = options.Frame ?? document.Frames[0];
        PixelBuffer flattened = new LayerCompositor().ComposeToNewBuffer(document, frame);
        PixelBuffer scaled = ImageScaling.ScaleNearestNeighbour(flattened, options.Scale);

        PngEncoder.Encode(stream, scaled);
    }

    /// <summary>
    /// Encodes a single image as PNG, bypassing the document model. Used for thumbnails and
    /// for the per-cel images inside a <c>.xpxel</c> project archive.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="image">The image to encode.</param>
    public static void EncodeImage(Stream stream, PixelBuffer image) => PngEncoder.Encode(stream, image);

    /// <summary>
    /// Decodes a single image from PNG, bypassing the document model.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <returns>The decoded image.</returns>
    public static PixelBuffer DecodeImage(Stream stream) => PngDecoder.Decode(stream);
}
