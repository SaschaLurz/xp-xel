using XPXel.Core.Documents;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;

namespace XPXel.Core.History;

/// <summary>
/// One drawing operation across every layer it is meant to reach: a
/// <see cref="RasterEditSession"/> for each of them, and a single history entry for the lot.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a tool no longer holds a session.</b> Marking several layers in the layers panel used
/// to mean something for deleting, cutting, copying and transforming, and nothing at all for
/// drawing — a stroke went to the active layer whatever else was highlighted. That split was
/// impossible to see and impossible to remember, so drawing now goes wherever every other
/// operation goes: every marked layer. A tool asks the context for a scope and writes through
/// it exactly as it used to write through one session.
/// </para>
/// <para>
/// <b>The sessions are kept apart, not merged.</b> Each layer records its own before-and-after
/// pixels, which is what makes an undo put every layer back rather than approximately back, and
/// what lets a soft brush or the eraser composite against the value that <em>that</em> layer
/// held before the stroke. A scope that flattened the layers into one buffer would get both of
/// those wrong in ways that only show up on partly transparent pixels.
/// </para>
/// <para>
/// <b><see cref="Primary"/> is the layer being aimed at.</b> Writing fans out to every session,
/// but reading cannot: the flood fill has to decide what region to fill from somebody's pixels,
/// and the only defensible answer is the layer the artist is looking at and clicked on. So the
/// active layer comes first and anything that reads goes to it.
/// </para>
/// </remarks>
public sealed class RasterEditScope
{
    private readonly RasterEditSession[] _sessions;

    private RasterEditScope(string actionName, RasterEditSession[] sessions)
    {
        ActionName = actionName;
        _sessions = sessions;
    }

    /// <summary>What the Edit menu calls this operation.</summary>
    public string ActionName { get; }

    /// <summary>
    /// Every session this scope writes through, the active layer's first.
    /// </summary>
    /// <remarks>
    /// Exposed because a tool that composites — the soft brush, the eraser — has to work
    /// against each layer's own pre-stroke pixels, which only that layer's session knows.
    /// </remarks>
    public IReadOnlyList<RasterEditSession> Sessions => _sessions;

    /// <summary>The session for the layer the artist is aiming at.</summary>
    public RasterEditSession Primary => _sessions[0];

    /// <summary>Gets a value indicating whether this operation reaches more than one layer.</summary>
    public bool SpansSeveralLayers => _sessions.Length > 1;

    /// <summary>How many layers this operation reaches.</summary>
    public int LayerCount => _sessions.Length;

    /// <summary>
    /// The buffer being aimed at, for tools that have to read the artwork before changing it.
    /// </summary>
    /// <remarks>
    /// Its coordinates are the <em>buffer's</em>, not the document's; add <see cref="Origin"/>
    /// to convert. See <see cref="RasterEditSession.Pixels"/>, whose remarks this shares.
    /// </remarks>
    public PixelBuffer Pixels => Primary.Pixels;

    /// <summary>Where <see cref="Pixels"/> sits in document coordinates.</summary>
    public ImagePoint Origin => Primary.Origin;

    /// <summary>Starts an operation over a set of cels.</summary>
    /// <param name="document">The document being edited.</param>
    /// <param name="actionName">The action name for the Edit menu.</param>
    /// <param name="cels">
    /// The cels to write to, the one being aimed at first. An empty sequence produces no scope,
    /// because an operation with nowhere to go is not an operation.
    /// </param>
    /// <param name="selection">The active selection, or <see langword="null"/> for none.</param>
    /// <param name="useSnapshot">
    /// <see langword="true"/> for operations that may touch most of the canvas; see
    /// <see cref="RasterEditSession"/> for the trade-off.
    /// </param>
    /// <returns>The scope, or <see langword="null"/> when there is nothing to write to.</returns>
    public static RasterEditScope? Begin(
        PixelDocument document,
        string actionName,
        IEnumerable<Cel> cels,
        Selection.SelectionMask? selection = null,
        bool useSnapshot = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(actionName);
        ArgumentNullException.ThrowIfNull(cels);

        List<RasterEditSession> sessions = [];

        foreach (Cel cel in cels)
        {
            sessions.Add(useSnapshot
                ? RasterEditSession.BeginWithSnapshot(document, cel, actionName, selection)
                : RasterEditSession.Begin(document, cel, actionName, selection));
        }

        return sessions.Count == 0 ? null : new RasterEditScope(actionName, [.. sessions]);
    }

    /// <summary>Writes a pixel on every layer this scope reaches.</summary>
    /// <param name="x">The horizontal coordinate, in document space.</param>
    /// <param name="y">The vertical coordinate, in document space.</param>
    /// <param name="color">The colour to write.</param>
    /// <returns><see langword="true"/> if any layer actually changed.</returns>
    public bool SetPixel(int x, int y, PixelColor color)
    {
        bool changed = false;

        foreach (RasterEditSession session in _sessions)
        {
            changed |= session.SetPixel(x, y, color);
        }

        return changed;
    }

    /// <summary>Shows the changes made so far, so the canvas repaints mid-stroke.</summary>
    public void PublishInterimChanges()
    {
        foreach (RasterEditSession session in _sessions)
        {
            session.PublishInterimChanges();
        }
    }

    /// <summary>
    /// Restores every pixel changed so far and leaves the scope open, which is what makes a
    /// live shape preview cost one undo entry however many times it is redrawn.
    /// </summary>
    public void ResetToOriginal()
    {
        foreach (RasterEditSession session in _sessions)
        {
            session.ResetToOriginal();
        }
    }

    /// <summary>Abandons the operation, restoring every pixel it changed on every layer.</summary>
    public void Rollback() => ResetToOriginal();

    /// <summary>
    /// Finishes the operation and produces its history entry.
    /// </summary>
    /// <returns>
    /// One command covering every layer that actually changed, or <see langword="null"/> if
    /// none did — in which case nothing should be pushed onto the history.
    /// </returns>
    /// <remarks>
    /// One entry, not one per layer: a single stroke is a single thing the artist did, and
    /// undoing it should not require as many presses as they happened to have layers marked.
    /// </remarks>
    public IUndoableCommand? Commit()
    {
        List<IUndoableCommand> parts = [];

        foreach (RasterEditSession session in _sessions)
        {
            if (session.Commit() is { } command)
            {
                parts.Add(command);
            }
        }

        return parts.Count switch
        {
            0 => null,
            1 => parts[0],
            _ => new CompositeCommand(ActionName, parts),
        };
    }
}
