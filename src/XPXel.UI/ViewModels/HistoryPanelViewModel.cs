using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XPXel.Core.Editing;
using XPXel.Core.History;
using XPXel.UI.Services;

namespace XPXel.UI.ViewModels;

/// <summary>
/// One row of the history panel: an action that was taken, and whether the document currently
/// stands after it.
/// </summary>
/// <param name="Position">
/// How many entries are applied once this row is the current one, which is what
/// <see cref="DocumentHistory.MoveTo(int)"/> is given.
/// </param>
/// <param name="Name">What the action is called.</param>
/// <param name="IsCurrent">Whether the document is standing at this row.</param>
/// <param name="IsUndone">Whether this row has been undone and could be redone.</param>
public sealed record HistoryEntryViewModel(int Position, string Name, bool IsCurrent, bool IsUndone);

/// <summary>
/// The history panel: what has been done to the document, and a way back to any of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is a navigator, not a log.</b> Clicking a row winds the document to that point, which
/// is the whole reason a history panel is worth having: going back six steps should not mean
/// pressing Ctrl+Z six times and counting. Undone rows stay in the list, greyed, until a new
/// action discards them — so an artist who went too far back can see exactly what they are
/// about to lose.
/// </para>
/// <para>
/// <b>What it costs.</b> Nothing is stored for this panel. The undo history already holds every
/// entry — a raster edit keeps a copy of just the rectangle it changed, which is why a pencil
/// dab costs a few dozen bytes rather than two copies of the canvas — and this reads that list.
/// The one real cost is the depth limit, which is why it is a setting: how far back to keep is
/// a trade between memory and regret that only the artist can make.
/// </para>
/// <para>
/// <b>The limit is remembered across sessions</b> through <see cref="ISettingsStore"/>, and
/// applied to every document as it is opened. Lowering it trims the open document immediately,
/// because a limit that only applied to future work would look like it had done nothing.
/// </para>
/// </remarks>
public sealed partial class HistoryPanelViewModel : ViewModelBase, IDisposable
{
    /// <summary>Where the depth limit is remembered between sessions.</summary>
    private const string CapacityKey = "history.capacity";

    private readonly ISettingsStore _settings;

    private DocumentWorkspace? _workspace;
    private DocumentHistory? _history;
    private bool _isNavigating;

    /// <summary>Initialises the panel.</summary>
    /// <param name="settings">Where the depth limit is stored.</param>
    public HistoryPanelViewModel(ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _capacity = LoadCapacity(settings);
    }

    /// <summary>The recorded actions, most recent first.</summary>
    /// <remarks>
    /// Newest at the top, because that is the end an artist is working at and the end they
    /// reach for when something has just gone wrong.
    /// </remarks>
    public ObservableCollection<HistoryEntryViewModel> Entries { get; } = [];

    /// <summary>How many actions are kept before the oldest are discarded.</summary>
    [ObservableProperty]
    private int _capacity = DocumentHistory.DefaultCapacity;

    /// <summary>The smallest depth that may be set.</summary>
    public static int MinimumCapacity => DocumentHistory.MinimumCapacity;

    /// <summary>The largest depth that may be set.</summary>
    public static int MaximumCapacity => DocumentHistory.MaximumCapacity;

    /// <summary>Gets a value indicating whether there is a document to show a history for.</summary>
    public bool HasDocument => _history is not null;

    /// <summary>How full the history is, for the panel's footer.</summary>
    public string DepthText => _history is { } history
        ? $"{history.Count} of {history.Capacity} kept"
        : "No document";

    /// <summary>Points the panel at a different document, or at none.</summary>
    /// <param name="workspace">The document to follow.</param>
    public void Attach(DocumentWorkspace? workspace)
    {
        if (ReferenceEquals(_workspace, workspace))
        {
            return;
        }

        if (_history is not null)
        {
            _history.Changed -= OnHistoryChanged;
        }

        _workspace = workspace;
        _history = workspace?.History;

        if (_history is not null)
        {
            // Every document adopts the artist's chosen depth as it arrives, including ones
            // opened before the setting was last changed.
            _history.Capacity = Capacity;
            _history.Changed += OnHistoryChanged;
        }

        Rebuild();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_history is not null)
        {
            _history.Changed -= OnHistoryChanged;
            _history = null;
        }

        _workspace = null;
    }

    /// <summary>Winds the document to the point an entry describes.</summary>
    /// <param name="entry">The row that was clicked.</param>
    [RelayCommand]
    private void GoTo(HistoryEntryViewModel? entry)
    {
        if (entry is null || _history is null)
        {
            return;
        }

        // The history raises Changed as it moves, which would rebuild the list underneath the
        // click. Nothing breaks, but the rebuild is wasted; one at the end is enough.
        _isNavigating = true;

        try
        {
            _history.MoveTo(entry.Position);
        }
        finally
        {
            _isNavigating = false;
        }

        Rebuild();
    }

    partial void OnCapacityChanged(int value)
    {
        int clamped = Math.Clamp(value, MinimumCapacity, MaximumCapacity);

        if (clamped != value)
        {
            Capacity = clamped;
            return;
        }

        if (_history is not null)
        {
            _history.Capacity = clamped;
        }

        _settings.SetValue(CapacityKey, clamped.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Rebuild();
    }

    private static int LoadCapacity(ISettingsStore settings)
    {
        // Settings are a convenience and may be missing or unparsable; the default is always
        // a correct answer.
        return int.TryParse(
            settings.GetValue(CapacityKey),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out int stored)
            ? Math.Clamp(stored, DocumentHistory.MinimumCapacity, DocumentHistory.MaximumCapacity)
            : DocumentHistory.DefaultCapacity;
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        if (!_isNavigating)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        Entries.Clear();

        if (_history is { } history)
        {
            // Walked backwards so the newest action is the first row.
            for (int i = history.Count - 1; i >= 0; i--)
            {
                Entries.Add(new HistoryEntryViewModel(
                    Position: i + 1,
                    Name: history.Commands[i].Name,
                    IsCurrent: history.Position == i + 1,
                    IsUndone: i >= history.Position));
            }

            // The bottom row is the document before anything was done to it, which has to be
            // reachable: without it there is no way back past the oldest recorded action.
            Entries.Add(new HistoryEntryViewModel(
                Position: 0,
                Name: "Opened",
                IsCurrent: history.Position == 0,
                IsUndone: false));
        }

        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(DepthText));
    }
}
