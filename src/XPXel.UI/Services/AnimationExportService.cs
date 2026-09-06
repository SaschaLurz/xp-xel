using Avalonia.Platform.Storage;
using XPXel.Core.Documents;
using XPXel.Formats.Animation;

namespace XPXel.UI.Services;

/// <summary>What an export produced, for the message the shell shows afterwards.</summary>
/// <param name="FileCount">How many files were written.</param>
/// <param name="Destination">Where they went, as a name to show the artist.</param>
public sealed record AnimationExportResult(int FileCount, string Destination);

/// <summary>
/// Runs an animation export and writes its files where the artist chose.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <see cref="DocumentFileService"/> for animation, and it exists for the
/// same reason: view models should never touch a stream or a file handle. It is a separate
/// service because the shapes differ — a still export writes one stream, while an animation
/// export produces a set of named files that have to be placed together.
/// </para>
/// <para>
/// <b>Single-file formats get a save dialog; the rest get a folder picker.</b> Asking where
/// to put a GIF is a save dialog like any other; asking for sixty numbered PNGs one at a time
/// would be unusable. <see cref="IAnimationExporter.ProducesSingleFile"/> is what decides,
/// so the shell does not have to know which format is which.
/// </para>
/// </remarks>
public sealed class AnimationExportService
{
    private readonly IFileDialogService _fileDialog;

    /// <summary>Initialises the service.</summary>
    /// <param name="fileDialog">Asks the artist where the files should go.</param>
    public AnimationExportService(IFileDialogService fileDialog)
    {
        ArgumentNullException.ThrowIfNull(fileDialog);
        _fileDialog = fileDialog;
    }

    /// <summary>Exports a document's animation, asking the artist where to put it.</summary>
    /// <param name="document">The document to export.</param>
    /// <param name="request">The format and settings chosen in the export dialog.</param>
    /// <returns>
    /// What was written, or <see langword="null"/> if the artist cancelled the file dialog.
    /// </returns>
    /// <exception cref="XPXel.Formats.ImageFormatException">The document cannot be exported.</exception>
    /// <exception cref="IOException">The files could not be written.</exception>
    public async Task<AnimationExportResult?> ExportAsync(PixelDocument document, AnimationExportRequestData request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);

        // Rendering happens before the file dialog. It is the step that can fail on the
        // document's own contents, and discovering that after the artist has chosen a folder
        // is a worse experience than discovering it instead of the dialog.
        IReadOnlyList<ExportArtifact> artifacts = request.Exporter.Export(document, request.Options);

        if (artifacts.Count == 0)
        {
            return null;
        }

        return request.Exporter.ProducesSingleFile && artifacts.Count == 1
            ? await WriteSingleFileAsync(artifacts[0], request).ConfigureAwait(true)
            : await WriteToFolderAsync(artifacts).ConfigureAwait(true);
    }

    private async Task<AnimationExportResult?> WriteSingleFileAsync(
        ExportArtifact artifact,
        AnimationExportRequestData request)
    {
        IStorageFile? file = await _fileDialog
            .PickSaveFileAsync(
                "Export Animation",
                [request.Exporter.Descriptor],
                Path.GetFileNameWithoutExtension(artifact.FileName))
            .ConfigureAwait(true);

        if (file is null)
        {
            return null;
        }

        await WriteAsync(file, artifact.Content).ConfigureAwait(true);
        return new AnimationExportResult(1, file.Name);
    }

    private async Task<AnimationExportResult?> WriteToFolderAsync(IReadOnlyList<ExportArtifact> artifacts)
    {
        IStorageFolder? folder = await _fileDialog
            .PickFolderAsync("Choose a folder for the exported animation")
            .ConfigureAwait(true);

        if (folder is null)
        {
            return null;
        }

        foreach (ExportArtifact artifact in artifacts)
        {
            IStorageFile? file = await folder.CreateFileAsync(artifact.FileName).ConfigureAwait(true)
                ?? throw new IOException($"Could not create '{artifact.FileName}' in the chosen folder.");

            await WriteAsync(file, artifact.Content).ConfigureAwait(true);
        }

        return new AnimationExportResult(artifacts.Count, folder.Name);
    }

    /// <summary>
    /// Writes one file's bytes.
    /// </summary>
    /// <remarks>
    /// The stream a storage file hands back is not guaranteed to truncate an existing file, so
    /// overwriting a larger export with a smaller one would otherwise leave trailing rubbish
    /// on the end of it. Setting the length explicitly is the same precaution
    /// <see cref="DocumentFileService"/> takes.
    /// </remarks>
    private static async Task WriteAsync(IStorageFile file, byte[] content)
    {
        await using Stream stream = await file.OpenWriteAsync().ConfigureAwait(true);

        if (stream.CanSeek)
        {
            stream.SetLength(0);
        }

        await stream.WriteAsync(content).ConfigureAwait(true);
    }
}

/// <summary>
/// The format and settings an export was confirmed with.
/// </summary>
/// <param name="Exporter">The format to write.</param>
/// <param name="Options">How to write it.</param>
/// <remarks>
/// Mirrors the dialog's own result type so that this service does not have to reference the
/// dialog's view model — which would point the services layer at the view models rather than
/// the other way round.
/// </remarks>
public sealed record AnimationExportRequestData(IAnimationExporter Exporter, AnimationExportOptions Options);
