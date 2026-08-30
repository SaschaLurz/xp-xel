using Avalonia.Platform.Storage;
using XPXel.Core.Documents;
using XPXel.Formats;

namespace XPXel.UI.Services;

/// <summary>
/// Reads and writes documents through Avalonia's storage abstraction, choosing the codec
/// from the format registry.
/// </summary>
/// <remarks>
/// <para>
/// The service exists so that view models never touch a codec, a stream or a file handle
/// directly. Adding a format therefore changes nothing here, and neither does the difference
/// between a desktop file path and a browser file handle.
/// </para>
/// <para>
/// <b>Why writes are buffered through memory.</b> The stream a storage file hands back is
/// not guaranteed to be seekable, and is not guaranteed to truncate an existing file. Both
/// matter: a zip archive is written with backwards seeks, and saving a smaller document over
/// a larger one would otherwise leave trailing rubbish that makes the file unreadable.
/// Serialising into memory first sidesteps both, at a cost that is irrelevant for documents
/// of this size.
/// </para>
/// </remarks>
public sealed class DocumentFileService
{
    private readonly ImageFormatRegistry _registry;

    /// <summary>Initialises the service.</summary>
    /// <param name="registry">The available codecs.</param>
    public DocumentFileService(ImageFormatRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <summary>The formats that can be opened, for building a file dialog's filters.</summary>
    public IReadOnlyList<FormatDescriptor> ImportableFormats =>
        [.. _registry.Importers.Select(i => i.Descriptor)];

    /// <summary>The formats that can be exported to.</summary>
    public IReadOnlyList<FormatDescriptor> ExportableFormats =>
        [.. _registry.Exporters.Select(e => e.Descriptor)];

    /// <summary>The formats that preserve a complete document, offered when saving.</summary>
    public IReadOnlyList<FormatDescriptor> ProjectFormats =>
        [.. _registry.Exporters.Select(e => e.Descriptor).Where(d => d.SupportsLayers)];

    /// <summary>Reads a document from a chosen file.</summary>
    /// <param name="file">The file to read.</param>
    /// <returns>The loaded document.</returns>
    /// <exception cref="ImageFormatException">The file is of an unknown or unreadable format.</exception>
    public async Task<PixelDocument> ReadAsync(IStorageFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        // Buffer the whole file: identifying a format means reading the leading bytes and
        // then rewinding, and a stream from a browser file handle cannot necessarily seek.
        using MemoryStream buffer = new();
        await using (Stream source = await file.OpenReadAsync().ConfigureAwait(true))
        {
            await source.CopyToAsync(buffer).ConfigureAwait(true);
        }

        buffer.Position = 0;

        IImageImporter importer = _registry.ResolveImporter(file.Name, buffer)
            ?? throw new ImageFormatException(
                $"'{file.Name}' is not a file type XP-xel can open.");

        buffer.Position = 0;
        return importer.Read(buffer, Path.GetFileNameWithoutExtension(file.Name));
    }

    /// <summary>Writes a document to a chosen file, picking the codec from its extension.</summary>
    /// <param name="file">The destination.</param>
    /// <param name="document">The document to write.</param>
    /// <param name="options">How to export.</param>
    /// <exception cref="ImageFormatException">No codec claims the destination's extension.</exception>
    public async Task WriteAsync(IStorageFile file, PixelDocument document, ExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(document);

        IImageExporter exporter = _registry.FindExporterByPath(file.Name)
            ?? throw new ImageFormatException(
                $"XP-xel does not know how to write '{Path.GetExtension(file.Name)}' files.");

        using MemoryStream buffer = new();
        exporter.Write(buffer, document, options);
        buffer.Position = 0;

        await using Stream destination = await file.OpenWriteAsync().ConfigureAwait(true);

        if (destination.CanSeek)
        {
            destination.SetLength(0);
        }

        await buffer.CopyToAsync(destination).ConfigureAwait(true);
    }

    /// <summary>
    /// Determines whether a file name denotes a complete project rather than a flat image,
    /// which is what decides between "Save" and "Export".
    /// </summary>
    /// <param name="fileName">The file name to inspect.</param>
    /// <returns><see langword="true"/> if the format preserves layers.</returns>
    public bool IsProjectFile(string fileName) =>
        _registry.FindExporterByPath(fileName)?.Descriptor.SupportsLayers ?? false;
}
