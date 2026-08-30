using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace XPXel.UI.ViewModels.Dialogs;

/// <summary>
/// The base class for the modal panels shown over the editor.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why dialogs are overlays rather than windows.</b> XP-xel targets the browser as well
/// as the desktop, and there are no child windows in the browser. Rendering dialogs as an
/// overlay inside the shell means one implementation behaves identically everywhere, and it
/// removes a whole class of platform-specific window ownership and modality bugs.
/// </para>
/// <para>
/// A dialog exposes its outcome as a <see cref="Task{TResult}"/>, so calling code reads as
/// a straight line — <c>if (await dialog.ShowAsync(...) is ImageSize size)</c> — rather than
/// as a callback.
/// </para>
/// </remarks>
public abstract partial class DialogViewModel : ViewModelBase
{
    private readonly TaskCompletionSource<object?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Initialises the dialog.</summary>
    /// <param name="title">The heading shown at the top of the panel.</param>
    protected DialogViewModel(string title) => Title = title;

    /// <summary>The heading shown at the top of the panel.</summary>
    public string Title { get; }

    /// <summary>
    /// Completes when the dialog closes, carrying its result, or <see langword="null"/> if
    /// it was dismissed.
    /// </summary>
    public Task<object?> Completion => _completion.Task;

    /// <summary>Dismisses the dialog without a result.</summary>
    [RelayCommand]
    public void Cancel() => Close(null);

    /// <summary>Closes the dialog, returning a result to whoever is awaiting it.</summary>
    /// <param name="result">The outcome, or <see langword="null"/> for a dismissal.</param>
    protected void Close(object? result) => _completion.TrySetResult(result);
}

/// <summary>
/// Hosts the dialog currently on screen, if any.
/// </summary>
/// <remarks>
/// The shell binds to <see cref="Current"/>: when it is non-null the overlay appears and the
/// editor beneath is blocked from input. Only one dialog is shown at a time, which is all
/// the application needs and avoids reasoning about stacked modality.
/// </remarks>
public sealed partial class DialogHostViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    private DialogViewModel? _current;

    /// <summary>Gets a value indicating whether a dialog is currently displayed.</summary>
    public bool IsOpen => Current is not null;

    /// <summary>Shows a dialog and waits for it to close.</summary>
    /// <param name="dialog">The dialog to display.</param>
    /// <returns>The dialog's result, or <see langword="null"/> if it was dismissed.</returns>
    public async Task<object?> ShowAsync(DialogViewModel dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        Current = dialog;

        try
        {
            return await dialog.Completion.ConfigureAwait(true);
        }
        finally
        {
            // Clearing in a finally block means a dialog that faults cannot leave the editor
            // permanently covered by an overlay that will never close.
            Current = null;
        }
    }
}
