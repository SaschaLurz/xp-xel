using CommunityToolkit.Mvvm.Input;

namespace XPXel.UI.ViewModels.Dialogs;

/// <summary>
/// A message with up to three responses: confirm, an optional alternative, and dismiss.
/// </summary>
/// <remarks>
/// The three-button form exists for the one question that genuinely needs it — "save,
/// discard, or cancel?" when closing a modified document. Collapsing that to two buttons is
/// the classic way an application loses somebody's work.
/// </remarks>
public sealed partial class ConfirmDialogViewModel : DialogViewModel
{
    /// <summary>The outcome of a confirmation.</summary>
    public enum Response
    {
        /// <summary>The artist dismissed the dialog.</summary>
        Cancelled,

        /// <summary>The artist chose the primary action.</summary>
        Confirmed,

        /// <summary>The artist chose the secondary action, such as "Discard".</summary>
        Alternative,
    }

    /// <summary>Initialises the dialog.</summary>
    /// <param name="title">The heading.</param>
    /// <param name="message">The body text.</param>
    /// <param name="confirmText">The label of the primary button.</param>
    /// <param name="cancelText">The label of the dismiss button, or null to hide it.</param>
    /// <param name="alternativeText">The label of the optional middle button, or null to hide it.</param>
    public ConfirmDialogViewModel(
        string title,
        string message,
        string confirmText = "OK",
        string? cancelText = "Cancel",
        string? alternativeText = null)
        : base(title)
    {
        Message = message;
        ConfirmText = confirmText;
        CancelText = cancelText;
        AlternativeText = alternativeText;
    }

    /// <summary>The body text.</summary>
    public string Message { get; }

    /// <summary>The label of the primary button.</summary>
    public string ConfirmText { get; }

    /// <summary>The label of the dismiss button.</summary>
    public string? CancelText { get; }

    /// <summary>The label of the optional middle button.</summary>
    public string? AlternativeText { get; }

    /// <summary>Gets a value indicating whether the dismiss button should be shown.</summary>
    public bool HasCancel => !string.IsNullOrEmpty(CancelText);

    /// <summary>Gets a value indicating whether the middle button should be shown.</summary>
    public bool HasAlternative => !string.IsNullOrEmpty(AlternativeText);

    /// <summary>Closes with <see cref="Response.Confirmed"/>.</summary>
    [RelayCommand]
    private void Confirm() => Close(Response.Confirmed);

    /// <summary>Closes with <see cref="Response.Alternative"/>.</summary>
    [RelayCommand]
    private void ChooseAlternative() => Close(Response.Alternative);
}
