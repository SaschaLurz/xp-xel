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
/// <b>Placing the pixels clears the marks.</b> Enter means the same thing everywhere on the
/// canvas — done — so a move ends the way a transform and a paste do, with the marching ants
/// gone rather than left over the artwork for the artist to dismiss separately. Undo brings
/// them back along with everything else. A drag that ended where it started clears nothing,
/// because it answered nothing.
/// </para>
/// <para>
/// <b>Nothing is lost over the canvas edge.</b> Artwork dragged half off the side is kept in
/// the layer, hidden rather than cut, exactly as it is when a whole layer is moved: the cel is
/// grown to hold it as the pixels are placed. The only things in XP-xel that destroy pixels are
/// the eraser and the commands that say so.
/// </para>
/// <para>
/// <b>A paste is not this.</b> Pasted pixels float in a <see cref="FreeTransform"/> box
/// instead, because what arrives from the clipboard usually needs resizing as well as placing
/// and doing those as two separately undoable acts is doing the job twice. This class is the
/// plain drag of pixels that are already the right size.
/// </para>
/// </remarks>
public sealed class SelectionTransform
{
    /// <summary>What the Edit menu calls a move, and what the history entry is named.</summary>
    private const string MoveActionName = "Move Selection";

    private readonly DocumentWorkspace _workspace;

    /// <summary>The cel the artwork was lifted from, and the preview is drawn into.</summary>
    private readonly Cel _cel;

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

    private SelectionTransform(
        DocumentWorkspace workspace,
        Cel cel,
        RasterEditSession session,
        SelectionMask? selectionBefore,
        ImageRect source,
        PixelColor[] lifted,
        byte[] coverage,
        string actionName)
    {
        _workspace = workspace;
        _cel = cel;
        _session = session;
        _selectionBefore = selectionBefore;
        _source = source;
        _lifted = lifted;
        _coverage = coverage;
        ActionName = actionName;
    }

    /// <summary>What the Edit menu calls this transform.</summary>
    public string ActionName { get; }

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
            cel,
            session,
            selection.Clone(),
            source,
            lifted,
            coverage,
            MoveActionName);
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
        // The preview is thrown away first and the artwork placed again from scratch, so that
        // the cel can be grown before anything is written and the part of the drag that went
        // over the canvas edge survives. See the type remarks.
        _session.Rollback();

        if (!HasMoved)
        {
            return null;
        }

        List<IUndoableCommand> parts = [];
        Cel target = _cel;

        if (_cel.CreateExpanded(Bounds, _workspace.Document.Bounds) is { } grown
            && !ReferenceEquals(grown, _cel))
        {
            _workspace.Document.Cels.Set(grown);
            parts.Add(new ReplaceCelCommand(ActionName, _workspace.Document, _cel, grown));
            target = grown;
        }

        // No selection is passed to the session, for the same reason the preview's is not:
        // this operation's whole business is writing where the artwork is going, which is
        // somewhere it is not yet marked.
        RasterEditSession session = RasterEditSession.Begin(_workspace.Document, target, ActionName);

        Erase(session);
        Stamp(session);

        if (session.Commit() is { } pixels)
        {
            parts.Add(pixels);
        }

        if (parts.Count == 0)
        {
            return null;
        }

        SetSelectionCommand deselect = new(_workspace, _selectionBefore, null, ActionName);
        deselect.Redo();
        parts.Add(deselect);

        return new CompositeCommand(ActionName, parts);
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

        // Dragged back to where it started, the artwork is home and the reset has already put
        // it there. Punching the hole and stamping it back would leave the session holding a
        // dirty region that nothing actually changed, and commit a do-nothing undo entry.
        if (!HasMoved)
        {
            return;
        }

        Erase(_session);
        Stamp(_session);

        _session.PublishInterimChanges();
    }

    /// <summary>Clears the space the artwork was lifted from.</summary>
    private void Erase(RasterEditSession session)
    {
        for (int y = 0; y < _source.Height; y++)
        {
            for (int x = 0; x < _source.Width; x++)
            {
                if (_coverage[(y * _source.Width) + x] > 0)
                {
                    session.SetPixel(_source.Left + x, _source.Top + y, PixelColor.Transparent);
                }
            }
        }
    }

    /// <summary>Writes the lifted artwork wherever the drag has put it.</summary>
    private void Stamp(RasterEditSession session)
    {
        for (int y = 0; y < _source.Height; y++)
        {
            for (int x = 0; x < _source.Width; x++)
            {
                int index = (y * _source.Width) + x;

                if (_coverage[index] > 0)
                {
                    session.SetPixel(
                        _source.Left + x + Offset.X,
                        _source.Top + y + Offset.Y,
                        _lifted[index]);
                }
            }
        }
    }
}
