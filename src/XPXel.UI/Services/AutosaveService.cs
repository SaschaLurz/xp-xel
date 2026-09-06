using System.Runtime.CompilerServices;
using Avalonia.Threading;
using XPXel.Core.Editing;
using XPXel.Formats;

namespace XPXel.UI.Services;

/// <summary>
/// What one round of autosaving did, for the status bar.
/// </summary>
/// <param name="Written">How many documents were backed up.</param>
/// <param name="Failed">How many could not be, which is usually a full or read-only disk.</param>
public sealed record AutosaveResult(int Written, int Failed);

/// <summary>
/// Takes a copy of every open document at a fixed interval, so that a crash costs minutes of
/// work rather than a session.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why three minutes.</b> A backup is a serialise-and-write of the whole document, which for
/// the sizes a pixel editor works at is a handful of milliseconds and a few hundred kilobytes.
/// The cost is low enough that the interval is set by how much work an artist is willing to
/// lose rather than by what the machine can afford.
/// </para>
/// <para>
/// <b>Only what has changed is written.</b> A document is skipped unless it has unsaved changes
/// <em>and</em> its history has moved since its last backup. Without the second test a document
/// left open and untouched would rewrite an identical copy every three minutes, and the two
/// older backups — the ones most likely to predate whatever went wrong — would be pushed out by
/// copies of the same thing.
/// </para>
/// <para>
/// <b>Serialising happens on the UI thread, writing does not.</b> The document is not
/// thread-safe and the artist may be drawing on it, so it is turned into bytes on the thread
/// that owns it; only the finished buffer is handed to the store, which may go to disk at its
/// leisure. This is why the timer is a <see cref="DispatcherTimer"/> and not a background one.
/// </para>
/// <para>
/// <b>Nothing here can interrupt the artist.</b> A failed backup is counted and reported
/// through <see cref="Completed"/>; it never throws, never shows a dialog, and never changes
/// the document's saved state.
/// </para>
/// </remarks>
public sealed class AutosaveService : IDisposable
{
    /// <summary>How often a backup is taken.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(3);

    /// <summary>A name whose extension picks the project writer out of the registry.</summary>
    private const string ProjectFileName = "document.xpxel";

    /// <summary>
    /// The revision of each document at its last backup, so that an untouched document is not
    /// copied again.
    /// </summary>
    /// <remarks>
    /// Keyed weakly, so that a closed document's entry goes away with it rather than holding
    /// the whole workspace — and its pixels — alive for the life of the application.
    /// </remarks>
    private readonly ConditionalWeakTable<DocumentWorkspace, BackupState> _state = new();

    private readonly IAutosaveStore _store;
    private readonly ImageFormatRegistry _formats;
    private readonly DispatcherTimer _timer;

    private Func<IReadOnlyList<DocumentWorkspace>>? _source;
    private bool _isRunning;
    private bool _isDisposed;

    /// <summary>Initialises the service.</summary>
    /// <param name="store">Where backups are kept.</param>
    /// <param name="formats">The codecs, for finding the project writer.</param>
    public AutosaveService(IAutosaveStore store, ImageFormatRegistry formats)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(formats);

        _store = store;
        _formats = formats;

        _timer = new DispatcherTimer(Interval, DispatcherPriority.Background, OnTick);
        _timer.Stop();
    }

    /// <summary>Raised on the UI thread after each round that backed something up.</summary>
    public event EventHandler<AutosaveResult>? Completed;

    /// <summary>Gets a value indicating whether this platform keeps backups.</summary>
    public bool IsAvailable => _store.IsAvailable;

    /// <summary>Where the backups are kept, or <see langword="null"/> when nowhere.</summary>
    public string? Location => _store.Location;

    /// <summary>Begins backing up whatever the given delegate reports as open.</summary>
    /// <param name="source">
    /// Asked, on each tick, which documents are open. A delegate rather than a collection
    /// because the shell keeps its documents in the docking layout, where tabs come and go.
    /// </param>
    public void Start(Func<IReadOnlyList<DocumentWorkspace>> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        _source = source;

        if (_store.IsAvailable)
        {
            _timer.Start();
        }
    }

    /// <summary>Stops backing up. Used when the application is shutting down.</summary>
    public void Stop() => _timer.Stop();

    /// <summary>Backs up everything that has changed, now.</summary>
    /// <returns>What was written.</returns>
    /// <remarks>
    /// Public so that the shell can take a backup at a moment it knows to be dangerous — before
    /// a resize, say — without waiting for the clock.
    /// </remarks>
    public async Task<AutosaveResult> RunAsync()
    {
        if (_source is not { } source || !_store.IsAvailable || _isRunning)
        {
            return new AutosaveResult(0, 0);
        }

        _isRunning = true;

        try
        {
            int written = 0;
            int failed = 0;

            foreach (DocumentWorkspace workspace in source())
            {
                switch (await TryBackUpAsync(workspace).ConfigureAwait(true))
                {
                    case true:
                        written++;
                        break;

                    case false:
                        failed++;
                        break;

                    default:
                        break;
                }
            }

            return new AutosaveResult(written, failed);
        }
        finally
        {
            _isRunning = false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _timer.Stop();
    }

    /// <summary>
    /// Backs up one document if it needs it.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> written, <see langword="false"/> attempted and failed, and
    /// <see langword="null"/> when there was nothing worth writing.
    /// </returns>
    private async Task<bool?> TryBackUpAsync(DocumentWorkspace workspace)
    {
        BackupState state = _state.GetValue(workspace, static _ => new BackupState());

        if (!workspace.HasUnsavedChanges || state.Revision == workspace.History.Revision)
        {
            return null;
        }

        if (_formats.FindExporterByPath(ProjectFileName) is not { } exporter)
        {
            return null;
        }

        byte[] contents;

        try
        {
            // On the UI thread on purpose: the artist may be mid-stroke, and the document is
            // not safe to read from anywhere else.
            using MemoryStream buffer = new();
            exporter.Write(buffer, workspace.Document, ExportOptions.Default);
            contents = buffer.ToArray();
        }
        catch (Exception ex) when (ex is IOException or ImageFormatException or InvalidOperationException)
        {
            return false;
        }

        bool ok = await _store
            .WriteAsync(state.Key, workspace.DisplayName, contents, CancellationToken.None)
            .ConfigureAwait(true);

        if (ok)
        {
            // Only on success, so a document whose backup failed is tried again next tick
            // rather than being written off.
            state.Revision = workspace.History.Revision;
        }

        return ok;
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        AutosaveResult result = await RunAsync().ConfigureAwait(true);

        if (result.Written > 0 || result.Failed > 0)
        {
            Completed?.Invoke(this, result);
        }
    }

    /// <summary>What the service remembers about one open document.</summary>
    private sealed class BackupState
    {
        /// <summary>
        /// This document's identity among the backups, for the life of the session.
        /// </summary>
        /// <remarks>
        /// Generated rather than derived from the file path, because an unsaved document has no
        /// path — and an unsaved document is precisely the one whose loss would hurt most.
        /// </remarks>
        public string Key { get; } = Guid.NewGuid().ToString("N")[..8];

        /// <summary>The document's history revision when it was last backed up.</summary>
        public int Revision { get; set; } = -1;
    }
}
