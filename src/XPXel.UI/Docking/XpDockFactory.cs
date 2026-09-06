using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using XPXel.UI.ViewModels;

namespace XPXel.UI.Docking;

/// <summary>
/// One panel: its identity, its title, the view model it shows, and where it belongs when the
/// layout is built or the panel is brought back after being closed.
/// </summary>
/// <param name="Id">
/// The panel's stable identity. This is the key a saved layout stores, so changing it orphans
/// the panel in layouts written by earlier builds.
/// </param>
/// <param name="Title">The name on the panel's tab and in the View menu.</param>
/// <param name="Content">The panel's view model.</param>
/// <param name="HomeDockId">
/// The dock this panel is created in, and the one it returns to when it is switched back on
/// after having been closed.
/// </param>
/// <param name="HomeAlignment">
/// Which side of the shell the panel belongs on. Closing the last panel in a dock collapses
/// that dock away, so a panel switched back on afterwards has no home to return to; this is
/// what keeps it landing on the correct side rather than in whichever dock happens to be
/// first in the tree.
/// </param>
public sealed record PanelDefinition(
    string Id,
    string Title,
    object Content,
    string HomeDockId,
    Alignment HomeAlignment);

/// <summary>
/// Builds the editor's default arrangement of panels and document tabs.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the one place that describes the default layout.</b> Once the application has
/// started, the arrangement belongs to the artist: panels can be dragged into other panels,
/// torn off into floating windows, tabbed together, switched off, or closed. The saved layout
/// is restored on the next launch, and "Reset to Default Layout" throws it away and calls back
/// into here.
/// </para>
/// <para>
/// <b>The panels are a table, not a sequence of statements.</b> <see cref="Definitions"/>
/// describes every panel once, and the default layout, the View menu's checklist, the
/// re-attachment of a restored layout and the "bring this panel back" path all read from it.
/// Adding a panel is one entry there plus a data template; nothing else in this file changes.
/// </para>
/// <para>
/// <b>Docking terms, briefly.</b> A <see cref="ProportionalDock"/> is a row or column whose
/// children share the space and are separated by draggable splitters. A
/// <see cref="ToolDock"/> holds panels. A <see cref="DocumentDock"/> holds the tabbed
/// documents. The root holds all of it plus any floating windows.
/// </para>
/// </remarks>
public sealed class XpDockFactory : Factory
{
    /// <summary>The identity of the dock that receives newly opened documents.</summary>
    public const string DocumentsDockId = "Documents";

    private const string LeftTopDockId = "LeftTopDock";
    private const string LeftBottomDockId = "LeftBottomDock";
    private const string RightTopDockId = "RightTopDock";
    private const string RightBottomDockId = "RightBottomDock";
    private const string BottomDockId = "BottomDock";

    /// <summary>The live panel objects, by id, so a closed panel can be put back.</summary>
    /// <remarks>
    /// A panel that has been switched off is removed from the layout tree but is not
    /// destroyed — its view model is a singleton holding the artist's brush, colours and
    /// selection. Keeping the wrapper here as well means switching the panel back on restores
    /// the same object rather than a fresh one that has forgotten everything.
    /// </remarks>
    private readonly Dictionary<string, PanelTool> _panels = new(StringComparer.Ordinal);

    /// <summary>Initialises the factory with the panels it should place.</summary>
    /// <param name="toolbox">The toolbox panel.</param>
    /// <param name="layers">The layers panel.</param>
    /// <param name="palette">The colour panel.</param>
    /// <param name="brush">The brush panel.</param>
    /// <param name="text">The text panel.</param>
    /// <param name="timeline">The timeline panel.</param>
    /// <param name="preview">The animation preview panel.</param>
    public XpDockFactory(
        ToolboxViewModel toolbox,
        LayersViewModel layers,
        PaletteViewModel palette,
        BrushPanelViewModel brush,
        TextPanelViewModel text,
        TimelineViewModel timeline,
        PreviewPanelViewModel preview)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(brush);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(preview);

        Definitions =
        [
            new PanelDefinition("Toolbox", "Tools", toolbox, LeftTopDockId, Alignment.Left),
            new PanelDefinition("Brush", "Brush", brush, LeftBottomDockId, Alignment.Left),
            new PanelDefinition("Text", "Text", text, LeftBottomDockId, Alignment.Left),
            new PanelDefinition("Layers", "Layers", layers, RightTopDockId, Alignment.Right),
            new PanelDefinition("Colour", "Colour", palette, RightBottomDockId, Alignment.Right),
            new PanelDefinition("Preview", "Preview", preview, RightBottomDockId, Alignment.Right),
            new PanelDefinition("Timeline", "Timeline", timeline, BottomDockId, Alignment.Bottom),
        ];
    }

    /// <summary>Every panel the editor has, in the order the View menu lists them.</summary>
    public IReadOnlyList<PanelDefinition> Definitions { get; }

    /// <summary>The dock that new documents are added to.</summary>
    public IDocumentDock? DocumentDock { get; private set; }

    /// <inheritdoc />
    public override IRootDock CreateLayout()
    {
        _panels.Clear();

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

        // The left column holds the tools and the options for whichever one is chosen, which
        // is the order they are used in: pick a tool, then set it up. The toolbox takes the
        // smaller share because it only has to fit a grid of icons, while the brush and text
        // panels need width for their sliders and their font list.
        //
        // The column is wide enough for the toolbox tab to show its own title. The first
        // version of this layout was narrow enough that the tab strip's buttons squeezed the
        // title off the panel entirely, which made the toolbox look broken rather than compact.
        ToolDock leftTop = CreateToolDock(LeftTopDockId, Alignment.Left, 0.34, "Toolbox");

        // Brush and text are tabbed together: both answer "how does the current tool behave?"
        // and only one of them is ever relevant at a time.
        ToolDock leftBottom = CreateToolDock(LeftBottomDockId, Alignment.Left, 0.66, "Brush", "Text");

        ProportionalDock leftColumn = new()
        {
            Id = "LeftColumn",
            Title = "LeftColumn",
            Orientation = Orientation.Vertical,
            Proportion = 0.16,
            VisibleDockables = CreateList<IDockable>(
                leftTop,
                new ProportionalDockSplitter(),
                leftBottom),
        };

        // Layers above colour, because that is the order they are reached in: pick the layer,
        // then pick the colour to put on it.
        ToolDock rightTop = CreateToolDock(RightTopDockId, Alignment.Right, 0.55, "Layers");

        // The preview is tabbed behind the colour panel rather than given its own space: it
        // matters intensely while animating and not at all the rest of the time, and a panel
        // that is only sometimes wanted is what tabs are for.
        ToolDock rightBottom = CreateToolDock(RightBottomDockId, Alignment.Right, 0.45, "Colour", "Preview");

        ProportionalDock rightColumn = new()
        {
            Id = "RightColumn",
            Title = "RightColumn",
            Orientation = Orientation.Vertical,
            Proportion = 0.22,
            VisibleDockables = CreateList<IDockable>(
                rightTop,
                new ProportionalDockSplitter(),
                rightBottom),
        };

        ProportionalDock mainRow = new()
        {
            Id = "MainRow",
            Title = "MainRow",
            Orientation = Orientation.Horizontal,
            Proportion = 0.74,
            VisibleDockables = CreateList<IDockable>(
                leftColumn,
                new ProportionalDockSplitter(),
                documents,
                new ProportionalDockSplitter(),
                rightColumn),
        };

        // The timeline spans the full width beneath everything else. It has to: a cel grid is
        // as wide as the animation is long, and putting it in a side column would make a
        // twenty-frame walk cycle unreadable.
        ToolDock bottomDock = CreateToolDock(BottomDockId, Alignment.Bottom, 0.26, "Timeline");

        ProportionalDock shell = new()
        {
            Id = "Shell",
            Title = "Shell",
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(
                mainRow,
                new ProportionalDockSplitter(),
                bottomDock),
        };

        RootDock root = new()
        {
            Id = "Root",
            Title = "Root",
            IsCollapsable = false,
            VisibleDockables = CreateList<IDockable>(shell),
            ActiveDockable = shell,
            DefaultDockable = shell,
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
        DockableLocator = Definitions.ToDictionary(
            d => d.Id,
            d => (Func<IDockable?>)(() => FindDockable(d.Id)),
            StringComparer.Ordinal);

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
    public void RestorePanelContent(IDockable layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        Dictionary<string, PanelDefinition> byId = Definitions.ToDictionary(d => d.Id, StringComparer.Ordinal);

        // The restored wrappers replace the ones this factory created, so that switching a
        // panel off and on again puts back the object that is actually in the artist's layout.
        _panels.Clear();

        foreach (IDockable dockable in Enumerate(layout))
        {
            if (dockable is not PanelTool panel || panel.Id is not { } id || !byId.TryGetValue(id, out PanelDefinition? definition))
            {
                continue;
            }

            panel.Content = definition.Content;
            panel.Title = definition.Title;
            _panels[id] = panel;
        }

        // A panel added in a newer build is missing from an older saved layout entirely. It is
        // created here so that it exists and can be switched on, rather than being unreachable
        // until the artist resets the whole arrangement.
        foreach (PanelDefinition definition in Definitions)
        {
            if (!_panels.ContainsKey(definition.Id))
            {
                _panels[definition.Id] = CreatePanel(definition);
            }
        }
    }

    /// <summary>Gets a value indicating whether a panel is currently somewhere in the layout.</summary>
    /// <param name="panelId">The panel's identity.</param>
    /// <returns><see langword="true"/> if the panel is present, whether or not it is in front.</returns>
    public bool IsPanelVisible(string panelId) =>
        CurrentRootDock is { } root
        && Enumerate(root).OfType<PanelTool>().Any(p => p.Id == panelId);

    /// <summary>Brings a panel to the front, adding it back to the layout if it was switched off.</summary>
    /// <param name="panelId">The panel's identity.</param>
    /// <returns>
    /// <see langword="false"/> if the panel could not be placed, which happens only when the
    /// layout has lost every dock that could hold it. The caller's honest repair is then to
    /// rebuild the default arrangement.
    /// </returns>
    public bool ShowPanel(string panelId)
    {
        if (CurrentRootDock is not { } root)
        {
            return false;
        }

        if (Enumerate(root).OfType<PanelTool>().FirstOrDefault(p => p.Id == panelId) is { } existing)
        {
            SetActiveDockable(existing);
            return true;
        }

        if (Definitions.FirstOrDefault(d => d.Id == panelId) is not { } definition)
        {
            return false;
        }

        if (!_panels.TryGetValue(panelId, out PanelTool? panel))
        {
            panel = CreatePanel(definition);
            _panels[panelId] = panel;
        }

        // Its own dock if it still exists. Closing the last panel in a dock collapses that
        // dock away, so a panel coming back may find its home gone; the next best answer is a
        // dock on the side it belongs to, and only then any dock at all. Landing on the wrong
        // side is jarring — the artist asked for the colour panel and it appears among the
        // tools — so the alignment step is worth the extra lookup.
        IDock? home = FindDockable(definition.HomeDockId) as IDock
            ?? Enumerate(root).OfType<ToolDock>().FirstOrDefault(d => d.Alignment == definition.HomeAlignment)
            ?? Enumerate(root).OfType<ToolDock>().FirstOrDefault();

        if (home is null)
        {
            return false;
        }

        AddDockable(home, panel);
        SetActiveDockable(panel);
        return true;
    }

    /// <summary>Switches a panel off, removing it from the layout without destroying it.</summary>
    /// <param name="panelId">The panel's identity.</param>
    public void HidePanel(string panelId)
    {
        if (CurrentRootDock is not { } root)
        {
            return;
        }

        if (Enumerate(root).OfType<PanelTool>().FirstOrDefault(p => p.Id == panelId) is { } panel)
        {
            CloseDockable(panel);
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
    /// <param name="dockable">The root to walk from.</param>
    /// <returns>Every dockable beneath it, including itself.</returns>
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

    /// <summary>Builds a tool dock holding the named panels, the first of which starts active.</summary>
    private ToolDock CreateToolDock(string id, Alignment alignment, double proportion, params string[] panelIds)
    {
        List<IDockable> contents = [];

        foreach (string panelId in panelIds)
        {
            PanelDefinition definition = Definitions.First(d => d.Id == panelId);
            PanelTool panel = CreatePanel(definition);
            _panels[definition.Id] = panel;
            contents.Add(panel);
        }

        return new ToolDock
        {
            Id = id,
            Title = id,
            Alignment = alignment,
            Proportion = proportion,
            VisibleDockables = CreateList(contents.ToArray()),
            ActiveDockable = contents.Count > 0 ? contents[0] : null,
        };
    }

    private static PanelTool CreatePanel(PanelDefinition definition) => new()
    {
        Id = definition.Id,
        Title = definition.Title,
        Content = definition.Content,
        CanPin = true,
        CanFloat = true,
    };

    private IDockable? FindDockable(string id) =>
        CurrentRootDock is { } root ? Enumerate(root).FirstOrDefault(d => d.Id == id) : null;
}
