using XPXel.Core.Documents;
using XPXel.Core.History;
using XPXel.Core.Primitives;
using XPXel.Core.Selection;

namespace XPXel.Core.Editing;

/// <summary>What a <see cref="FreeTransform"/> is acting on.</summary>
public enum FreeTransformScope
{
    /// <summary>The artwork itself: the marked pixels, or every marked layer's contents.</summary>
    Pixels,

    /// <summary>The marching ants alone, leaving the artwork exactly where it is.</summary>
    Marks,

    /// <summary>
    /// Pixels arriving from the clipboard, floating over the document until they are placed.
    /// </summary>
    /// <remarks>
    /// The same box, the same eight handles and the same Enter and Escape as a transform of the
    /// artwork, because a paste wants every one of them: what arrives is almost never the right
    /// size or in the right place, and asking the artist to place it first and resize it as a
    /// second, separately undoable act is asking them to do the job twice. The only difference
    /// is that nothing is lifted, so no hole is left behind.
    /// </remarks>
    Clip,
}

/// <summary>
/// Which part of a transform box a point is over.
/// </summary>
/// <remarks>
/// The same shape as <see cref="Tools.CropGrip"/> and for the same reason: the canvas asks on
/// every pointer move so the pointer can promise what a press will do. The edges combine,
/// <see cref="Inside"/> is the body of the box, and <see cref="Center"/> is the handle in the
/// middle of it — which both move the whole thing.
/// </remarks>
[Flags]
public enum TransformGrip
{
    /// <summary>Nothing grabbable here.</summary>
    None = 0,

    /// <summary>The left edge.</summary>
    Left = 1,

    /// <summary>The right edge.</summary>
    Right = 2,

    /// <summary>The top edge.</summary>
    Top = 4,

    /// <summary>The bottom edge.</summary>
    Bottom = 8,

    /// <summary>The body of the box, which moves it.</summary>
    Inside = 16,

    /// <summary>
    /// The handle in the middle of the box, which moves it and outranks every edge.
    /// </summary>
    /// <remarks>
    /// It exists because of small artwork. The eight resize handles need a few pixels of slack
    /// around them to be aimed at with a mouse, and on a sprite twenty pixels across that slack
    /// covers the whole box — so every press grabbed an edge and there was no way left to
    /// simply move the thing. A dedicated handle in the middle, tested before anything else,
    /// means there is always somewhere to take hold of the artwork itself.
    /// </remarks>
    Center = 32,
}

/// <summary>
/// A free transform: a box around whatever is being changed, eight handles to scale it with,
/// a handle in the middle to move it by, flips, quarter turns, and one undo entry for the lot.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it acts on, in order.</b> Marked pixels if anything is marked; otherwise everything
/// painted on the marked layers. That is the same "what do you mean by <em>this</em>" rule the
/// move tool follows, and it is what makes Ctrl+T do the obvious thing without a dialog asking
/// which of the two was meant. A paste (<see cref="FreeTransformScope.Clip"/>) skips the
/// question entirely: what it acts on came from the clipboard.
/// </para>
/// <para>
/// <b>Nearest neighbour, and no arbitrary angles.</b> Scaling picks the nearest source pixel and
/// copies it, so a sprite doubled in size is made of four-pixel blocks in the palette it started
/// with, which is what scaling means in pixel art and what any interpolation would destroy.
/// Rotation is offered in quarter turns only, for the same reason from the other direction: a
/// sprite turned by 37 degrees cannot be resampled onto a pixel grid without ragged edges and
/// invented colours, so the honest answer is not to offer it.
/// </para>
/// <para>
/// <b>Flipping is a drag, not a separate command.</b> Pulling a handle past the opposite edge
/// mirrors the artwork, which is what the gesture already looks like it is doing and what every
/// other transform box on the platform does with it.
/// </para>
/// <para>
/// <b>Live, and reversible until it is placed.</b> Every adjustment resets each layer's edit
/// session and redraws from the pixels lifted at the start, exactly as
/// <see cref="SelectionTransform"/> does, so the artist can scale, flip, rotate and scale again
/// and the history still gets one entry for the whole episode however many times they changed
/// their mind.
/// </para>
/// <para>
/// <b>Placing it is a separate pass, and that is deliberate.</b> <see cref="Commit"/> throws the
/// live preview away, putting every layer back exactly as the box found it, and only then works
/// out where the artwork is going. Two things depend on that. It can look at what is underneath
/// the destination and decide the result deserves a layer of its own; and it can grow a cel
/// before writing to it, so artwork dragged over the canvas edge is kept rather than clipped
/// away — the same promise moving a layer has always made, which a transform used to break.
/// </para>
/// <para>
/// <b><see cref="FreeTransformScope.Marks"/> touches no pixels at all.</b> It resamples the
/// selection mask and nothing else, which is how an artist reshapes a marquee they have already
/// drawn: mark a shape, transform the marks into a bigger one, then fill it. The artwork
/// underneath is untouched throughout, and it is the one scope that ends with something marked
/// — for every other, placing the artwork clears the marks, because the marks were the question
/// and the placed pixels are the answer.
/// </para>
/// </remarks>
public sealed class FreeTransform
{
    /// <summary>
    /// How near a handle a press has to land to take hold of it, in screen pixels.
    /// </summary>
    /// <remarks>
    /// Deliberately close to half the drawn handle, so that what looks grabbable is what is
    /// grabbable. The box used to be resizable anywhere along an edge with seven pixels of
    /// slack in both directions, which on anything small meant the artist could not reach the
    /// artwork at all without catching an edge on the way.
    /// </remarks>
    public const double GripScreenDistance = 5.0;

    /// <summary>What the Edit menu calls a transform of the artwork.</summary>
    private const string PixelsActionName = "Transform";

    /// <summary>What it calls a transform of the marching ants.</summary>
    private const string MarksActionName = "Transform Selection";

    /// <summary>What it calls placing a paste.</summary>
    private const string PasteActionName = "Paste";

    private readonly DocumentWorkspace _workspace;

    /// <summary>One entry per layer being transformed; empty for a marks-only transform.</summary>
    private readonly Subject[] _subjects;

    /// <summary>Where the artwork was lifted from, in document coordinates.</summary>
    private readonly ImageRect _source;

    /// <summary>How much of each source pixel takes part, row by row across <see cref="_source"/>.</summary>
    private readonly byte[] _coverage;

    /// <summary>The marquee to put back on undo, or on Escape.</summary>
    private readonly SelectionMask? _selectionBefore;

    /// <summary>
    /// Whether the artwork was taken off a layer, and so leaves a hole where it came from.
    /// </summary>
    /// <remarks>
    /// False for a paste: the pixels came from the clipboard, and clearing the region under
    /// them would punch a hole in artwork the artist never asked to change.
    /// </remarks>
    private readonly bool _leavesAHole;

    /// <summary>Whether the result always lands on a layer of its own.</summary>
    private readonly bool _alwaysOwnLayer;

    /// <summary>
    /// Whether the result lands on a layer of its own when it would otherwise cover artwork
    /// that is already there.
    /// </summary>
    private readonly bool _ownLayerWhenCovering;

    private ImageRect _bounds;
    private bool _flipX;
    private bool _flipY;
    private int _quarterTurns;

    /// <summary>The box and mirroring the current drag started from; see <see cref="BeginDrag"/>.</summary>
    private ImageRect _dragBounds;
    private bool _dragFlipX;
    private bool _dragFlipY;

    private FreeTransform(
        DocumentWorkspace workspace,
        Subject[] subjects,
        ImageRect source,
        byte[] coverage,
        SelectionMask? selectionBefore,
        FreeTransformScope scope,
        bool leavesAHole,
        bool alwaysOwnLayer,
        bool ownLayerWhenCovering,
        string actionName)
    {
        _workspace = workspace;
        _subjects = subjects;
        _source = source;
        _coverage = coverage;
        _selectionBefore = selectionBefore;
        _leavesAHole = leavesAHole;
        _alwaysOwnLayer = alwaysOwnLayer;
        _ownLayerWhenCovering = ownLayerWhenCovering;
        _bounds = source;
        _dragBounds = source;

        Scope = scope;
        ActionName = actionName;
    }

    /// <summary>What this transform is acting on.</summary>
    public FreeTransformScope Scope { get; }

    /// <summary>What the Edit menu calls this transform.</summary>
    public string ActionName { get; }

    /// <summary>Where the artwork sat when the transform began.</summary>
    public ImageRect SourceBounds => _source;

    /// <summary>Where the box is now: what the artist is dragging, and what the canvas draws.</summary>
    public ImageRect Bounds => _bounds;

    /// <summary>The box the current drag started from.</summary>
    public ImageRect DragBounds => _dragBounds;

    /// <summary>Gets a value indicating whether the artwork is mirrored left to right.</summary>
    public bool IsFlippedHorizontally => _flipX;

    /// <summary>Gets a value indicating whether the artwork is mirrored top to bottom.</summary>
    public bool IsFlippedVertically => _flipY;

    /// <summary>How many quarter turns clockwise have been applied, 0 to 3.</summary>
    public int QuarterTurns => _quarterTurns;

    /// <summary>Gets a value indicating whether anything has actually been changed yet.</summary>
    public bool HasChanges => _bounds != _source || _flipX || _flipY || _quarterTurns != 0;

    /// <summary>Starts a transform, lifting whatever it is going to act on.</summary>
    /// <param name="workspace">The document to transform in.</param>
    /// <param name="scope">Whether to transform the artwork or only the marching ants.</param>
    /// <returns>
    /// The transform, or <see langword="null"/> when there is nothing to transform: no marked
    /// pixels and no painted content on any layer that can be written to, or, for
    /// <see cref="FreeTransformScope.Marks"/>, nothing marked at all.
    /// </returns>
    public static FreeTransform? Begin(DocumentWorkspace workspace, FreeTransformScope scope)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        SelectionMask? selection = workspace.Selection is { IsEmpty: false } marked ? marked : null;

        if (scope == FreeTransformScope.Marks)
        {
            // There is nothing to reshape without a marquee, and inventing one around the whole
            // canvas would be a different command wearing this one's name.
            return selection is null ? null : BeginMarks(workspace, selection);
        }

        List<(RasterLayer Layer, Cel Cel)> targets = [];

        foreach (LayerNode leaf in workspace.GetSelectedLeaves())
        {
            // Text layers are excluded on purpose. Their pixels are a cache regenerated from
            // the words, so anything done to them here would be quietly undone the next time
            // the layer re-rendered. Rasterising the text first is the honest route, and the
            // shell says so rather than pretending the transform worked.
            if (leaf is not RasterLayer { IsLocked: false } layer || !layer.IsEffectivelyVisible)
            {
                continue;
            }

            if (workspace.Document.Cels.Find(layer.Id, workspace.ActiveFrame.Id) is { } cel)
            {
                targets.Add((layer, cel));
            }
        }

        if (targets.Count == 0)
        {
            return null;
        }

        ImageRect source = selection is not null
            ? selection.Bounds.Intersect(workspace.Document.Bounds)
            : MeasurePaintedBounds(targets, workspace.Document.Bounds);

        if (source.Width <= 0 || source.Height <= 0)
        {
            return null;
        }

        byte[] coverage = BuildCoverage(selection, source);
        List<Subject> subjects = [];

        foreach ((RasterLayer layer, Cel cel) in targets)
        {
            PixelColor[] lifted = new PixelColor[source.Width * source.Height];

            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    lifted[(y * source.Width) + x] = cel.Pixels.GetPixelOrTransparent(
                        source.Left + x - cel.Offset.X,
                        source.Top + y - cel.Offset.Y);
                }
            }

            // No selection is passed to the session on purpose, for the same reason a move does
            // not pass one: a transform's whole business is writing where the artwork is going,
            // which is somewhere it is not yet marked.
            subjects.Add(new Subject(
                layer,
                cel,
                RasterEditSession.Begin(workspace.Document, cel, PixelsActionName),
                lifted));
        }

        return new FreeTransform(
            workspace,
            [.. subjects],
            source,
            coverage,
            selection?.Clone(),
            FreeTransformScope.Pixels,
            leavesAHole: true,
            alwaysOwnLayer: false,
            ownLayerWhenCovering: workspace.LayerSettings.TransformToNewLayer,
            PixelsActionName);
    }

    /// <summary>Floats a clip over the active layer inside a transform box, ready to be placed.</summary>
    /// <param name="workspace">The document to paste into.</param>
    /// <param name="clip">The pixels to paste.</param>
    /// <param name="origin">Where the clip's top-left corner should land.</param>
    /// <returns>
    /// The transform, or <see langword="null"/> when there is nothing to paste into — a locked
    /// or hidden layer, or one that is not a drawing layer — or nothing worth pasting.
    /// </returns>
    /// <remarks>
    /// The pixels are stamped down straight away rather than on the first drag, so the artist
    /// sees what arrived and where. It is still floating at that point: dragging it, resizing
    /// it, nudging it or pressing Escape all work, and nothing reaches the history until it is
    /// placed.
    /// </remarks>
    /// <param name="fromOutside">
    /// Whether the pixels came from another program rather than from XP-xel itself, which is
    /// what decides whether they are given a layer of their own.
    /// </param>
    public static FreeTransform? BeginPaste(
        DocumentWorkspace workspace,
        PixelClip clip,
        ImagePoint origin,
        bool fromOutside = false)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(clip);

        if (clip.IsEmpty
            || workspace.ActiveRasterLayer is not { } layer
            || workspace.GetOrCreateActiveCel() is not { } cel)
        {
            return null;
        }

        ImageRect source = new(origin.X, origin.Y, clip.Size.Width, clip.Size.Height);

        FreeTransform transform = new(
            workspace,
            [new Subject(
                layer,
                cel,
                RasterEditSession.Begin(workspace.Document, cel, PasteActionName),
                clip.Pixels.ToArray())],
            source,
            clip.Coverage.ToArray(),
            workspace.Selection?.Clone(),
            FreeTransformScope.Clip,
            leavesAHole: false,

            // One option governs a paste, not two. Someone who has turned "paste to a new
            // layer" off has said where they want pasted pixels to go, and second-guessing
            // that whenever the destination happens to be occupied would make the setting
            // mean "sometimes".
            //
            // And only artwork from another program: pixels copied inside XP-xel go where the
            // artist put them. A new layer per paste is what turned "put this background on
            // each frame" into a stack of layers nobody asked for.
            alwaysOwnLayer: fromOutside && workspace.LayerSettings.PasteToNewLayer,
            ownLayerWhenCovering: false,
            PasteActionName);

        transform.Render();
        return transform;
    }

    /// <summary>Starts a transform of the marching ants alone.</summary>
    private static FreeTransform BeginMarks(DocumentWorkspace workspace, SelectionMask selection)
    {
        ImageRect source = selection.Bounds.Intersect(workspace.Document.Bounds);

        return new FreeTransform(
            workspace,
            [],
            source,
            BuildCoverage(selection, source),
            selection.Clone(),
            FreeTransformScope.Marks,
            leavesAHole: false,
            alwaysOwnLayer: false,
            ownLayerWhenCovering: false,
            MarksActionName);
    }

    /// <summary>A short description of the current state, for the status bar.</summary>
    /// <returns>The size, and the turns and mirroring if there are any.</returns>
    public string Describe()
    {
        string turn = _quarterTurns == 0 ? string.Empty : $", turned {_quarterTurns * 90}°";
        string mirror = (_flipX, _flipY) switch
        {
            (true, true) => ", flipped both ways",
            (true, false) => ", flipped across",
            (false, true) => ", flipped over",
            _ => string.Empty,
        };

        return $"{_bounds.Width} × {_bounds.Height}{turn}{mirror}";
    }

    /// <summary>
    /// Reports what a point is over, so the pointer can say what a press would take hold of.
    /// </summary>
    /// <param name="point">The point, in image coordinates.</param>
    /// <param name="tolerance">How near a handle still counts as being on it, in image pixels.</param>
    /// <returns>The grip a press there would take.</returns>
    /// <remarks>
    /// <para>
    /// <b>The handles are points, not whole edges.</b> The eight squares the canvas draws are
    /// the eight places the box resizes from, and nowhere else — which is what makes the
    /// promise the pointer shape is already making a true one. Anywhere else inside the box
    /// moves it.
    /// </para>
    /// <para>
    /// <b>The middle wins.</b> The centre handle is tested first and its reach is held to a
    /// quarter of the box, so it can never be the only thing a small box offers, and the eight
    /// resize handles can never crowd out the one gesture that moves the artwork.
    /// </para>
    /// </remarks>
    public TransformGrip HitTest(ImagePoint point, int tolerance)
    {
        tolerance = Math.Max(1, tolerance);

        int centreX = _bounds.Left + (_bounds.Width / 2);
        int centreY = _bounds.Top + (_bounds.Height / 2);
        int reach = Math.Clamp(tolerance, 1, Math.Max(1, Math.Min(_bounds.Width, _bounds.Height) / 4));

        if (Math.Abs(point.X - centreX) <= reach && Math.Abs(point.Y - centreY) <= reach)
        {
            return TransformGrip.Center;
        }

        bool onLeft = Math.Abs(point.X - _bounds.Left) <= tolerance;
        bool onRight = Math.Abs(point.X - _bounds.Right) <= tolerance;
        bool onMiddleColumn = Math.Abs(point.X - centreX) <= tolerance;

        bool onTop = Math.Abs(point.Y - _bounds.Top) <= tolerance;
        bool onBottom = Math.Abs(point.Y - _bounds.Bottom) <= tolerance;
        bool onMiddleRow = Math.Abs(point.Y - centreY) <= tolerance;

        TransformGrip horizontal = onLeft ? TransformGrip.Left : onRight ? TransformGrip.Right : TransformGrip.None;
        TransformGrip vertical = onTop ? TransformGrip.Top : onBottom ? TransformGrip.Bottom : TransformGrip.None;

        // A corner handle is both edges at once; an edge handle is one edge and the middle of
        // the other axis, which is exactly where the four edge squares are drawn.
        if (horizontal != TransformGrip.None && vertical != TransformGrip.None)
        {
            return horizontal | vertical;
        }

        if (horizontal != TransformGrip.None && onMiddleRow)
        {
            return horizontal;
        }

        if (vertical != TransformGrip.None && onMiddleColumn)
        {
            return vertical;
        }

        return _bounds.Contains(point) ? TransformGrip.Inside : TransformGrip.None;
    }

    /// <summary>
    /// Remembers the current box, so that a drag is measured from where it started rather than
    /// from where the last pointer sample left it.
    /// </summary>
    /// <remarks>
    /// Mirroring is the reason this is needed. A handle dragged past the opposite edge flips the
    /// artwork and dragged back again flips it home; accumulating that per pointer sample would
    /// make the sprite strobe as the pointer crossed the edge.
    /// </remarks>
    public void BeginDrag()
    {
        _dragBounds = _bounds;
        _dragFlipX = _flipX;
        _dragFlipY = _flipY;
    }

    /// <summary>
    /// Puts the box's edges where a drag has taken them, mirroring the artwork if an edge has
    /// crossed its opposite.
    /// </summary>
    /// <param name="left">The left edge; may be past <paramref name="right"/>.</param>
    /// <param name="top">The top edge; may be past <paramref name="bottom"/>.</param>
    /// <param name="right">The right edge.</param>
    /// <param name="bottom">The bottom edge.</param>
    public void DragEdges(int left, int top, int right, int bottom)
    {
        bool mirroredX = right < left;
        bool mirroredY = bottom < top;

        ImageRect box = ImageRect.FromEdges(
            Math.Min(left, right),
            Math.Min(top, bottom),
            Math.Max(left, right),
            Math.Max(top, bottom));

        // A box with no width holds nothing and offers no way back out, because every further
        // drag would be scaling zero pixels. One pixel is the smallest thing still made of art.
        box = new ImageRect(box.X, box.Y, Math.Max(1, box.Width), Math.Max(1, box.Height));

        bool flipX = _dragFlipX ^ mirroredX;
        bool flipY = _dragFlipY ^ mirroredY;

        if (box == _bounds && flipX == _flipX && flipY == _flipY)
        {
            return;
        }

        _bounds = box;
        _flipX = flipX;
        _flipY = flipY;
        Render();
    }

    /// <summary>Moves the box without resizing it, which is what the arrow keys do.</summary>
    /// <param name="deltaX">Horizontal movement in pixels.</param>
    /// <param name="deltaY">Vertical movement in pixels.</param>
    public void MoveBy(int deltaX, int deltaY)
    {
        if (deltaX == 0 && deltaY == 0)
        {
            return;
        }

        _bounds = _bounds.Translate(deltaX, deltaY);
        _dragBounds = _dragBounds.Translate(deltaX, deltaY);
        Render();
    }

    /// <summary>Turns the artwork a quarter turn.</summary>
    /// <param name="clockwise">Which way to turn it.</param>
    /// <remarks>
    /// The box turns with the artwork, about its own centre, so a tall sprite becomes a wide one
    /// in the place it already occupied rather than jumping off to a corner.
    /// </remarks>
    public void Rotate(bool clockwise)
    {
        _quarterTurns = (_quarterTurns + (clockwise ? 1 : 3)) % 4;

        int centreX = _bounds.Left + (_bounds.Width / 2);
        int centreY = _bounds.Top + (_bounds.Height / 2);

        _bounds = new ImageRect(
            centreX - (_bounds.Height / 2),
            centreY - (_bounds.Width / 2),
            _bounds.Height,
            _bounds.Width);

        _dragBounds = _bounds;
        Render();
    }

    /// <summary>Mirrors the artwork.</summary>
    /// <param name="horizontally">
    /// <see langword="true"/> to mirror left to right, <see langword="false"/> top to bottom.
    /// </param>
    public void Flip(bool horizontally)
    {
        if (horizontally)
        {
            _flipX = !_flipX;
            _dragFlipX = _flipX;
        }
        else
        {
            _flipY = !_flipY;
            _dragFlipY = _flipY;
        }

        Render();
    }

    /// <summary>
    /// Puts the artwork down and produces the history entry for the whole transform.
    /// </summary>
    /// <returns>The command to record, or <see langword="null"/> if nothing actually changed.</returns>
    /// <remarks>
    /// The change is already applied to the document by the time this returns, so the caller
    /// records the command with <see cref="DocumentHistory.Push"/> rather than
    /// <see cref="DocumentHistory.Execute"/>.
    /// </remarks>
    public IUndoableCommand? Commit()
    {
        if (Scope == FreeTransformScope.Marks)
        {
            if (!HasChanges)
            {
                return null;
            }

            SetSelectionCommand marks = new(_workspace, _selectionBefore, BuildMask(), ActionName);
            marks.Redo();
            return marks;
        }

        // The live preview is thrown away first, putting every layer back exactly as the box
        // found it. Everything below then works from the artwork as it really is: what the
        // destination would cover, and how much bigger a cel has to be to hold what is going
        // over the canvas edge. See the type remarks.
        foreach (Subject subject in _subjects)
        {
            subject.Session.Rollback();
        }

        List<IUndoableCommand> parts = [];

        // A transform of the artwork that was never adjusted has nothing to place. A paste
        // always has: the pixels arrived from outside the document and have to be written down
        // even if the artist accepted where they landed.
        if (Scope == FreeTransformScope.Clip || HasChanges)
        {
            foreach (Subject subject in _subjects)
            {
                Place(subject, parts);
            }
        }

        // Placing the artwork answers whatever the marks were asking, so they go. This is what
        // makes Enter mean "done" rather than leaving the artist to press Escape or Ctrl+D
        // afterwards to get a clean canvas back.
        //
        // Only once something has actually been placed, though. A box put up and taken down
        // again without a drag has answered nothing, and this is reached defensively from
        // several places — switching tool, switching tab — where silently throwing the marks
        // away would be a change nobody asked for.
        if (parts.Count > 0 && (_selectionBefore is not null || _workspace.Selection is not null))
        {
            SetSelectionCommand deselect = new(_workspace, _selectionBefore, null, ActionName);
            deselect.Redo();
            parts.Add(deselect);
        }

        return parts.Count switch
        {
            0 => null,
            1 => parts[0],
            _ => new CompositeCommand(ActionName, parts),
        };
    }

    /// <summary>Abandons the transform, putting everything back as it was found.</summary>
    public void Cancel()
    {
        foreach (Subject subject in _subjects)
        {
            subject.Session.Rollback();
        }

        if (Scope == FreeTransformScope.Marks)
        {
            _workspace.Selection = _selectionBefore;
        }
    }

    /// <summary>
    /// Writes one subject's artwork where the box has put it, growing or adding whatever has to
    /// exist first, and records every step in <paramref name="parts"/>.
    /// </summary>
    /// <remarks>
    /// The order the parts go in is the order redo replays them and the reverse of the order
    /// undo unwinds them, so a cel has to be swapped in before the edits recorded against it
    /// and a layer added before its cel is touched.
    /// </remarks>
    private void Place(Subject subject, List<IUndoableCommand> parts)
    {
        PixelDocument document = _workspace.Document;

        if (!_alwaysOwnLayer && !(_ownLayerWhenCovering && WouldCoverArtwork(subject)))
        {
            // Straight back onto the layer it came from: clear the space it is leaving and
            // stamp it where it has been put, in one session so it is one history entry.
            Cel cel = Expand(subject.Cel, parts);
            RasterEditSession session = RasterEditSession.Begin(document, cel, ActionName);

            ClearSource(session);
            Stamp(session, subject);

            if (session.Commit() is { } edit)
            {
                parts.Add(edit);
            }

            return;
        }

        // A layer of its own. The hole still belongs to the layer the artwork was lifted off,
        // because the artwork has moved rather than been copied.
        if (_leavesAHole)
        {
            RasterEditSession vacate = RasterEditSession.Begin(document, subject.Cel, ActionName);
            ClearSource(vacate);

            if (vacate.Commit() is { } cleared)
            {
                parts.Add(cleared);
            }
        }

        GroupLayer parent = subject.Layer.Parent ?? document.Root;
        int index = parent.IndexOf(subject.Layer) + 1;

        // The new layer wears the old one's opacity and blend mode, so the artwork looks the
        // same the instant it is placed as it did a moment earlier under the transform box.
        // Anything else would make Enter appear to change the colours.
        RasterLayer layer = new(Guid.NewGuid(), NextLayerName(document))
        {
            Opacity = subject.Layer.Opacity,
            BlendMode = subject.Layer.BlendMode,
        };

        document.InsertLayer(layer, parent, index);
        parts.Add(new AddLayerCommand(ActionName, document, layer, parent, index));

        Cel fresh = Expand(document.GetOrCreateCel(layer, _workspace.ActiveFrame), parts);
        RasterEditSession paint = RasterEditSession.Begin(document, fresh, ActionName);

        Stamp(paint, subject);

        if (paint.Commit() is { } painted)
        {
            parts.Add(painted);
        }

        // The new layer is where the artist's next stroke should land: it is the one holding
        // what they have just been looking at for the length of the drag.
        _workspace.ActiveLayer = layer;
        _workspace.SetSelectedLayers([layer]);
    }

    /// <summary>
    /// Makes a cel big enough for wherever the box has ended up, recording the swap if one was
    /// needed.
    /// </summary>
    /// <returns>The cel to write to, which is the original one when it was already big enough.</returns>
    private Cel Expand(Cel cel, List<IUndoableCommand> parts)
    {
        Cel grown = cel.CreateExpanded(_bounds, _workspace.Document.Bounds);

        if (ReferenceEquals(grown, cel))
        {
            return cel;
        }

        _workspace.Document.Cels.Set(grown);
        parts.Add(new ReplaceCelCommand(ActionName, _workspace.Document, cel, grown));

        return grown;
    }

    /// <summary>
    /// Gets a value indicating whether placing this subject where the box now sits would write
    /// over artwork that is already on the layer.
    /// </summary>
    /// <remarks>
    /// The space the artwork is vacating does not count — that is the artwork's own, and it is
    /// about to be cleared — and neither does a transparent pixel in either image, since
    /// nothing is destroyed by laying nothing on top of nothing. What is left is exactly the
    /// case the artist would lose work to: a solid pixel landing on a solid pixel that has
    /// nothing to do with it.
    /// </remarks>
    private bool WouldCoverArtwork(Subject subject)
    {
        for (int ty = _bounds.Top; ty < _bounds.Bottom; ty++)
        {
            for (int tx = _bounds.Left; tx < _bounds.Right; tx++)
            {
                if (MapToSource(tx, ty) is not { } index
                    || _coverage[index] == 0
                    || subject.Pixels[index].A == 0
                    || IsWithinSource(tx, ty))
                {
                    continue;
                }

                if (subject.Cel.Pixels.GetPixelOrTransparent(
                    tx - subject.Cel.Offset.X,
                    ty - subject.Cel.Offset.Y).A > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether a document point is one of the pixels this transform took part of.</summary>
    private bool IsWithinSource(int x, int y) =>
        _leavesAHole
        && _source.Contains(x, y)
        && _coverage[((y - _source.Top) * _source.Width) + (x - _source.Left)] > 0;

    /// <summary>Clears the space the artwork was lifted from, if it was lifted from anywhere.</summary>
    private void ClearSource(RasterEditSession session)
    {
        if (!_leavesAHole)
        {
            return;
        }

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

    /// <summary>Writes the artwork wherever the box now sits.</summary>
    private void Stamp(RasterEditSession session, Subject subject)
    {
        for (int ty = _bounds.Top; ty < _bounds.Bottom; ty++)
        {
            for (int tx = _bounds.Left; tx < _bounds.Right; tx++)
            {
                if (MapToSource(tx, ty) is { } index && _coverage[index] > 0)
                {
                    session.SetPixel(tx, ty, subject.Pixels[index]);
                }
            }
        }
    }

    /// <summary>Produces "Layer 3" style names, the same way the layers panel does.</summary>
    private static string NextLayerName(PixelDocument document) =>
        $"Layer {document.EnumerateLayers().Count() + 1}";

    /// <summary>Redraws the artwork, or the marquee, at the current box.</summary>
    /// <remarks>
    /// This is the preview and only the preview. It writes into the cels as they already are,
    /// so anything past a cel's edge simply does not appear — which is correct, because nothing
    /// past the canvas edge is drawn either. <see cref="Commit"/> is where the artwork is
    /// actually placed, and where the room for it is made.
    /// </remarks>
    private void Render()
    {
        if (Scope == FreeTransformScope.Marks)
        {
            // The marquee is changed live rather than previewed, because it is the preview:
            // there is nothing else on screen for the artist to judge the drag by.
            _workspace.Selection = BuildMask();
            return;
        }

        foreach (Subject subject in _subjects)
        {
            // Back to the layer as it was before the transform started, so every box is drawn
            // from the original artwork rather than from the last box's leavings.
            subject.Session.ResetToOriginal();
        }

        // Lifted artwork dragged back where it started is home, and the reset has already put
        // it there. A paste has no such home: at its starting box it is still the thing that
        // has to be stamped.
        if (_leavesAHole && !HasChanges)
        {
            foreach (Subject subject in _subjects)
            {
                subject.Session.PublishInterimChanges();
            }

            return;
        }

        foreach (Subject subject in _subjects)
        {
            ClearSource(subject.Session);
            Stamp(subject.Session, subject);
            subject.Session.PublishInterimChanges();
        }
    }

    /// <summary>
    /// Finds the source pixel a point in the box comes from, as an index into the lifted arrays,
    /// or <see langword="null"/> if it falls outside them.
    /// </summary>
    /// <remarks>
    /// Nearest neighbour: the box is walked in whole target pixels and each one is given the
    /// single source pixel it lands on. Scaling up therefore repeats pixels into square blocks
    /// and scaling down drops them, which is exactly what pixel art wants and what any weighted
    /// average would ruin.
    /// </remarks>
    private int? MapToSource(int x, int y)
    {
        (int orientedWidth, int orientedHeight) = OrientedSize;

        // Where the point falls as a fraction of the box, mapped onto the oriented source grid.
        int u = (int)((long)(x - _bounds.Left) * orientedWidth / Math.Max(1, _bounds.Width));
        int v = (int)((long)(y - _bounds.Top) * orientedHeight / Math.Max(1, _bounds.Height));

        u = Math.Clamp(u, 0, orientedWidth - 1);
        v = Math.Clamp(v, 0, orientedHeight - 1);

        int width = _source.Width;
        int height = _source.Height;

        // Undo the rotation, then the mirroring, to land back on the pixel that was lifted.
        (int fx, int fy) = _quarterTurns switch
        {
            1 => (v, height - 1 - u),
            2 => (width - 1 - u, height - 1 - v),
            3 => (width - 1 - v, u),
            _ => (u, v),
        };

        int sx = _flipX ? width - 1 - fx : fx;
        int sy = _flipY ? height - 1 - fy : fy;

        return sx >= 0 && sy >= 0 && sx < width && sy < height ? (sy * width) + sx : null;
    }

    /// <summary>The source's dimensions after the quarter turns, which swap them when odd.</summary>
    private (int Width, int Height) OrientedSize =>
        _quarterTurns % 2 == 0 ? (_source.Width, _source.Height) : (_source.Height, _source.Width);

    /// <summary>Traces the marquee around wherever the transform has put things.</summary>
    /// <returns>The mask, or <see langword="null"/> if it would mark nothing.</returns>
    private SelectionMask? BuildMask()
    {
        SelectionMask mask = new(_workspace.Document.Size);

        for (int y = _bounds.Top; y < _bounds.Bottom; y++)
        {
            for (int x = _bounds.Left; x < _bounds.Right; x++)
            {
                if (MapToSource(x, y) is { } index && _coverage[index] > 0)
                {
                    // Anything pushed off the canvas is simply dropped: a mask has no room for
                    // pixels the canvas does not have.
                    mask.SetCoverage(x, y, _coverage[index]);
                }
            }
        }

        mask.RecalculateBounds();
        return mask.IsEmpty ? null : mask;
    }

    /// <summary>
    /// Builds the per-pixel coverage a transform acts through.
    /// </summary>
    /// <remarks>
    /// A selection contributes its own shape, so transforming a wand selection scales the shape
    /// the artist marked rather than the box around it. With nothing marked the whole box takes
    /// part, including its transparent gaps: the holes in a sprite belong to the sprite and have
    /// to scale along with it.
    /// </remarks>
    private static byte[] BuildCoverage(SelectionMask? selection, ImageRect source)
    {
        byte[] coverage = new byte[source.Width * source.Height];

        if (selection is null)
        {
            Array.Fill(coverage, byte.MaxValue);
            return coverage;
        }

        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                coverage[(y * source.Width) + x] = selection.GetCoverage(source.Left + x, source.Top + y);
            }
        }

        return coverage;
    }

    /// <summary>
    /// The smallest rectangle holding every painted pixel of the given layers.
    /// </summary>
    /// <remarks>
    /// The painted pixels rather than the cel rectangles, so that the handles hug the sprite. A
    /// cel is usually the size of the whole canvas, and a transform box around the whole canvas
    /// would be a box with nothing to say about where the artwork actually is.
    /// </remarks>
    private static ImageRect MeasurePaintedBounds(List<(RasterLayer Layer, Cel Cel)> targets, ImageRect canvas)
    {
        ImageRect painted = ImageRect.Empty;

        foreach ((_, Cel cel) in targets)
        {
            ImageRect visible = cel.Bounds.Intersect(canvas);

            for (int y = visible.Top; y < visible.Bottom; y++)
            {
                for (int x = visible.Left; x < visible.Right; x++)
                {
                    if (cel.Pixels.GetPixelOrTransparent(x - cel.Offset.X, y - cel.Offset.Y).A > 0)
                    {
                        painted = painted.IsEmpty ? new ImageRect(x, y, 1, 1) : painted.Union(x, y);
                    }
                }
            }
        }

        return painted;
    }

    /// <summary>One layer taking part in a transform.</summary>
    /// <param name="Layer">The layer the artwork came from, and the one a new layer goes above.</param>
    /// <param name="Cel">The cel the artwork was lifted from, and the preview is drawn into.</param>
    /// <param name="Session">Where the preview's changes are recorded, so they can be undone.</param>
    /// <param name="Pixels">The artwork as it was lifted, row by row across the source.</param>
    private sealed record Subject(RasterLayer Layer, Cel Cel, RasterEditSession Session, PixelColor[] Pixels);
}
