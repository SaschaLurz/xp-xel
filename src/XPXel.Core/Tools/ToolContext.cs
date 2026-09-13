using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.History;
using XPXel.Core.Primitives;
using XPXel.Core.Selection;
using XPXel.Core.Text;

namespace XPXel.Core.Tools;

/// <summary>
/// Everything a tool is allowed to see and change: the document being edited, the current
/// colours, the tool options, and the means to record an undoable edit.
/// </summary>
/// <remarks>
/// <para>
/// Passing a context to each callback — rather than giving tools constructor dependencies —
/// keeps tools stateless with respect to <em>which</em> document they act on. One pencil
/// instance serves every open document, and a tool can be unit-tested by handing it a
/// context built over a throwaway document.
/// </para>
/// <para>
/// <b>Tools must edit through <see cref="BeginRasterEdit"/>.</b> Writing to a cel's
/// <c>PixelBuffer</c> directly bypasses both the undo history and the selection, and is the
/// single easiest way to introduce a bug that only shows up when the artist presses Ctrl+Z.
/// It is also what routes a stroke to every layer the artist has marked rather than only to
/// the one being aimed at.
/// </para>
/// <para>
/// <b>There is no colour for the right mouse button.</b> A right press on the canvas opens the
/// tool's settings panel and never reaches a tool at all, so the secondary colour is reached
/// with X or from the palette. Tools used to carry a branch for painting with it that could
/// not run.
/// </para>
/// </remarks>
public sealed class ToolContext
{
    /// <summary>Initialises a context.</summary>
    /// <param name="workspace">The document being edited.</param>
    /// <param name="settings">The shared tool options.</param>
    /// <param name="colors">The primary and secondary colours.</param>
    /// <param name="textRasterizer">
    /// The font engine, for the tools that have to measure or re-render words. Defaults to the
    /// one that draws nothing, which is what a headless test gets.
    /// </param>
    public ToolContext(
        DocumentWorkspace workspace,
        ToolSettings settings,
        ColorSlots colors,
        ITextRasterizer? textRasterizer = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(colors);

        Workspace = workspace;
        Settings = settings;
        Colors = colors;
        TextRasterizer = textRasterizer ?? NullTextRasterizer.Instance;
    }

    /// <summary>The document being edited, together with its history and active layer.</summary>
    public DocumentWorkspace Workspace { get; }

    /// <summary>The shared tool options such as brush size and fill tolerance.</summary>
    public ToolSettings Settings { get; }

    /// <summary>The primary and secondary drawing colours.</summary>
    public ColorSlots Colors { get; }

    /// <summary>
    /// The font engine, for the tools and gestures that have to measure or re-render words.
    /// </summary>
    /// <remarks>
    /// It sits on the context rather than being injected into each tool because two things that
    /// are not the text tool need it: the move tool, which repositions a text layer by moving
    /// its origin and re-rendering, and the canvas, which opens an edit when the artist
    /// double-clicks a text layer with any tool at all in hand.
    /// </remarks>
    public ITextRasterizer TextRasterizer { get; }

    /// <summary>The document being edited.</summary>
    public PixelDocument Document => Workspace.Document;

    /// <summary>The undo history for the document being edited.</summary>
    public DocumentHistory History => Workspace.History;

    /// <summary>The active selection, or <see langword="null"/> when everything is editable.</summary>
    public SelectionMask? Selection => Workspace.Selection;

    /// <summary>
    /// Geometry the active tool wants the canvas to draw on top of the artwork while an
    /// interaction is in progress.
    /// </summary>
    /// <remarks>
    /// One overlay per document, so a marquee dragged on one tab does not appear on another.
    /// See <see cref="ToolOverlay"/> for why some previews go here and others are drawn
    /// straight onto the layer.
    /// </remarks>
    public ToolOverlay Overlay { get; } = new();

    /// <summary>
    /// Reads a pixel of the flattened image at the given image coordinate, or
    /// <see langword="null"/> when no flattened image is available.
    /// </summary>
    /// <remarks>
    /// The canvas control already maintains a composited copy of the document in order to
    /// draw it, so it supplies that here rather than every colour pick paying to flatten the
    /// document again. Tools must tolerate this being <see langword="null"/>, which is the
    /// case in unit tests and any headless use.
    /// </remarks>
    public Func<int, int, PixelColor>? CompositeSampler { get; set; }

    /// <summary>
    /// How many screen pixels one image pixel currently occupies — the canvas's zoom.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Almost nothing needs this, and nothing that draws may use it: a tool whose output changed
    /// with the zoom would produce different artwork depending on how closely the artist happened
    /// to be looking. It is here for the one thing that genuinely is a screen measurement — how
    /// near the pointer has to be to something before it counts as grabbing it. Six screen pixels
    /// is a fingertip at any zoom; six image pixels is most of a sprite.
    /// </para>
    /// <para>
    /// Defaults to 1, which is what a headless test or an unattached tool gets, and means image
    /// pixels and screen pixels are the same thing.
    /// </para>
    /// </remarks>
    public double ViewScale { get; set; } = 1.0;

    /// <summary>Gets a value indicating whether a drawing tool may currently make an edit.</summary>
    public bool CanEdit => Workspace.CanEditActiveLayer;

    /// <summary>
    /// Starts recording an undoable edit across every layer the artist has marked, creating
    /// a cel for the active frame on any of them that is still empty.
    /// </summary>
    /// <param name="actionName">The name shown in the Edit menu, for example "Pen-xel".</param>
    /// <param name="useSnapshot">
    /// <see langword="true"/> for operations that may touch most of the canvas, such as a
    /// bucket fill. See <see cref="RasterEditSession"/> for the trade-off.
    /// </param>
    /// <returns>
    /// The scope to write through, or <see langword="null"/> if there is no editable
    /// layer — in which case the tool should simply do nothing.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Every marked layer, not just the active one.</b> Marking several rows in the layers
    /// panel used to change what Delete, Cut, Copy and Ctrl+T did and leave drawing alone, which
    /// meant the same highlight meant two different things depending on what you did next.
    /// Drawing now follows the same rule as everything else. The canvas shows a badge beside the
    /// pointer whenever more than one layer would be written to, because a stroke landing on
    /// layers the artist has forgotten about is worth saying out loud.
    /// </para>
    /// <para>
    /// <b>The active layer comes first</b>, so <see cref="RasterEditScope.Primary"/> is the
    /// layer being aimed at — which is the one a flood fill has to read to decide what to fill.
    /// A locked, hidden or text layer among the marked set is skipped rather than refusing the
    /// whole stroke: the artist plainly meant the layers that can take paint.
    /// </para>
    /// </remarks>
    public RasterEditScope? BeginRasterEdit(string actionName, bool useSnapshot = false)
    {
        if (Workspace.GetOrCreateActiveCel() is not { } active)
        {
            return null;
        }

        List<Cel> cels = [active];

        foreach (LayerNode leaf in Workspace.GetSelectedLeaves())
        {
            if (leaf is not RasterLayer { IsLocked: false } layer
                || !layer.IsEffectivelyVisible
                || layer.Id == active.LayerId)
            {
                continue;
            }

            cels.Add(Document.GetOrCreateCel(layer, Workspace.ActiveFrame));
        }

        return RasterEditScope.Begin(Document, actionName, cels, Selection, useSnapshot);
    }

    /// <summary>
    /// Ends an operation and records it in the history, doing nothing if it turned out to
    /// change no pixels.
    /// </summary>
    /// <param name="scope">The scope to finish.</param>
    /// <returns><see langword="true"/> if a history entry was created.</returns>
    public bool CommitRasterEdit(RasterEditScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        IUndoableCommand? command = scope.Commit();
        if (command is null)
        {
            return false;
        }

        // The pixels are already in place - the artist watched them appear - so the command
        // is recorded rather than executed.
        History.Push(command);
        return true;
    }
}
