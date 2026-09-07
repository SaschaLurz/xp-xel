using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace XPXel.UI.ViewModels;

/// <summary>
/// One entry in the View menu's list of layouts.
/// </summary>
/// <remarks>
/// <para>
/// Two kinds of entry share this type: the two arrangements the editor ships with, which
/// cannot be renamed or deleted, and the ones the artist has saved, which can. Distinguishing
/// them with a flag rather than with two types keeps the menu a single list — which is what it
/// looks like to the person reading it, and how they expect to be able to choose from it.
/// </para>
/// <para>
/// A built-in entry carries no serialised layout at all: it is rebuilt from
/// <c>XpDockFactory</c> each time it is chosen, so it is always the arrangement this version
/// of the editor means rather than one frozen when the artist first launched it.
/// </para>
/// <para>
/// <b>Each entry carries its own command</b> rather than the menu reaching back up the visual
/// tree for the shell's. A submenu's items live in a popup, which is a separate visual root,
/// so an ancestor lookup from one of them is at best fragile and at worst silently null — and
/// a menu entry that quietly does nothing is the hardest kind of bug to notice. The same
/// reasoning put a callback on <see cref="PanelToggleViewModel"/>.
/// </para>
/// </remarks>
public sealed class LayoutEntryViewModel
{
    private LayoutEntryViewModel(
        string name,
        string? layout,
        bool isBuiltIn,
        bool isSimple,
        Action<LayoutEntryViewModel> onChosen)
    {
        Name = name;
        Layout = layout;
        IsBuiltIn = isBuiltIn;
        IsSimple = isSimple;
        ApplyCommand = new RelayCommand(() => onChosen(this));
    }

    /// <summary>The name shown in the menu.</summary>
    public string Name { get; }

    /// <summary>The serialised arrangement, or <see langword="null"/> for a built-in one.</summary>
    public string? Layout { get; }

    /// <summary>Gets a value indicating whether the editor ships with this layout.</summary>
    public bool IsBuiltIn { get; }

    /// <summary>Gets a value indicating whether this is the pared-back beginner's layout.</summary>
    public bool IsSimple { get; }

    /// <summary>Switches the editor to this layout.</summary>
    public ICommand ApplyCommand { get; }

    /// <summary>Creates the entry for the full arrangement the editor is built around.</summary>
    /// <param name="onChosen">Called when the artist picks it.</param>
    /// <returns>The menu entry.</returns>
    public static LayoutEntryViewModel Default(Action<LayoutEntryViewModel> onChosen) =>
        new("Default", layout: null, isBuiltIn: true, isSimple: false, onChosen);

    /// <summary>Creates the entry for colour, tools and the canvas, and nothing else.</summary>
    /// <param name="onChosen">Called when the artist picks it.</param>
    /// <returns>The menu entry.</returns>
    public static LayoutEntryViewModel Simple(Action<LayoutEntryViewModel> onChosen) =>
        new("Simple", layout: null, isBuiltIn: true, isSimple: true, onChosen);

    /// <summary>Wraps one of the artist's saved arrangements.</summary>
    /// <param name="name">What they called it.</param>
    /// <param name="layout">The serialised arrangement.</param>
    /// <param name="onChosen">Called when the artist picks it.</param>
    /// <returns>The menu entry.</returns>
    public static LayoutEntryViewModel Saved(string name, string layout, Action<LayoutEntryViewModel> onChosen) =>
        new(name, layout, isBuiltIn: false, isSimple: false, onChosen);
}
