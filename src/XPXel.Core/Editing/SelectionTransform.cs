using XPXel.Core.Documents;
using XPXel.Core.History;
using XPXel.Core.Primitives;
using XPXel.Core.Selection;

namespace XPXel.Core.Editing;

/// <summary>
/// Holds the selected pixels off the layer so they can be dragged somewhere else, and puts
/// them down again as a single undoable move.
/// </summary>
/// <remarks>
/// <para>
/// <b>The pixels are lifted once and redrawn from the copy.</b> Reading them out of the layer
/// on every pointer move would smear: after the first step the layer no longer holds the
/// original artwork, so the second step would pick up whatever the first one left. Taking one
/// copy at the start and always stamping from that is what makes dragging around and back again
/// leave the document exactly as it was.
/// </para>
/// <para>
/// <b>The whole drag is one undo entry.</b> Every move resets the edit session to the pixels
/// that were there before the transform began and redraws from there, which is the same
/// technique the shape tools use for their live preview. The artist gets one "Move Selection"
/// in the Edit menu however far they dragged, and undoing it restores both the hole and the
/// artwork in one step.
/// </para>
/// <para>
/// <b>The session ignores the selection, and it must.</b> Everything else edits through a
/// session that clips to the selection so no tool can escape it. This is the one operation whose
/// entire purpose is to write outside it — the pixels are going somewhere they are not yet
/// selected — so it clips to the layer instead.
/// </para>
/// <para>
/// <b>The marquee travels with the pixels.</b> Committing moves the selection to where the
/// artwork landed, so a move can be followed by another move, and so an undo does not leave the
/// marquee sitting over the hole the artwork used to fill.
/// </para>
/// <para>
/// <b>A paste is the same thing with nothing lifted.</b> Pasted pixels float over the layer
/// exactly as moved ones do — draggable, nudgeable with the arrow keys, placed with Enter and
/// abandoned with Escape — and the only difference is that no hole is punched where they came
/// from, because they did not come from anywhere on this layer. Sharing one class is what makes
/// paste land already in the artist's hand rather than stamped down wherever the program chose;
/// see <see cref="BeginPaste"/>.
/// </para>
/// </remarks>
public sealed class SelectionTransform
{
    /// <summary>What the Edit menu calls a move, and what the history entry is named.</summary>
    private const string MoveActionName = "Move Selection";

    /// <summary>What it calls a paste.</summary>
    private const string PasteActionName = "Paste";

    private readonly DocumentWorkspace _workspace;
    private readonly RasterEditSession _session;

    /// <summary>The selection to put back on undo, or <see langword="null"/> if there was none.</summary>
    private readonly SelectionMask? _selectionBefore;

    private readonly ImageRect _source;

    /// <summary>The floating artwork, row by row across <see cref="_source"/>.</summary>
    private readonly PixelColor[] _lifted;

    /// <summary>
    /// How much of each of those pixels belongs to the shape being moved, row by row.
    /// </summary>
    /// <remarks>
    /// Coverage rather than a plain flag, so that the marquee this produces on commit is the
    /// one the artist actually had rather than a flattened version of it.
    /// </remarks>
    private readonly byte[] _coverage;

    /// <summary>
    /// Whether the artwork was taken off the layer, and so leaves a hole behind it.
    /// </summary>
    /// <remarks>
    /// False for a paste, where the pixels came from the clipboard: clearing the region under
    /// them would punch a hole in artwork the artist never asked to change.
    /// </remarks>
    private readonly bool _leavesAHole;

    private SelectionTransform(
        DocumentWorkspace workspace,
        RasterEditSession session,
        SelectionMask? selectionBefore,
        ImageRect source,
        PixelColor[] lifted,
        byte[] coverage,
        bool leavesAHole,
        string actionName)
    {
        _workspace = workspace;
        _session = session;
        _selectionBefore = selectionBefore;
        _source = source;
        _lifted = lifted;
        _coverage = coverage;
        _leavesAHole = leavesAHole;
        ActionName = actionName;
    }

    /// <summary>What the Edit menu calls this transform.</summary>
    public string ActionName { get; }

    /// <summary>
    /// Gets a value indicating whether this is the selection itself in the air, rather than a
    /// clip arriving from the clipboard.
    /// </summary>
    /// <remarks>
    /// The canvas asks so that it can outline the right thing. A moved selection is whatever
    /// shape the artist marked and the marching ants travel with it; a pasted clip is a
    /// rectangle that has nothing to do with whatever happened to be marked before it arrived,
    /// so the box around it is the only honest outline to draw.
    /// </remarks>
    public bool MovesTheSelection => _leavesAHole;

    /// <summary>How far the artwork has been moved from where it was lifted.</summary>
    public ImagePoint Offset { get; private set; }

    /// <summary>Where the selected artwork sat when it was lifted.</summary>
    public ImageRect SourceBounds => _source;

    /// <summary>Where the selected artwork sits now.</summary>
    public ImageRect Bounds => _source.Translate(Offset.X, Offset.Y);

    /// <summary>Gets a value indicating whether the artwork has actually gone anywhere.</summary>
    public bool HasMoved => Offset != ImagePoint.Origin;

    /// <summary>Lifts the current selection off the active layer.</summary>
    /// <param name="workspace">The document to work on.</param>
    /// <returns>
    /// The transform, or <see langword="null"/> when there is nothing to move: no selection, an
    /// empty one, or a layer that cannot be drawn on.
    /// </returns>
    public static SelectionTransform? Begin(DocumentWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (workspace.Selection is not { IsEmpty: false } selection)
        {
            return null;
        }

        if (workspace.GetOrCreateActiveCel() is not { } cel)
        {
            return null;
        }

        ImageRect source = selection.Bounds;
        int count = source.Width * source.Height;

        PixelColor[] lifted = new PixelColor[count];
        byte[] coverage = new byte[count];
        bool any = false;

        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                int index = (y * source.Width) + x;
                int sx = source.Left + x;
                int sy = source.Top + y;

                byte covered = selection.GetCoverage(sx, sy);

                if (covered == 0)
                {
                    continue;
                }

                coverage[index] = covered;
                lifted[index] = cel.Pixels.GetPixelOrTransparent(sx - cel.Offset.X, sy - cel.Offset.Y);
                any = true;
            }
        }

        if (!any)
        {
            return null;
        }

        // No selection is passed to the session on purpose; see the type remarks.
        RasterEditSession session = RasterEditSession.Begin(workspace.Document, cel, MoveActionName);

        return new SelectionTransform(
            workspace,
            session,
            selection.Clone(),
            source,
            lifted,
            coverage,
            leavesAHole: true,
            MoveActionName);
    }

    /// <summary>Floats a clip over the active layer, ready to be placed.</summary>
    /// <param name="workspace">The document to paste into.</param>
    /// <param name="clip">The pixels to paste.</param>
    /// <param name="origin">Where the clip's top-left corner should land.</param>
    /// <returns>
    /// The transform, or <see langword="null"/> when there is nothing to paste into — a locked
    /// or hidden layer, or one that is not a drawing layer — or nothing worth pasting.
    /// </returns>
    /// <remarks>
    /// The pixels are stamped down straight away rather than on the first drag, so the artist
    /// sees what arrived and where. It is still floating at that point: dragging it, nudging it
    /// or pressing Escape all work, and nothing reaches the history until it is placed.
    /// </remarks>
    public static SelectionTransform? BeginPaste(DocumentWorkspace workspace, PixelClip clip, ImagePoint origin)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(clip);

        if (clip.IsEmpty || workspace.GetOrCreateActiveCel() is not { } cel)
        {
            return null;
        }

        ImageRect source = new(origin.X, origin.Y, clip.Size.Width, clip.Size.Height);

        RasterEditSession session = RasterEditSession.Begin(workspace.Document, cel, PasteActionName);

        SelectionTransform transform = new(
            workspace,
            session,
            workspace.Selection?.Clone(),
            source,
            clip.Pixels.ToArray(),
            clip.Coverage.ToArray(),
            leavesAHole: false,
            PasteActionName);

        transform.Render();
        return transform;
    }

    /// <summary>Moves the lifted artwork to a given displacement from where it started.</summary>
    /// <param name="offset">How far from the original position to put it.</param>
    public void MoveTo(ImagePoint offset)
    {
        if (offset == Offset)
        {
            return;
        }

        Offset = offset;
        Render();
    }

    /// <summary>Nudges the lifted artwork, which is what the arrow keys do.</summary>
    /// <param name="deltaX">Horizontal movement in pixels.</param>
    /// <param name="deltaY">Vertical movement in pixels.</param>
    public void MoveBy(int deltaX, int deltaY) => MoveTo(Offset.Translate(deltaX, deltaY));

    /// <summary>
    /// Puts the artwork down and produces the history entry for the whole move.
    /// </summary>
    /// <returns>
    /// The command to record, or <see langword="null"/> if the artwork ended up exactly where it
    /// started and nothing therefore changed.
    /// </returns>
    /// <remarks>
    /// The change has already been applied to the document by the time this returns, so the
    /// caller must record the command with <see cref="DocumentHistory.Push"/> rather than
    /// <see cref="DocumentHistory.Execute"/>.
    /// </remarks>
    public IUndoableCommand? Commit()
    {
        if (_session.Commit() is not { } pixels)
        {
            return null;
        }

        SetSelectionCommand marquee = new(_workspace, _selectionBefore, TranslateSelection(), ActionName);
        marquee.Redo();

        return new CompositeCommand(ActionName, [pixels, marquee]);
    }

    /// <summary>Abandons the move, putting every pixel back where it was.</summary>
    public void Cancel() => _session.Rollback();

    /// <summary>Redraws the lifted artwork at the current offset.</summary>
    /// <remarks>
    /// Order matters: the hole is cleared before the artwork is stamped, so that a move short
    /// enough for the two to overlap keeps the artwork rather than punching a hole through it.
    /// </remarks>
    private void Render()
    {
        // Back to the layer as it was before this transform touched it, so each position is
        // drawn from the original artwork rather than from the last position's leavings.
        _session.ResetToOriginal();

        // Dragged back to where it started, moved artwork is home and the reset has already put
        // it there. Punching the hole and stamping it back would leave the session holding a
        // dirty region that nothing actually changed, and commit a do-nothing undo entry. A
        // paste has no such home: at offset zero it is still the thing that has to be stamped.
        if (_leavesAHole && !HasMoved)
        {
            return;
        }

        if (_leavesAHole)
        {
            for (int y = 0; y < _source.Height; y++)
            {
                for (int x = 0; x < _source.Width; x++)
                {
                    if (_coverage[(y * _source.Width) + x] > 0)
                    {
                        _session.SetPixel(_source.Left + x, _source.Top + y, PixelColor.Transparent);
                    }
                }
            }
        }

        for (int y = 0; y < _source.Height; y++)
        {
            for (int x = 0; x < _source.Width; x++)
            {
                int index = (y * _source.Width) + x;

                if (_coverage[index] > 0)
                {
                    _session.SetPixel(
                        _source.Left + x + Offset.X,
                        _source.Top + y + Offset.Y,
                        _lifted[index]);
                }
            }
        }

        _session.PublishInterimChanges();
    }

    /// <summary>
    /// Builds the marquee around wherever the artwork has ended up.
    /// </summary>
    /// <remarks>
    /// Traced from the transform's own coverage rather than from the selection it started with,
    /// so that it says the same thing for a paste — which began with no selection at all, and
    /// ends with the pasted shape marked, ready to be moved again or deleted.
    /// </remarks>
    private SelectionMask TranslateSelection()
    {
        SelectionMask moved = new(_workspace.Document.Size);

        for (int y = 0; y < _source.Height; y++)
        {
            for (int x = 0; x < _source.Width; x++)
            {
                byte coverage = _coverage[(y * _source.Width) + x];

                if (coverage > 0)
                {
                    // Anything pushed off the canvas is simply dropped, which matches what
                    // happened to the pixels: the session could not write them either.
                    moved.SetCoverage(
                        _source.Left + x + Offset.X,
                        _source.Top + y + Offset.Y,
                        coverage);
                }
            }
        }

        moved.RecalculateBounds();
        return moved;
    }
}
