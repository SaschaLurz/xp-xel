using XPXel.UI.Services;

namespace XPXel.Desktop;

/// <summary>
/// Keeps autosaved copies of open documents as ordinary <c>.xpxel</c> files in a folder the
/// artist can open, search and drag out of.
/// </summary>
/// <remarks>
/// <para>
/// <b>Plain files in a plain folder, on purpose.</b> A backup is only worth having if it can be
/// recovered by someone who has just lost their work and is not in a mood to learn anything. So
/// there is no database, no index and no private format: each file is a complete project that
/// opens with File → Open, named after the document and stamped with the time it was taken.
/// </para>
/// <para>
/// <b>Where they go.</b> <see cref="Environment.SpecialFolder.LocalApplicationData"/> rather
/// than the roaming folder that holds the settings, because these are large, machine-local and
/// disposable, and pushing tens of megabytes through a roaming profile every three minutes
/// would be a poor way to repay a network administrator.
/// </para>
/// <para>
/// <b>How they are pruned.</b> The newest <see cref="CopiesPerDocument"/> copies of each
/// document survive, which is the point of keeping more than one: the most recent backup may
/// contain whatever went wrong, and the one before it is the way back. Anything older than
/// <see cref="RetentionDays"/> days goes regardless, which is what clears out documents from
/// sessions long since finished — their keys are never seen again, so nothing else would.
/// </para>
/// <para>
/// <b>Every failure is swallowed.</b> A full disk or a locked folder must not interrupt
/// drawing; the write reports failure and the shell says so quietly in the status bar.
/// </para>
/// </remarks>
internal sealed class FileAutosaveStore : IAutosaveStore
{
    /// <summary>How many backups of one document are kept.</summary>
    private const int CopiesPerDocument = 3;

    /// <summary>How long a backup survives regardless of how few there are.</summary>
    private const int RetentionDays = 7;

    /// <summary>
    /// Separates the parts of a backup's file name. A character that cannot appear in the
    /// document name half, so the key can always be read back out.
    /// </summary>
    private const char Separator = '~';

    private readonly string _directory;

    /// <summary>Initialises the store.</summary>
    public FileAutosaveStore() =>
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XP-xel",
            "Backups");

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public string Location => _directory;

    /// <inheritdoc />
    public async Task<bool> WriteAsync(
        string documentKey,
        string documentName,
        ReadOnlyMemory<byte> contents,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentKey);

        try
        {
            Directory.CreateDirectory(_directory);

            string stamp = DateTime.Now.ToString("yyyy-MM-dd HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            string name = $"{Sanitise(documentName)}{Separator}{documentKey}{Separator}{stamp}.xpxel";
            string path = Path.Combine(_directory, name);

            await using (FileStream stream = File.Create(path))
            {
                await stream.WriteAsync(contents, cancellationToken).ConfigureAwait(false);
            }

            Prune(documentKey);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reduces a document name to something a file system will accept, keeping it recognisable.
    /// </summary>
    private static string Sanitise(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Untitled";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        char[] cleaned = [.. name.Select(c => Array.IndexOf(invalid, c) >= 0 || c == Separator ? '_' : c)];

        // Long enough to stay recognisable, short enough that the name plus the key, the stamp
        // and the folder cannot run past a path length limit.
        string result = new string(cleaned).Trim();
        return result.Length > 48 ? result[..48] : result;
    }

    /// <summary>Deletes the copies that are no longer worth keeping.</summary>
    private void Prune(string documentKey)
    {
        try
        {
            List<FileInfo> all = [.. new DirectoryInfo(_directory).EnumerateFiles($"*{Separator}*.xpxel")];
            DateTime cutoff = DateTime.Now.AddDays(-RetentionDays);

            IEnumerable<FileInfo> expired = all.Where(f => f.LastWriteTime < cutoff);

            IEnumerable<FileInfo> superseded = all
                .Where(f => BelongsTo(f, documentKey))
                .OrderByDescending(f => f.LastWriteTime)
                .Skip(CopiesPerDocument);

            foreach (FileInfo file in expired.Concat(superseded).DistinctBy(f => f.FullName))
            {
                file.Delete();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // A backup that could not be tidied up is still a backup. Leaving a file behind is
            // the harmless failure here, and the next round will try again.
        }
    }

    private static bool BelongsTo(FileInfo file, string documentKey)
    {
        string[] parts = Path.GetFileNameWithoutExtension(file.Name).Split(Separator);
        return parts.Length == 3 && string.Equals(parts[1], documentKey, StringComparison.Ordinal);
    }
}
