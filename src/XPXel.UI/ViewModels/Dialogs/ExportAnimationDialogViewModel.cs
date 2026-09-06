using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XPXel.Core.Documents;
using XPXel.Formats.Animation;

namespace XPXel.UI.ViewModels.Dialogs;

/// <summary>
/// One entry of the animation dropdown, including the "all of them" entry.
/// </summary>
/// <param name="Tag">The animation, or <see langword="null"/> for every one of them.</param>
/// <param name="Name">The label to show.</param>
/// <remarks>
/// A wrapper rather than a list with a null in it, because a data template is not applied to
/// a null item at all — the entry renders as a blank row, which reads as a bug rather than as
/// a choice.
/// </remarks>
public sealed record ExportTagChoice(AnimationTag? Tag, string Name);

/// <summary>The settings an animation export was confirmed with.</summary>
/// <param name="Exporter">The format to write.</param>
/// <param name="Options">How to write it.</param>
public sealed record AnimationExportRequest(IAnimationExporter Exporter, AnimationExportOptions Options);

/// <summary>
/// The export dialog: which format, which animation, and how the frames are arranged.
/// </summary>
/// <remarks>
/// <para>
/// <b>The formats describe themselves.</b> The list is the exporter registry, and the
/// explanation beside each one is the exporter's own <see cref="IAnimationExporter.Summary"/>.
/// The dialog therefore contains no knowledge of any particular format — adding one changes
/// nothing here — and, more usefully, the differences that matter to the decision are stated
/// where the decision is made rather than left for the artist to discover afterwards.
/// </para>
/// <para>
/// <b>Sheet options hide themselves for formats that have none.</b> A GIF has no layout or
/// padding, and offering controls that quietly do nothing is worse than offering none.
/// </para>
/// </remarks>
public sealed partial class ExportAnimationDialogViewModel : DialogViewModel
{
    private readonly PixelDocument _document;

    [ObservableProperty]
    private IAnimationExporter _selectedExporter;

    [ObservableProperty]
    private ExportTagChoice _selectedTagChoice;

    [ObservableProperty]
    private SpriteSheetLayout _layout = SpriteSheetLayout.RowPerAnimation;

    [ObservableProperty]
    private int _scale = 1;

    [ObservableProperty]
    private int _padding;

    [ObservableProperty]
    private bool _separateFilePerTag;

    [ObservableProperty]
    private bool _includeMetadata = true;

    [ObservableProperty]
    private string _baseName;

    /// <summary>Initialises the dialog.</summary>
    /// <param name="document">The document being exported.</param>
    /// <param name="exporters">The available formats, in display order.</param>
    /// <param name="suggestedName">The document's name, used as the file name.</param>
    public ExportAnimationDialogViewModel(
        PixelDocument document,
        IReadOnlyList<IAnimationExporter> exporters,
        string suggestedName)
        : base("Export Animation")
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(exporters);

        if (exporters.Count == 0)
        {
            throw new ArgumentException("There are no animation exporters registered.", nameof(exporters));
        }

        _document = document;
        Exporters = [.. exporters];

        // The first registered format is the sprite sheet, which is what 2D game animation
        // actually ships in — see the composition root for why that order is deliberate.
        _selectedExporter = exporters[0];
        _baseName = AnimationExportOptions.SanitiseFileName(suggestedName);

        Tags =
        [
            new ExportTagChoice(null, "All animations"),
            .. document.Tags.Select(t => new ExportTagChoice(t, t.Name)),
        ];

        _selectedTagChoice = Tags[0];

        // A document with several named animations laid out one per row is the readable
        // character sheet; one without tags has nothing to make rows from.
        if (document.Tags.Count == 0)
        {
            _layout = SpriteSheetLayout.Grid;
        }
    }

    /// <summary>The formats on offer.</summary>
    public ObservableCollection<IAnimationExporter> Exporters { get; }

    /// <summary>
    /// The animations on offer, led by an entry meaning "all of them".
    /// </summary>
    public ObservableCollection<ExportTagChoice> Tags { get; }

    /// <summary>The animation to export, or <see langword="null"/> for every one of them.</summary>
    public AnimationTag? SelectedTag => SelectedTagChoice.Tag;

    /// <summary>The sheet layouts on offer.</summary>
    public static IReadOnlyList<SpriteSheetLayout> Layouts { get; } = Enum.GetValues<SpriteSheetLayout>();

    /// <summary>The magnifications on offer.</summary>
    public static IReadOnlyList<int> Scales { get; } = [1, 2, 3, 4, 6, 8];

    /// <summary>Gets a value indicating whether the chosen format arranges frames on a sheet.</summary>
    public bool ShowsSheetOptions => SelectedExporter.UsesSheetLayout;

    /// <summary>Gets a value indicating whether the document has named animations to choose from.</summary>
    public bool HasTags => _document.Tags.Count > 0;

    /// <summary>An explanation of the chosen format, shown beneath the list.</summary>
    public string FormatSummary => SelectedExporter.Summary;

    /// <summary>
    /// A plain description of what the artist will end up with, so that "one file or a
    /// folder full of them" is answered before the file dialog appears rather than after.
    /// </summary>
    public string OutcomeText
    {
        get
        {
            int animations = SelectedTag is not null ? 1 : Math.Max(1, _document.Tags.Count);

            if (SelectedExporter.ProducesSingleFile)
            {
                return animations == 1
                    ? "Writes one file."
                    : $"Writes {animations} files, one per animation, into a folder you choose.";
            }

            if (SelectedExporter.UsesSheetLayout)
            {
                return SeparateFilePerTag && animations > 1
                    ? $"Writes {animations} sheets and {animations} metadata files into a folder you choose."
                    : "Writes one sheet and one metadata file into a folder you choose.";
            }

            int frames = SelectedTag?.FrameCount ?? _document.Frames.Count;
            return $"Writes {frames} numbered images into a folder you choose.";
        }
    }

    /// <summary>Confirms the export.</summary>
    [RelayCommand]
    private void Confirm()
    {
        AnimationExportOptions options = new()
        {
            BaseName = BaseName,
            Scale = Scale,
            Tag = SelectedTag,
            SeparateFilePerTag = SeparateFilePerTag,
            Layout = Layout,
            Padding = Padding,
            IncludeMetadata = IncludeMetadata,
        };

        Close(new AnimationExportRequest(SelectedExporter, options));
    }

    partial void OnSelectedExporterChanged(IAnimationExporter value)
    {
        OnPropertyChanged(nameof(ShowsSheetOptions));
        OnPropertyChanged(nameof(FormatSummary));
        OnPropertyChanged(nameof(OutcomeText));
    }

    partial void OnSelectedTagChoiceChanged(ExportTagChoice value)
    {
        OnPropertyChanged(nameof(SelectedTag));
        OnPropertyChanged(nameof(OutcomeText));
    }

    partial void OnSeparateFilePerTagChanged(bool value) => OnPropertyChanged(nameof(OutcomeText));

    partial void OnLayoutChanged(SpriteSheetLayout value) => OnPropertyChanged(nameof(OutcomeText));
}
