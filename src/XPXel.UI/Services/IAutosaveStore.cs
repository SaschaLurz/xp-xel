namespace XPXel.UI.Services;

/// <summary>
/// Somewhere to keep periodic backups of the documents that are open, so that a crash costs
/// minutes rather than an afternoon.
/// </summary>
/// <remarks>
/// <para>
/// An interface for the same reason <see cref="ISettingsStore"/> is one: where the copies
/// physically go differs completely between heads, and the WebAssembly head has nowhere to put
/// a several-megabyte archive at all. Everything above this line asks whether backups are
/// <see cref="IsAvailable"/> and otherwise carries on.
/// </para>
/// <para>
/// <b>A backup is not a save.</b> Nothing here touches the artist's own file or the document's
/// undo history, and an autosave never marks a document as saved. The copies exist to be found
/// after a crash, which is why they carry a timestamp and why several are kept: the most recent
/// one may well contain the mistake that caused the crash.
/// </para>
/// </remarks>
public interface IAutosaveStore
{
    /// <summary>Gets a value indicating whether this platform can keep backups at all.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Where the backups are kept, in a form that can be shown to the artist and opened, or
    /// <see langword="null"/> when there is nowhere.
    /// </summary>
    string? Location { get; }

    /// <summary>Writes one backup, retiring the oldest copies of the same document.</summary>
    /// <param name="documentKey">
    /// A stable identifier for the open document, which decides which existing copies this one
    /// supersedes. Two documents must never share one.
    /// </param>
    /// <param name="documentName">The document's name, for a person reading the folder.</param>
    /// <param name="contents">The serialised project.</param>
    /// <param name="cancellationToken">Cancels a write in progress.</param>
    /// <returns>
    /// <see langword="true"/> if the backup was written. A store that fails reports it rather
    /// than throwing: a backup that cannot be taken must never interrupt the artist.
    /// </returns>
    Task<bool> WriteAsync(
        string documentKey,
        string documentName,
        ReadOnlyMemory<byte> contents,
        CancellationToken cancellationToken);
}

/// <summary>
/// The autosave store for a platform with nowhere to write, which accepts backups and drops
/// them.
/// </summary>
/// <remarks>
/// Registered by default so that a head which has not implemented backups still runs; it
/// reports <see cref="IsAvailable"/> as <see langword="false"/> so the shell can say so rather
/// than promising a safety net it does not have.
/// </remarks>
public sealed class NullAutosaveStore : IAutosaveStore
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public string? Location => null;

    /// <inheritdoc />
    public Task<bool> WriteAsync(
        string documentKey,
        string documentName,
        ReadOnlyMemory<byte> contents,
        CancellationToken cancellationToken) => Task.FromResult(false);
}
