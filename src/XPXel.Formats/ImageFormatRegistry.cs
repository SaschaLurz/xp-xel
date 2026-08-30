namespace XPXel.Formats;

/// <summary>
/// The set of file formats XP-xel can read and write.
/// </summary>
/// <remarks>
/// <para>
/// This is the growth mechanism the whole format layer is built around. Nothing outside
/// this project names a concrete codec: the application asks the registry which importer
/// handles a path, and asks it to enumerate formats when building a file dialog's filters.
/// Supporting GIF, BMP, JPEG or Aseprite files is therefore one new class plus one
/// registration line — no changes to menus, dialogs, or the open and save code paths.
/// </para>
/// <para>
/// Lookup is by extension first, because that is what the artist chose in the save dialog,
/// and by content sniffing when opening, because file extensions are frequently wrong.
/// </para>
/// </remarks>
public sealed class ImageFormatRegistry
{
    /// <summary>
    /// How many leading bytes are read for content sniffing. Comfortably more than any
    /// signature this project is likely to add.
    /// </summary>
    public const int SniffLength = 64;

    private readonly IImageImporter[] _importers;
    private readonly IImageExporter[] _exporters;

    /// <summary>Initialises the registry.</summary>
    /// <param name="importers">The available importers, in file-dialog order.</param>
    /// <param name="exporters">The available exporters, in file-dialog order.</param>
    public ImageFormatRegistry(IEnumerable<IImageImporter> importers, IEnumerable<IImageExporter> exporters)
    {
        ArgumentNullException.ThrowIfNull(importers);
        ArgumentNullException.ThrowIfNull(exporters);

        _importers = [.. importers];
        _exporters = [.. exporters];
    }

    /// <summary>Every registered importer.</summary>
    public IReadOnlyList<IImageImporter> Importers => _importers;

    /// <summary>Every registered exporter.</summary>
    public IReadOnlyList<IImageExporter> Exporters => _exporters;

    /// <summary>Finds the importer whose format claims the given file extension.</summary>
    /// <param name="path">The file name or full path.</param>
    /// <returns>The importer, or <see langword="null"/> if the extension is unknown.</returns>
    public IImageImporter? FindImporterByPath(string path)
    {
        foreach (IImageImporter importer in _importers)
        {
            if (importer.Descriptor.MatchesPath(path))
            {
                return importer;
            }
        }

        return null;
    }

    /// <summary>Finds the exporter whose format claims the given file extension.</summary>
    /// <param name="path">The file name or full path.</param>
    /// <returns>The exporter, or <see langword="null"/> if the extension is unknown.</returns>
    public IImageExporter? FindExporterByPath(string path)
    {
        foreach (IImageExporter exporter in _exporters)
        {
            if (exporter.Descriptor.MatchesPath(path))
            {
                return exporter;
            }
        }

        return null;
    }

    /// <summary>
    /// Identifies a stream's format from its leading bytes and rewinds the stream.
    /// </summary>
    /// <param name="stream">A seekable stream positioned at the start of the file.</param>
    /// <returns>The importer that recognised the content, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException">The stream cannot seek.</exception>
    public IImageImporter? FindImporterByContent(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
        {
            throw new ArgumentException("Content sniffing requires a seekable stream.", nameof(stream));
        }

        long origin = stream.Position;
        Span<byte> header = stackalloc byte[SniffLength];
        int read = stream.ReadAtLeast(header, SniffLength, throwOnEndOfStream: false);
        stream.Position = origin;

        ReadOnlySpan<byte> actual = header[..read];
        foreach (IImageImporter importer in _importers)
        {
            if (importer.CanRead(actual))
            {
                return importer;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the importer for a file, preferring what the content says over what the
    /// extension claims.
    /// </summary>
    /// <param name="path">The file name or full path, used as a fallback.</param>
    /// <param name="stream">A seekable stream over the file's contents.</param>
    /// <returns>The importer to use, or <see langword="null"/> if the format is unknown.</returns>
    public IImageImporter? ResolveImporter(string path, Stream stream) =>
        FindImporterByContent(stream) ?? FindImporterByPath(path);
}
