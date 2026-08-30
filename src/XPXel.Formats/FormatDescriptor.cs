namespace XPXel.Formats;

/// <summary>
/// What a file format is able to represent.
/// </summary>
/// <remarks>
/// The UI reads these flags rather than hard-coding knowledge of individual formats. That
/// is how the export dialog can warn "PNG will flatten your 4 layers" without containing
/// the word "PNG" anywhere, and why a new format gets that behaviour for free.
/// </remarks>
[Flags]
public enum FormatCapabilities
{
    /// <summary>A flat, opaque, single-image format.</summary>
    None = 0,

    /// <summary>The format preserves separate layers.</summary>
    Layers = 1,

    /// <summary>The format preserves multiple frames.</summary>
    Animation = 2,

    /// <summary>The format stores a colour palette and indexed pixels.</summary>
    IndexedColor = 4,

    /// <summary>The format stores an alpha channel.</summary>
    Alpha = 8,

    /// <summary>The format preserves arbitrary key/value metadata.</summary>
    Metadata = 16,
}

/// <summary>
/// The identity and abilities of one file format.
/// </summary>
/// <remarks>
/// Every importer and exporter exposes one of these. Together they drive the file dialog
/// filters, the format dropdowns and the lossy-export warnings, so adding a format never
/// requires touching the UI.
/// </remarks>
public sealed class FormatDescriptor
{
    /// <summary>Initialises a descriptor.</summary>
    /// <param name="id">A stable identifier such as <c>"xpxel.format.png"</c>.</param>
    /// <param name="displayName">The name shown in file dialogs, such as "PNG Image".</param>
    /// <param name="extensions">
    /// The file extensions, each including the leading dot. The first is used when a
    /// filename needs one appended.
    /// </param>
    /// <param name="mimeType">The MIME type, used for clipboard and web integration.</param>
    /// <param name="capabilities">What the format can represent.</param>
    public FormatDescriptor(
        string id,
        string displayName,
        IReadOnlyList<string> extensions,
        string mimeType,
        FormatCapabilities capabilities)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(displayName);
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentException.ThrowIfNullOrEmpty(mimeType);

        if (extensions.Count == 0)
        {
            throw new ArgumentException("A format must declare at least one extension.", nameof(extensions));
        }

        Id = id;
        DisplayName = displayName;
        Extensions = extensions;
        MimeType = mimeType;
        Capabilities = capabilities;
    }

    /// <summary>A stable identifier, persisted in settings and never changed once shipped.</summary>
    public string Id { get; }

    /// <summary>The name shown in file dialogs.</summary>
    public string DisplayName { get; }

    /// <summary>The file extensions, each including the leading dot.</summary>
    public IReadOnlyList<string> Extensions { get; }

    /// <summary>The format's primary extension, including the leading dot.</summary>
    public string PrimaryExtension => Extensions[0];

    /// <summary>The MIME type.</summary>
    public string MimeType { get; }

    /// <summary>What the format is able to represent.</summary>
    public FormatCapabilities Capabilities { get; }

    /// <summary>Gets a value indicating whether the format preserves layers.</summary>
    public bool SupportsLayers => Capabilities.HasFlag(FormatCapabilities.Layers);

    /// <summary>Gets a value indicating whether the format preserves multiple frames.</summary>
    public bool SupportsAnimation => Capabilities.HasFlag(FormatCapabilities.Animation);

    /// <summary>Determines whether a file name carries one of this format's extensions.</summary>
    /// <param name="path">The file name or full path to test.</param>
    /// <returns><see langword="true"/> if the extension matches.</returns>
    public bool MatchesPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        string extension = Path.GetExtension(path);
        foreach (string candidate in Extensions)
        {
            if (string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public override string ToString() => $"{DisplayName} ({string.Join(", ", Extensions)})";
}
