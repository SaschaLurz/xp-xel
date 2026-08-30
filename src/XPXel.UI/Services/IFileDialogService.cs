using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using XPXel.Formats;

namespace XPXel.UI.Services;

/// <summary>
/// Asks the artist to choose a file to open or a location to save to.
/// </summary>
/// <remarks>
/// <para>
/// The dialog filters are built from the format registry rather than hard-coded, so a newly
/// registered codec appears in the Open and Export dialogs with no change here.
/// </para>
/// <para>
/// The methods return Avalonia's <see cref="IStorageFile"/> rather than a path, because a
/// path is a desktop concept. In the browser the artist's chosen file is a handle with no
/// meaningful path, and code written against handles works unchanged on both.
/// </para>
/// </remarks>
public interface IFileDialogService
{
    /// <summary>Asks the artist to choose an existing file.</summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="formats">The formats to offer, in order.</param>
    /// <returns>The chosen file, or <see langword="null"/> if the artist cancelled.</returns>
    Task<IStorageFile?> PickOpenFileAsync(string title, IReadOnlyList<FormatDescriptor> formats);

    /// <summary>Asks the artist where to save.</summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="formats">The formats to offer, in order.</param>
    /// <param name="suggestedName">The file name to pre-fill, without an extension.</param>
    /// <returns>The chosen destination, or <see langword="null"/> if the artist cancelled.</returns>
    Task<IStorageFile?> PickSaveFileAsync(
        string title,
        IReadOnlyList<FormatDescriptor> formats,
        string suggestedName);
}

/// <summary>
/// The default <see cref="IFileDialogService"/>, built on Avalonia's storage provider.
/// </summary>
/// <remarks>
/// One implementation covers every platform: <see cref="TopLevel.StorageProvider"/> maps to
/// native dialogs on Windows, macOS and Linux, and to the File System Access API in the
/// browser.
/// </remarks>
public sealed class StorageFileDialogService : IFileDialogService
{
    /// <inheritdoc />
    public async Task<IStorageFile?> PickOpenFileAsync(string title, IReadOnlyList<FormatDescriptor> formats)
    {
        if (ResolveTopLevel()?.StorageProvider is not { } provider)
        {
            return null;
        }

        IReadOnlyList<IStorageFile> files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = BuildFilters(formats, includeCombinedEntry: true),
        }).ConfigureAwait(true);

        return files.Count > 0 ? files[0] : null;
    }

    /// <inheritdoc />
    public async Task<IStorageFile?> PickSaveFileAsync(
        string title,
        IReadOnlyList<FormatDescriptor> formats,
        string suggestedName)
    {
        if (ResolveTopLevel()?.StorageProvider is not { } provider)
        {
            return null;
        }

        return await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = formats.Count > 0 ? formats[0].PrimaryExtension.TrimStart('.') : null,
            ShowOverwritePrompt = true,
            FileTypeChoices = BuildFilters(formats, includeCombinedEntry: false),
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Finds the top level to parent dialogs to, by asking Avalonia how the application was
    /// launched.
    /// </summary>
    /// <remarks>
    /// Going through the lifetime rather than walking the visual tree means no view has to
    /// register itself anywhere, and it is the one piece of code that needs to know a
    /// desktop application has a window while a browser application does not.
    /// </remarks>
    private static TopLevel? ResolveTopLevel() => Application.Current?.ApplicationLifetime switch
    {
        IClassicDesktopStyleApplicationLifetime desktop => desktop.MainWindow,
        ISingleViewApplicationLifetime singleView when singleView.MainView is { } view =>
            TopLevel.GetTopLevel(view),
        _ => null,
    };

    /// <summary>
    /// Turns format descriptors into picker filters.
    /// </summary>
    /// <param name="formats">The formats to offer.</param>
    /// <param name="includeCombinedEntry">
    /// Whether to prepend an "All supported files" entry. Useful when opening, where the
    /// artist usually just wants their file; pointless when saving, where exactly one format
    /// must be chosen.
    /// </param>
    private static List<FilePickerFileType> BuildFilters(
        IReadOnlyList<FormatDescriptor> formats,
        bool includeCombinedEntry)
    {
        List<FilePickerFileType> filters = [];

        if (includeCombinedEntry && formats.Count > 1)
        {
            List<string> allPatterns = [];
            List<string> allMimeTypes = [];

            foreach (FormatDescriptor format in formats)
            {
                allMimeTypes.Add(format.MimeType);
                foreach (string extension in format.Extensions)
                {
                    allPatterns.Add($"*{extension}");
                }
            }

            filters.Add(new FilePickerFileType("All supported files")
            {
                Patterns = allPatterns,
                MimeTypes = allMimeTypes,
            });
        }

        foreach (FormatDescriptor format in formats)
        {
            List<string> patterns = [];
            foreach (string extension in format.Extensions)
            {
                patterns.Add($"*{extension}");
            }

            filters.Add(new FilePickerFileType(format.DisplayName)
            {
                Patterns = patterns,
                MimeTypes = [format.MimeType],
            });
        }

        return filters;
    }
}
