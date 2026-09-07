using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XPXel.UI.Services;

namespace XPXel.UI.ViewModels.Dialogs;

/// <summary>
/// One saved layout as it appears in the manage dialog: a name that can be typed over, and
/// the arrangement behind it.
/// </summary>
/// <remarks>
/// The name is edited in place rather than behind a separate rename dialog. Renaming is the
/// most common thing anyone does in a list like this, and a second dialog to do it would be
/// more ceremony than the act deserves.
/// </remarks>
public sealed partial class LayoutRowViewModel : ObservableObject
{
    /// <summary>The layout's name, as the artist is currently editing it.</summary>
    [ObservableProperty]
    private string _name;

    /// <summary>Initialises the row.</summary>
    /// <param name="preset">The saved layout it stands for.</param>
    public LayoutRowViewModel(LayoutPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        _name = preset.Name;
        Layout = preset.Layout;
    }

    /// <summary>The serialised arrangement, which renaming does not touch.</summary>
    public string Layout { get; }
}

/// <summary>
/// The list of saved layouts, where they are renamed and removed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing is written until Done.</b> The dialog works on its own copy of the list and
/// hands the whole thing back when it closes, so a rename thought better of is undone by
/// dismissing the dialog — which is what Cancel means everywhere else in this application and
/// would be a strange thing for this one list to disagree about.
/// </para>
/// <para>
/// <b>Applying a layout is not here.</b> Choosing one is what the View menu is for, and a
/// dialog that both managed the list and switched between its entries would have to close
/// itself to show the result. This is where layouts are tidied up; the menu is where they are
/// used.
/// </para>
/// </remarks>
public sealed partial class ManageLayoutsDialogViewModel : DialogViewModel
{
    /// <summary>Initialises the dialog.</summary>
    /// <param name="presets">The layouts currently saved.</param>
    public ManageLayoutsDialogViewModel(IReadOnlyList<LayoutPreset> presets)
        : base("Manage Layouts")
    {
        ArgumentNullException.ThrowIfNull(presets);

        Rows = [.. presets.Select(p => new LayoutRowViewModel(p))];
    }

    /// <summary>The layouts, in the order they will be listed in the menu.</summary>
    public ObservableCollection<LayoutRowViewModel> Rows { get; }

    /// <summary>Gets a value indicating whether there is anything to manage.</summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>Removes a layout from the list.</summary>
    /// <param name="row">The layout to remove.</param>
    [RelayCommand]
    private void Remove(LayoutRowViewModel? row)
    {
        if (row is not null && Rows.Remove(row))
        {
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>Moves a layout one place up the list, and so up the menu.</summary>
    /// <param name="row">The layout to move.</param>
    [RelayCommand]
    private void MoveUp(LayoutRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        int index = Rows.IndexOf(row);

        if (index > 0)
        {
            Rows.Move(index, index - 1);
        }
    }

    /// <summary>Moves a layout one place down the list.</summary>
    /// <param name="row">The layout to move.</param>
    [RelayCommand]
    private void MoveDown(LayoutRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        int index = Rows.IndexOf(row);

        if (index >= 0 && index < Rows.Count - 1)
        {
            Rows.Move(index, index + 1);
        }
    }

    /// <summary>Closes, handing back the edited list.</summary>
    /// <remarks>
    /// A row left with a blank name is dropped rather than saved as an unclickable menu entry;
    /// clearing a name is as reasonable a way to mean "get rid of this" as pressing the bin.
    /// </remarks>
    [RelayCommand]
    private void Confirm() => Close((IReadOnlyList<LayoutPreset>)
    [
        .. Rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .Select(r => new LayoutPreset(r.Name.Trim(), r.Layout)),
    ]);
}
