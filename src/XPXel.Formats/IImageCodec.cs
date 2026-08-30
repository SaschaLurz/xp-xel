using XPXel.Core.Documents;

namespace XPXel.Formats;

/// <summary>
/// Reads a document from a file of one particular format.
/// </summary>
/// <remarks>
/// <para>
/// An importer always produces a whole <see cref="PixelDocument"/>, even for a flat format
/// like PNG, which yields a document with a single layer. That uniformity is what lets the
/// "Open" code path be identical no matter what was opened, and lets a layered format such
/// as Aseprite's slot in later with no change to callers.
/// </para>
/// <para>
/// <b>Adding a format:</b> implement this interface (and usually
/// <see cref="IImageExporter"/> alongside it), then register the class with the dependency
/// injection container. <see cref="ImageFormatRegistry"/> and every file dialog pick it up
/// automatically.
/// </para>
/// </remarks>
public interface IImageImporter
{
    /// <summary>The format this importer reads.</summary>
    FormatDescriptor Descriptor { get; }

    /// <summary>
    /// Determines whether the given leading bytes look like this format.
    /// </summary>
    /// <param name="header">
    /// The first bytes of the file. May be shorter than the format's signature, in which
    /// case the answer must be <see langword="false"/> rather than an exception.
    /// </param>
    /// <returns><see langword="true"/> if the content appears to be this format.</returns>
    /// <remarks>
    /// Content sniffing exists because file extensions lie. It lets XP-xel open a
    /// mis-named file and, more importantly, refuse to mangle one.
    /// </remarks>
    bool CanRead(ReadOnlySpan<byte> header);

    /// <summary>Reads a document from a stream.</summary>
    /// <param name="stream">The stream to read, positioned at the start of the file.</param>
    /// <param name="documentName">
    /// A name for the resulting document's initial layer, normally the file name without
    /// its extension.
    /// </param>
    /// <returns>The imported document.</returns>
    /// <exception cref="ImageFormatException">The data is not valid for this format.</exception>
    PixelDocument Read(Stream stream, string documentName);
}

/// <summary>
/// Writes a document to a file of one particular format.
/// </summary>
public interface IImageExporter
{
    /// <summary>The format this exporter writes.</summary>
    FormatDescriptor Descriptor { get; }

    /// <summary>Writes a document to a stream.</summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="document">The document to write.</param>
    /// <param name="options">How to export; pass <see cref="ExportOptions.Default"/> for the usual case.</param>
    void Write(Stream stream, PixelDocument document, ExportOptions options);
}

/// <summary>
/// Thrown when a file cannot be read because its contents are malformed or use a feature
/// the codec does not implement.
/// </summary>
/// <remarks>
/// Codecs should throw this rather than a general-purpose exception type, so the UI can
/// distinguish "this file is broken or unsupported" — which deserves a friendly message —
/// from a genuine bug.
/// </remarks>
public sealed class ImageFormatException : Exception
{
    /// <summary>Initialises the exception with no message.</summary>
    public ImageFormatException()
    {
    }

    /// <summary>Initialises the exception.</summary>
    /// <param name="message">A message describing what was wrong with the file.</param>
    public ImageFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Initialises the exception.</summary>
    /// <param name="message">A message describing what was wrong with the file.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ImageFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
