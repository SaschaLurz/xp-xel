namespace XPXel.UI.ViewModels;

/// <summary>
/// Where a layer being dragged would be inserted relative to the row it is hovering over.
/// </summary>
/// <remarks>
/// The values describe the position <i>as displayed</i> in the layers panel, where the
/// top-most layer appears first. The panel converts that to the document's bottom-first
/// storage order in <see cref="LayersViewModel.ReorderLayer"/>, which is the only place in
/// the codebase that needs to know the two orders differ.
/// </remarks>
public enum LayerDropPosition
{
    /// <summary>Nothing is being dragged over the row.</summary>
    None = 0,

    /// <summary>The dragged layer would be placed above this row.</summary>
    Above,

    /// <summary>The dragged layer would be placed below this row.</summary>
    Below,

    /// <summary>
    /// The dragged layer would be moved inside this row's folder. Only folder rows ever
    /// report this, and only for the middle band of the row — the top and bottom quarters
    /// still restack, so a layer can be dropped beside a folder as well as into it.
    /// </summary>
    Inside,
}
