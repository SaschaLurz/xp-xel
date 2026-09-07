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
}

/// <summary>
/// Which part of a transform box a point is over.
/// </summary>
/// <remarks>
/// The same shape as <see cref="Tools.CropGrip"/> and for the same reason: the canvas asks on
/// every pointer move so the pointer can promise what a press will do. The edges combine, and
/// <see cref="Inside"/> is the body of the box, which moves the whole thing.
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
}

/// <summary>
/// A free transform: a box around whatever is being changed, eight handles to scale it with,
/// flips, quarter turns, and one undo entry for the lot.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it acts on, in order.</b> Marked pixels if anything is marked; otherwise everything
/// painted on the marked layers. That is the same "what do you mean by <em>this</em>" rule the
/// move tool follows, and it is what makes Ctrl+T do the obvious thing without a dialog asking
/// which of the two was meant.
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
/// <b><see cref="FreeTransformScope.Marks"/> touches no pixels at all.</b> It resamples the
/// selection mask and nothing else, which is how an artist reshapes a marquee they have already
/// drawn: mark a shape, transform the marks into a bigger one, then fill it. The artwork
/// underneath is untouched throughout.
/// </para>
/// </remarks>
public sealed class FreeTransform
{
    /// <summary>What the Edit menu calls a transform of the artwork.</summary>
    private const string PixelsActionName = "Transform";

    /// <summary>What it calls a transform of the marching ants.</summary>
    private const string MarksActionName = "Transform Selection";

    private readonly DocumentWorkspace _workspace;

    /// <summary>One entry per layer being transformed; empty for a marks-only transform.</summary>
    private readonly Subject[] _subjects;

    /// <summary>Where the artwork was lifted from, in document coordinates.</summary>
    private readonly ImageRect _source;

    /// <summary>How much of each source pixel takes part, row by row across <see cref="_source"/>.</summary>
    private readonly byte[] _coverage;

    /// <summary>The marquee to put back on undo, or on Escape.</summary>
    private readonly SelectionMask? _selectionBefore;

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
        FreeTransformScope scope)
    {
        _workspace = workspace;
        _subjects = subjects;
        _source = source;
        _coverage = coverage;
        _selectionBefore = selectionBefore;
        _bounds = source;
        _dragBounds = source;

        Scope = scope;
        ActionName = scope == FreeTransformScope.Marks ? MarksActionName : PixelsActionName;
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

        List<Cel> cels = [];

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
                cels.Add(cel);
            }
        }

        if (cels.Count == 0)
        {
            return null;
        }

        ImageRect source = selection is not null
            ? selection.Bounds.Intersect(workspace.Document.Bounds)
            : MeasurePaintedBounds(cels, workspace.Document.Bounds);

        if (source.Width <= 0 || source.Height <= 0)
        {
            return null;
        }

        byte[] coverage = BuildCoverage(selection, source);
        List<Subject> subjects = [];

        foreach (Cel cel in cels)
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
                RasterEditSession.Begin(workspace.Document, cel, PixelsActionName),
                lifted));
        }

        return new FreeTransform(
            workspace,
            [.. subjects],
            source,
            coverage,
            selection?.Clone(),
            FreeTransformScope.Pixels);
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
            FreeTransformScope.Marks);
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
    /// <param name="tolerance">How near an edge still counts as being on it, in image pixels.</param>
    /// <returns>The grip a press there would take.</returns>
    public TransformGrip HitTest(ImagePoint point, int tolerance)
    {
        tolerance = Math.Max(1, tolerance);

        bool nearLeft = Math.Abs(point.X - _bounds.Left) <= tolerance;
        bool nearRight = Math.Abs(point.X - _bounds.Right) <= tolerance;
        bool nearTop = Math.Abs(point.Y - _bounds.Top) <= tolerance;
        bool nearBottom = Math.Abs(point.Y - _bounds.Bottom) <= tolerance;

        bool withinRows = point.Y >= _bounds.Top - tolerance && point.Y <= _bounds.Bottom + tolerance;
        bool withinColumns = point.X >= _bounds.Left - tolerance && point.X <= _bounds.Right + tolerance;

        TransformGrip grip = TransformGrip.None;

        if (nearLeft && withinRows)
        {
            grip |= TransformGrip.Left;
        }
        else if (nearRight && withinRows)
        {
            grip |= TransformGrip.Right;
        }

        if (nearTop && withinColumns)
        {
            grip |= TransformGrip.Top;
        }
        else if (nearBottom && withinColumns)
        {
            grip |= TransformGrip.Bottom;
        }

        return grip != TransformGrip.None || !_bounds.Contains(point) ? grip : TransformGrip.Inside;
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

        List<IUndoableCommand> parts = [];

        foreach (Subject subject in _subjects)
        {
            if (subject.Session.Commit() is { } command)
            {
                parts.Add(command);
            }
        }

        if (parts.Count == 0)
        {
            return null;
        }

        // The marquee follows the artwork, so a transform can be followed by another one and so
        // an undo does not strand the ants over the space the artwork used to fill.
        SetSelectionCommand marquee = new(_workspace, _selectionBefore, BuildMask(), ActionName);
        marquee.Redo();
        parts.Add(marquee);

        return new CompositeCommand(ActionName, parts);
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

    /// <summary>Redraws the artwork, or the marquee, at the current box.</summary>
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

        if (!HasChanges)
        {
            foreach (Subject subject in _subjects)
            {
                subject.Session.PublishInterimChanges();
            }

            return;
        }

        foreach (Subject subject in _subjects)
        {
            for (int y = 0; y < _source.Height; y++)
            {
                for (int x = 0; x < _source.Width; x++)
                {
                    if (_coverage[(y * _source.Width) + x] > 0)
                    {
                        subject.Session.SetPixel(_source.Left + x, _source.Top + y, PixelColor.Transparent);
                    }
                }
            }

            for (int ty = _bounds.Top; ty < _bounds.Bottom; ty++)
            {
                for (int tx = _bounds.Left; tx < _bounds.Right; tx++)
                {
                    if (MapToSource(tx, ty) is not { } index || _coverage[index] == 0)
                    {
                        continue;
                    }

                    subject.Session.SetPixel(tx, ty, subject.Pixels[index]);
                }
            }

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
                    // Anything pushed off the canvas is simply dropped, which matches what
                    // happened to the pixels: the session could not write them either.
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
    /// The smallest rectangle holding every painted pixel of the given cels.
    /// </summary>
    /// <remarks>
    /// The painted pixels rather than the cel rectangles, so that the handles hug the sprite. A
    /// cel is usually the size of the whole canvas, and a transform box around the whole canvas
    /// would be a box with nothing to say about where the artwork actually is.
    /// </remarks>
    private static ImageRect MeasurePaintedBounds(List<Cel> cels, ImageRect canvas)
    {
        ImageRect painted = ImageRect.Empty;

        foreach (Cel cel in cels)
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

    /// <summary>One layer taking part in a transform: its edit session and its lifted pixels.</summary>
    /// <param name="Session">Where the layer's changes are recorded.</param>
    /// <param name="Pixels">The artwork as it was lifted, row by row across the source.</param>
    private sealed record Subject(RasterEditSession Session, PixelColor[] Pixels);
}
