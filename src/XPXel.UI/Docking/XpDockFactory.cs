using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using XPXel.UI.ViewModels;

namespace XPXel.UI.Docking;

/// <summary>
/// Builds the editor's default arrangement of panels and document tabs.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the one place that describes the default layout.</b> Once the application has
/// started, the arrangement belongs to the artist: panels can be dragged into other panels,
/// torn off into floating windows, tabbed together, or closed. The saved layout is restored
/// on the next launch, and "Reset Layout" throws it away and calls back into here.
/// </para>
/// <para>
/// <b>Docking terms, briefly.</b> A <see cref="ProportionalDock"/> is a row or column whose
/// children share the space and are separated by draggable splitters. A
/// <see cref="ToolDock"/> holds panels. A <see cref="DocumentDock"/> holds the tabbed
/// documents. The root holds all of it plus any floating windows.
/// </para>
/// <para>
/// <b>Identity matters.</b> Every dockable is given a stable <c>Id</c>, because that is what
/// a saved layout stores. Changing an Id orphans that panel in layouts saved by earlier
/// builds — the layout loader is written to tolerate that, but the panel comes back at its
/// default position rather than where the artist left it.
/// </para>
/// </remarks>
public sealed class XpDockFactory : Factory
{
    /// <summary>The identity of the dock that receives newly opened documents.</summary>
    public const string DocumentsDockId = "Documents";

    private readonly ToolboxViewModel _toolbox;
    private readonly LayersViewModel _layers;
    private readonly PaletteViewModel _palette;

    /// <summary>Initialises the factory with the panels it should place.</summary>
    /// <param name="toolbox">The toolbox panel.</param>
    /// <param name="layers">The layers panel.</param>
    /// <param name="palette">The colour panel.</param>
    public XpDockFactory(ToolboxViewModel toolbox, LayersViewModel layers, PaletteViewModel palette)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(palette);

        _toolbox = toolbox;
        _layers = layers;
        _palette = palette;
    }

    /// <summary>The dock that new documents are added to.</summary>
    public IDocumentDock? DocumentDock { get; private set; }

    /// <inheritdoc />
    public override IRootDock CreateLayout()
    {
        PanelTool toolboxPanel = new()
        {
            Id = "Toolbox",
            Title = "Tools",
            Content = _toolbox,
            CanPin = true,
            CanFloat = true,
        };

        PanelTool layersPanel = new()
        {
            Id = "Layers",
            Title = "Layers",
            Content = _layers,
            CanPin = true,
            CanFloat = true,
        };

        PanelTool colorPanel = new()
        {
            Id = "Colour",
            Title = "Colour",
            Content = _palette,
            CanPin = true,
            CanFloat = true,
        };

        DocumentDock documents = new()
        {
            Id = DocumentsDockId,
            Title = "Documents",
            IsCollapsable = false,
            VisibleDockables = CreateList<IDockable>(),

            // The "+" on the tab strip is hidden: a new document needs a size, which means
            // the New dialog, and that belongs to the shell rather than to the tab strip.
            CanCreateDocument = false,
        };

        DocumentDock = documents;

        ToolDock leftDock = new()
        {
            Id = "LeftDock",
            Title = "LeftDock",
            Alignment = Alignment.Left,
            Proportion = 0.06,
            VisibleDockables = CreateList<IDockable>(toolboxPanel),
            ActiveDockable = toolboxPanel,
        };

        // Layers above colour in one column, because that is the order they are reached in:
        // pick the layer, then pick the colour to put on it.
        ToolDock rightTopDock = new()
        {
            Id = "RightTopDock",
            Title = "RightTopDock",
            Alignment = Alignment.Right,
            Proportion = 0.55,
            VisibleDockables = CreateList<IDockable>(layersPanel),
            ActiveDockable = layersPanel,
        };

        ToolDock rightBottomDock = new()
        {
            Id = "RightBottomDock",
            Title = "RightBottomDock",
            Alignment = Alignment.Right,
            Proportion = 0.45,
            VisibleDockables = CreateList<IDockable>(colorPanel),
            ActiveDockable = colorPanel,
        };

        ProportionalDock rightColumn = new()
        {
            Id = "RightColumn",
            Title = "RightColumn",
            Orientation = Orientation.Vertical,
            Proportion = 0.22,
            VisibleDockables = CreateList<IDockable>(
                rightTopDock,
                new ProportionalDockSplitter(),
                rightBottomDock),
        };

        ProportionalDock mainRow = new()
        {
            Id = "MainRow",
            Title = "MainRow",
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(
                leftDock,
                new ProportionalDockSplitter(),
                documents,
                new ProportionalDockSplitter(),
                rightColumn),
        };

        RootDock root = new()
        {
            Id = "Root",
            Title = "Root",
            IsCollapsable = false,
            VisibleDockables = CreateList<IDockable>(mainRow),
            ActiveDockable = mainRow,
            DefaultDockable = mainRow,
        };

        return root;
    }

    /// <inheritdoc />
    public override void InitLayout(IDockable layout)
    {
        // Without a host window locator, tearing a panel off the shell has nowhere to go and
        // the panel is simply removed from the layout — it disappears, with no window and no
        // way back short of resetting. This is the one line that makes floating work.
        DefaultHostWindowLocator = () => new HostWindow();

        // Dockables that were serialised without a context resolve through this. Returning a
        // plain object rather than null keeps a restored layout from faulting on a panel
        // whose content is reattached a moment later by RestorePanelContent.
        DefaultContextLocator = () => new object();

        // The locators are how the docking system re-attaches a layout that was loaded from
        // disk: the saved file stores ids, and these turn an id back into the live object.
        // Without them a restored panel would come back as an empty tab.
        DockableLocator = new Dictionary<string, Func<IDockable?>>
        {
            ["Toolbox"] = () => FindDockable("Toolbox"),
            ["Layers"] = () => FindDockable("Layers"),
            ["Colour"] = () => FindDockable("Colour"),
        };

        base.InitLayout(layout);

        // Remember where documents go, so opening a file after a layout restore still lands
        // in the right place rather than creating a second document area.
        DocumentDock = FindDockable(DocumentsDockId) as IDocumentDock ?? DocumentDock;
    }

    /// <summary>Restores the panel content on a layout that was loaded from a saved file.</summary>
    /// <param name="layout">The freshly deserialised layout.</param>
    /// <remarks>
    /// A saved layout stores structure, not view models: the serialiser has no way to write
    /// out a live toolbox. Every <see cref="PanelTool"/> therefore comes back with its
    /// <see cref="PanelTool.Content"/> empty, and this puts the real panels back by id. A
    /// panel whose id is not recognised — one removed in a later version — is left alone and
    /// simply renders empty rather than throwing.
    /// </remarks>
    public static void RestorePanelContent(
        IDockable layout,
        ToolboxViewModel toolbox,
        LayersViewModel layers,
        PaletteViewModel palette)
    {
        ArgumentNullException.ThrowIfNull(layout);

        foreach (IDockable dockable in Enumerate(layout))
        {
            if (dockable is not PanelTool panel)
            {
                continue;
            }

            panel.Content = panel.Id switch
            {
                "Toolbox" => toolbox,
                "Layers" => layers,
                "Colour" => palette,
                _ => panel.Content,
            };
        }
    }

    /// <summary>
    /// Removes any document tabs that came back with a restored layout.
    /// </summary>
    /// <param name="layout">The freshly deserialised layout.</param>
    /// <remarks>
    /// A saved layout records where things sit, not what was open — the artwork lives in
    /// <c>.xpxel</c> files, not in a settings file. A document that survives deserialisation
    /// is therefore an empty shell with no workspace behind it, and showing it would give the
    /// artist a tab that looks like their work and is not. Dropping them leaves the document
    /// area empty, which the shell immediately fills with a new document.
    /// </remarks>
    public static void RemoveRestoredDocuments(IDockable layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        foreach (IDockable dockable in Enumerate(layout).ToList())
        {
            if (dockable is not IDock { VisibleDockables: { } children })
            {
                continue;
            }

            for (int i = children.Count - 1; i >= 0; i--)
            {
                if (children[i] is IDocument)
                {
                    children.RemoveAt(i);
                }
            }

            if (dockable is IDock dock && dock.ActiveDockable is IDocument)
            {
                dock.ActiveDockable = null;
            }
        }
    }

    /// <summary>Walks a layout tree, including the contents of floating windows.</summary>
    public static IEnumerable<IDockable> Enumerate(IDockable dockable)
    {
        ArgumentNullException.ThrowIfNull(dockable);

        yield return dockable;

        if (dockable is IDock { VisibleDockables: { } children })
        {
            foreach (IDockable child in children)
            {
                foreach (IDockable descendant in Enumerate(child))
                {
                    yield return descendant;
                }
            }
        }

        if (dockable is IRootDock { Windows: { } windows })
        {
            foreach (IDockWindow window in windows)
            {
                if (window.Layout is { } windowLayout)
                {
                    foreach (IDockable descendant in Enumerate(windowLayout))
                    {
                        yield return descendant;
                    }
                }
            }
        }
    }

    private IDockable? FindDockable(string id) =>
        CurrentRootDock is { } root ? Enumerate(root).FirstOrDefault(d => d.Id == id) : null;
}
