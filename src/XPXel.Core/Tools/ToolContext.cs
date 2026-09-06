using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.History;
using XPXel.Core.Primitives;
using XPXel.Core.Selection;

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
/// </para>
/// </remarks>
public sealed class ToolContext
{
    /// <summary>Initialises a context.</summary>
    /// <param name="workspace">The document being edited.</param>
    /// <param name="settings">The shared tool options.</param>
    /// <param name="colors">The primary and secondary colours.</param>
    public ToolContext(DocumentWorkspace workspace, ToolSettings settings, ColorSlots colors)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(colors);

        Workspace = workspace;
        Settings = settings;
        Colors = colors;
    }

    /// <summary>The document being edited, together with its history and active layer.</summary>
    public DocumentWorkspace Workspace { get; }

    /// <summary>The shared tool options such as brush size and fill tolerance.</summary>
    public ToolSettings Settings { get; }

    /// <summary>The primary and secondary drawing colours.</summary>
    public ColorSlots Colors { get; }

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

    /// <summary>Gets a value indicating whether a drawing tool may currently make an edit.</summary>
    public bool CanEdit => Workspace.CanEditActiveLayer;

    /// <summary>
    /// Returns the colour a button should apply: primary for the left button, secondary for
    /// the right.
    /// </summary>
    /// <param name="button">The button that was used.</param>
    /// <returns>The colour to draw with.</returns>
    public PixelColor GetColorForButton(ToolPointerButton button) =>
        button == ToolPointerButton.Right ? Colors.Secondary : Colors.Primary;

    /// <summary>
    /// Starts recording an undoable edit on the active layer's cel for the active frame,
    /// creating that cel if the layer is still empty.
    /// </summary>
    /// <param name="actionName">The name shown in the Edit menu, for example "Pencil".</param>
    /// <param name="useSnapshot">
    /// <see langword="true"/> for operations that may touch most of the canvas, such as a
    /// bucket fill. See <see cref="RasterEditSession"/> for the trade-off.
    /// </param>
    /// <returns>
    /// The session to write through, or <see langword="null"/> if there is no editable
    /// layer — in which case the tool should simply do nothing.
    /// </returns>
    public RasterEditSession? BeginRasterEdit(string actionName, bool useSnapshot = false)
    {
        Cel? cel = Workspace.GetOrCreateActiveCel();
        if (cel is null)
        {
            return null;
        }

        return useSnapshot
            ? RasterEditSession.BeginWithSnapshot(Document, cel, actionName, Selection)
            : RasterEditSession.Begin(Document, cel, actionName, Selection);
    }

    /// <summary>
    /// Ends a session and records it in the history, doing nothing if the operation turned
    /// out to change no pixels.
    /// </summary>
    /// <param name="session">The session to finish.</param>
    /// <returns><see langword="true"/> if a history entry was created.</returns>
    public bool CommitRasterEdit(RasterEditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        IUndoableCommand? command = session.Commit();
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
