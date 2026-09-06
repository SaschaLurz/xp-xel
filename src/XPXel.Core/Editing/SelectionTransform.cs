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
/// </remarks>
public sealed class SelectionTransform
{
    /// <summary>What the Edit menu calls this, and what the history entry is named.</summary>
    private const string ActionName = "Move Selection";

    private readonly DocumentWorkspace _workspace;
    private readonly RasterEditSession _session;
    private readonly SelectionMask _selection;
    private readonly ImageRect _source;

    /// <summary>The lifted artwork, row by row across <see cref="_source"/>.</summary>
    private readonly PixelColor[] _lifted;

    /// <summary>Which of those pixels were actually selected, and so actually move.</summary>
    private readonly bool[] _covered;

    private SelectionTransform(
        DocumentWorkspace workspace,
        RasterEditSession session,
        SelectionMask selection,
        ImageRect source,
        PixelColor[] lifted,
        bool[] covered)
    {
        _workspace = workspace;
        _session = session;
        _selection = selection;
        _source = source;
        _lifted = lifted;
        _covered = covered;
    }

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
        bool[] covered = new bool[count];
        bool any = false;

        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                int index = (y * source.Width) + x;
                int sx = source.Left + x;
                int sy = source.Top + y;

                if (!selection.IsSelected(sx, sy))
                {
                    continue;
                }

                covered[index] = true;
                lifted[index] = cel.Pixels.GetPixelOrTransparent(sx, sy);
                any = true;
            }
        }

        if (!any)
        {
            return null;
        }

        // No selection is passed to the session on purpose; see the type remarks.
        RasterEditSession session = RasterEditSession.Begin(workspace.Document, cel, ActionName);

        return new SelectionTransform(workspace, session, selection.Clone(), source, lifted, covered);
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

        SetSelectionCommand marquee = new(_workspace, _selection, TranslateSelection(), ActionName);
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

        // Dragged back to where it started, the artwork is home and the reset has already put
        // it there. Punching the hole and stamping it back would leave the session holding a
        // dirty region that nothing actually changed, and commit a do-nothing undo entry.
        if (!HasMoved)
        {
            return;
        }

        for (int y = 0; y < _source.Height; y++)
        {
            for (int x = 0; x < _source.Width; x++)
            {
                if (_covered[(y * _source.Width) + x])
                {
                    _session.SetPixel(_source.Left + x, _source.Top + y, PixelColor.Transparent);
                }
            }
        }

        for (int y = 0; y < _source.Height; y++)
        {
            for (int x = 0; x < _source.Width; x++)
            {
                int index = (y * _source.Width) + x;

                if (_covered[index])
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

    /// <summary>Builds the marquee in its new position.</summary>
    private SelectionMask TranslateSelection()
    {
        SelectionMask moved = new(_selection.Size);

        for (int y = _source.Top; y < _source.Bottom; y++)
        {
            for (int x = _source.Left; x < _source.Right; x++)
            {
                byte coverage = _selection.GetCoverage(x, y);

                if (coverage > 0)
                {
                    // Anything pushed off the canvas is simply dropped, which matches what
                    // happened to the pixels: the session could not write them either.
                    moved.SetCoverage(x + Offset.X, y + Offset.Y, coverage);
                }
            }
        }

        return moved;
    }
}
