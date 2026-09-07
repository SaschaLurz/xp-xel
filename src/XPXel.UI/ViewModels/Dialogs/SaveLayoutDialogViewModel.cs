using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace XPXel.UI.ViewModels.Dialogs;

/// <summary>
/// Asks what to call the arrangement the artist is about to save.
/// </summary>
/// <remarks>
/// A name that is already taken is allowed rather than rejected: typing the name of an
/// existing layout is how an artist says "update that one", and answering with an error would
/// leave them deleting it first. The dialog says what will happen instead.
/// </remarks>
public sealed partial class SaveLayoutDialogViewModel : DialogViewModel
{
    private readonly IReadOnlyCollection<string> _existingNames;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyPropertyChangedFor(nameof(Warning))]
    [NotifyPropertyChangedFor(nameof(HasWarning))]
    private string _name = string.Empty;

    /// <summary>Initialises the dialog.</summary>
    /// <param name="existingNames">The layouts already saved, so an overwrite can be announced.</param>
    /// <param name="suggestedName">The name to start with.</param>
    public SaveLayoutDialogViewModel(IReadOnlyCollection<string> existingNames, string suggestedName)
        : base("Save Layout")
    {
        ArgumentNullException.ThrowIfNull(existingNames);

        _existingNames = existingNames;
        _name = suggestedName;
    }

    /// <summary>Gets a value indicating whether the name would replace an existing layout.</summary>
    public bool HasWarning => Warning.Length > 0;

    /// <summary>What choosing this name would do, when it would do something unexpected.</summary>
    public string Warning =>
        _existingNames.Contains(Name.Trim(), StringComparer.CurrentCultureIgnoreCase)
            ? "A layout with this name already exists, and will be replaced."
            : string.Empty;

    /// <summary>Gets a value indicating whether the name is usable.</summary>
    private bool CanConfirm => !string.IsNullOrWhiteSpace(Name);

    /// <summary>Closes with the chosen name.</summary>
    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => Close(Name.Trim());
}
