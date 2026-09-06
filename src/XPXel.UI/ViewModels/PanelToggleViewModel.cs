namespace XPXel.UI.ViewModels;

/// <summary>
/// One panel's entry in the View menu: its name, and a checkmark showing whether it is
/// currently on screen.
/// </summary>
/// <remarks>
/// <para>
/// <b>The checkmark is the state, not a command.</b> Before this existed the menu offered a
/// plain "Tools" item that brought the panel forward, and choosing it when the panel had been
/// closed rebuilt the entire default layout — so asking for one panel switched every panel
/// back on and threw away the artist's arrangement. A checkable item says what is currently
/// true and changes only the one thing it names.
/// </para>
/// <para>
/// <see cref="SetVisibleFromLayout"/> is how the shell writes the answer back <em>in</em>
/// without the menu trying to act on it. The artist can also close a panel with the × on its
/// tab or by dragging it away, and the checkmark has to follow those too.
/// </para>
/// </remarks>
public sealed class PanelToggleViewModel : ViewModelBase
{
    private readonly Action<PanelToggleViewModel, bool> _onToggled;
    private bool _isVisible;
    private bool _isSynchronising;

    /// <summary>Initialises the entry.</summary>
    /// <param name="id">The panel's stable identity.</param>
    /// <param name="title">The name shown in the menu.</param>
    /// <param name="onToggled">Called when the artist ticks or unticks the item.</param>
    public PanelToggleViewModel(string id, string title, Action<PanelToggleViewModel, bool> onToggled)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(onToggled);

        Id = id;
        Title = title;
        _onToggled = onToggled;
    }

    /// <summary>The panel's stable identity.</summary>
    public string Id { get; }

    /// <summary>The name shown in the menu.</summary>
    public string Title { get; }

    /// <summary>Whether the panel is currently in the layout.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (!SetProperty(ref _isVisible, value) || _isSynchronising)
            {
                return;
            }

            _onToggled(this, value);
        }
    }

    /// <summary>
    /// Updates the checkmark to match the layout, without treating it as the artist asking
    /// for a change.
    /// </summary>
    /// <param name="isVisible">Whether the panel is present in the layout.</param>
    public void SetVisibleFromLayout(bool isVisible)
    {
        _isSynchronising = true;

        try
        {
            IsVisible = isVisible;
        }
        finally
        {
            _isSynchronising = false;
        }
    }
}
