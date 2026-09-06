using XPXel.Core.Documents;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Selection;

namespace XPXel.Core.History;

/// <summary>
/// Accumulates the pixel changes of one user action — a stroke, a fill, a paste — and turns
/// them into a single <see cref="RasterEditCommand"/> at the end.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem it solves.</b> Drawing happens live: the artist sees each pixel appear as
/// the pointer moves. But the undo entry may only be created once, at the end, and it needs
/// to know what the pixels looked like <em>before</em> the stroke started. A session
/// captures exactly that, and nothing more.
/// </para>
/// <para>
/// <b>Two capture strategies.</b> Which is cheaper depends on the operation:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <see cref="Begin"/> records the original value of each pixel the first time it is
///     touched. Ideal for freehand strokes, which touch a small, unpredictable set of
///     pixels.
///   </description></item>
///   <item><description>
///     <see cref="BeginWithSnapshot"/> copies the whole cel up front. Ideal for operations
///     that may touch most of the canvas — bucket fill above all — where per-pixel
///     bookkeeping would cost more than the copy.
///   </description></item>
/// </list>
/// <para>
/// Either way <see cref="Commit"/> produces a command holding only the rectangle that
/// actually changed, so the choice affects peak working memory during the operation, never
/// the size of the history entry.
/// </para>
/// <para>
/// <b>Selection is enforced here</b>, not in each tool, so that no tool can accidentally
/// forget to respect it.
/// </para>
/// </remarks>
public sealed class RasterEditSession
{
    private readonly PixelDocument _document;
    private readonly Cel _cel;
    private readonly string _name;
    private readonly SelectionMask? _selection;

    /// <summary>Where the cel buffer's top-left corner sits in document coordinates.</summary>
    /// <remarks>
    /// Captured once. A cel's offset changes only when the layer is moved or the canvas is
    /// resized, and both of those replace the cel rather than editing it, so no session can be
    /// open across such a change.
    /// </remarks>
    private readonly ImagePoint _origin;

    /// <summary>Original values keyed by pixel index, used by the per-pixel strategy.</summary>
    private readonly Dictionary<int, PixelColor>? _originals;

    /// <summary>A copy of the cel taken before the operation, used by the snapshot strategy.</summary>
    private readonly PixelBuffer? _snapshot;

    private ImageRect _dirtyRegion = ImageRect.Empty;

    private RasterEditSession(
        PixelDocument document,
        Cel cel,
        string name,
        SelectionMask? selection,
        bool useSnapshot)
    {
        _document = document;
        _cel = cel;
        _name = name;
        _selection = selection;
        _origin = cel.Offset;

        if (useSnapshot)
        {
            _snapshot = cel.Pixels.Clone();
        }
        else
        {
            _originals = [];
        }
    }

    /// <summary>The rectangle touched so far, in document coordinates.</summary>
    public ImageRect DirtyRegion =>
        _dirtyRegion.IsEmpty ? _dirtyRegion : _dirtyRegion.Translate(_origin.X, _origin.Y);

    /// <summary>Gets a value indicating whether any pixel has actually changed value.</summary>
    public bool HasChanges => !_dirtyRegion.IsEmpty;

    /// <summary>
    /// The buffer being edited. Tools read from it — for example to sample the colour under
    /// the cursor — but must write only through this session so changes are recorded.
    /// </summary>
    /// <remarks>
    /// Its coordinates are the <em>buffer's</em>, not the document's, and the two differ once a
    /// layer has been moved. Add <see cref="Origin"/> to go from one to the other. Every other
    /// member of this class speaks document coordinates, because that is what the pointer hands
    /// the tools; this one cannot, because a buffer does not know where it is.
    /// </remarks>
    public PixelBuffer Pixels => _cel.Pixels;

    /// <summary>
    /// Where <see cref="Pixels"/> sits in document coordinates, which is (0, 0) until the layer
    /// it belongs to has been moved.
    /// </summary>
    public ImagePoint Origin => _origin;

    /// <summary>
    /// Starts a session that records each pixel's original value on first touch. Use for
    /// operations touching a small or unpredictable set of pixels.
    /// </summary>
    /// <param name="document">The document being edited.</param>
    /// <param name="cel">The cel being edited.</param>
    /// <param name="name">The action name for the Edit menu.</param>
    /// <param name="selection">The active selection, or <see langword="null"/> for none.</param>
    /// <returns>The new session.</returns>
    public static RasterEditSession Begin(
        PixelDocument document,
        Cel cel,
        string name,
        SelectionMask? selection = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(cel);
        ArgumentException.ThrowIfNullOrEmpty(name);

        return new RasterEditSession(document, cel, name, selection, useSnapshot: false);
    }

    /// <summary>
    /// Starts a session that copies the whole cel up front. Use for operations that may
    /// cover most of the canvas.
    /// </summary>
    /// <param name="document">The document being edited.</param>
    /// <param name="cel">The cel being edited.</param>
    /// <param name="name">The action name for the Edit menu.</param>
    /// <param name="selection">The active selection, or <see langword="null"/> for none.</param>
    /// <returns>The new session.</returns>
    public static RasterEditSession BeginWithSnapshot(
        PixelDocument document,
        Cel cel,
        string name,
        SelectionMask? selection = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(cel);
        ArgumentException.ThrowIfNullOrEmpty(name);

        return new RasterEditSession(document, cel, name, selection, useSnapshot: true);
    }

    /// <summary>
    /// Returns the value a pixel held before this session touched it.
    /// </summary>
    /// <param name="x">The horizontal coordinate, in document space.</param>
    /// <param name="y">The vertical coordinate, in document space.</param>
    /// <returns>The pre-session colour, or the current one for a pixel not yet touched.</returns>
    /// <remarks>
    /// A soft brush needs this. Its stamps overlap heavily along a stroke, and compositing
    /// each one onto the result of the last would darken the middle of the stroke into a
    /// solid line every time the pointer slowed down. Compositing against the pixel's
    /// <em>pre-stroke</em> value instead makes a stamp idempotent, so passing over the same
    /// pixel twice in one stroke looks the same as passing over it once.
    /// </remarks>
    public PixelColor GetOriginalPixel(int x, int y)
    {
        int bufferX = x - _origin.X;
        int bufferY = y - _origin.Y;

        if (_snapshot is not null)
        {
            return _snapshot.GetPixelOrTransparent(bufferX, bufferY);
        }

        if (_originals is not null
            && _cel.Pixels.Size.Contains(bufferX, bufferY)
            && _originals.TryGetValue((bufferY * _cel.Pixels.Width) + bufferX, out PixelColor original))
        {
            return original;
        }

        return _cel.Pixels.GetPixelOrTransparent(bufferX, bufferY);
    }

    /// <summary>
    /// Replaces a pixel, recording its previous value. Writing the value a pixel already
    /// holds is not a change and is ignored, which keeps no-op strokes out of the history.
    /// </summary>
    /// <param name="x">The horizontal coordinate, in document space.</param>
    /// <param name="y">The vertical coordinate, in document space.</param>
    /// <param name="color">The colour to write.</param>
    /// <returns><see langword="true"/> if the pixel changed.</returns>
    public bool SetPixel(int x, int y, PixelColor color)
    {
        if (!CanEdit(x, y))
        {
            return false;
        }

        int bufferX = x - _origin.X;
        int bufferY = y - _origin.Y;

        PixelColor existing = _cel.Pixels.GetPixel(bufferX, bufferY);
        if (existing == color)
        {
            return false;
        }

        Record(bufferX, bufferY, existing);
        _cel.Pixels.SetPixel(bufferX, bufferY, color);
        _dirtyRegion = _dirtyRegion.Union(bufferX, bufferY);
        return true;
    }

    /// <summary>
    /// Composites a colour over a pixel using source-over blending, recording its previous
    /// value.
    /// </summary>
    /// <param name="x">The horizontal coordinate, in document space.</param>
    /// <param name="y">The vertical coordinate, in document space.</param>
    /// <param name="color">The colour to blend on top.</param>
    /// <returns><see langword="true"/> if the pixel changed.</returns>
    public bool BlendPixel(int x, int y, PixelColor color)
    {
        if (!CanEdit(x, y))
        {
            return false;
        }

        PixelColor existing = _cel.Pixels.GetPixel(x - _origin.X, y - _origin.Y);
        return SetPixel(x, y, ColorBlending.SourceOver(color, existing));
    }

    /// <summary>
    /// Publishes the changes made so far to the document so the canvas repaints mid-stroke.
    /// Call this as the pointer moves; it does not end the session.
    /// </summary>
    public void PublishInterimChanges()
    {
        if (HasChanges)
        {
            _document.NotifyPixelsChanged(_dirtyRegion.Translate(_cel.Offset.X, _cel.Offset.Y));
        }
    }

    /// <summary>
    /// Finishes the session and produces the history entry.
    /// </summary>
    /// <returns>
    /// The command describing the change, or <see langword="null"/> if nothing changed — in
    /// which case nothing should be pushed onto the history.
    /// </returns>
    public IUndoableCommand? Commit()
    {
        if (!HasChanges)
        {
            return null;
        }

        ImageRect region = _dirtyRegion;
        byte[] after = _cel.Pixels.ExtractRegion(region);
        byte[] before = BuildBeforeImage(region, after);

        _document.NotifyPixelsChanged(region.Translate(_cel.Offset.X, _cel.Offset.Y));
        return new RasterEditCommand(_name, _document, _cel, region, before, after);
    }

    /// <summary>
    /// Abandons the session, restoring every pixel it changed. Used when a stroke is
    /// cancelled — for example by pressing Escape or right-clicking mid-drag.
    /// </summary>
    public void Rollback() => ResetToOriginal();

    /// <summary>
    /// Restores every pixel this session has changed and leaves the session open and
    /// reusable.
    /// </summary>
    /// <remarks>
    /// This is what makes a live shape preview one undo step. A rectangle tool redraws its
    /// outline on every pointer move, and each redraw has to erase the last one; doing that
    /// by resetting the session rather than by opening a new one means the artist gets a
    /// single history entry for the whole drag, with the original pixels correctly recorded
    /// from before the first preview.
    /// </remarks>
    public void ResetToOriginal()
    {
        if (!HasChanges)
        {
            return;
        }

        if (_snapshot is not null)
        {
            _cel.Pixels.ReplaceRegion(_dirtyRegion, _snapshot.ExtractRegion(_dirtyRegion));
        }
        else if (_originals is not null)
        {
            foreach ((int index, PixelColor original) in _originals)
            {
                _cel.Pixels.SetPixel(index % _cel.Pixels.Width, index / _cel.Pixels.Width, original);
            }
        }

        _document.NotifyPixelsChanged(_dirtyRegion.Translate(_cel.Offset.X, _cel.Offset.Y));
        _dirtyRegion = ImageRect.Empty;
    }

    /// <summary>
    /// Decides whether a document-space pixel may be written: it has to lie inside the buffer,
    /// and inside the selection if there is one.
    /// </summary>
    private bool CanEdit(int x, int y) =>
        _cel.Pixels.Size.Contains(x - _origin.X, y - _origin.Y)
        && (_selection is null || _selection.IsSelected(x, y));

    /// <summary>Remembers a pixel's original value, in <em>buffer</em> coordinates.</summary>
    private void Record(int x, int y, PixelColor original)
    {
        // The snapshot strategy already holds every original value, so there is nothing
        // per-pixel to remember.
        if (_originals is null)
        {
            return;
        }

        int index = (y * _cel.Pixels.Width) + x;
        _originals.TryAdd(index, original);
    }

    /// <summary>
    /// Reconstructs what <paramref name="region"/> looked like before the operation.
    /// </summary>
    /// <param name="region">The changed rectangle.</param>
    /// <param name="after">The region's current contents, reused as the starting point.</param>
    /// <returns>The region's contents prior to the operation.</returns>
    private byte[] BuildBeforeImage(ImageRect region, byte[] after)
    {
        if (_snapshot is not null)
        {
            return _snapshot.ExtractRegion(region);
        }

        // Start from the current contents and undo the recorded pixels. Pixels inside the
        // dirty rectangle that were never touched are identical in both images, so copying
        // them across is correct as well as cheap.
        byte[] before = (byte[])after.Clone();
        int width = _cel.Pixels.Width;
        int rowBytes = region.Width * PixelBuffer.BytesPerPixel;

        foreach ((int index, PixelColor original) in _originals!)
        {
            int x = index % width;
            int y = index / width;
            if (!region.Contains(x, y))
            {
                continue;
            }

            int offset = ((y - region.Top) * rowBytes) + ((x - region.Left) * PixelBuffer.BytesPerPixel);
            before[offset] = original.R;
            before[offset + 1] = original.G;
            before[offset + 2] = original.B;
            before[offset + 3] = original.A;
        }

        return before;
    }
}
