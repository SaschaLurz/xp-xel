using XPXel.Core.ComponentModel;
using XPXel.Core.Primitives;

namespace XPXel.Core.Animation;

/// <summary>
/// How the frames on either side of the current one are ghosted under it.
/// </summary>
/// <remarks>
/// <para>
/// Onion skinning is the single most useful thing an animator has: it is how you line up the
/// contact pose of a walk cycle with the one before it, without flipping back and forth and
/// guessing. The name comes from the translucent paper animators drew on.
/// </para>
/// <para>
/// <b>Past and future are tinted differently on purpose.</b> A ghost that is merely faint
/// tells you a drawing is nearby but not whether the motion is coming or going. Red behind
/// and green ahead is the convention every animation package uses, and it is what makes a
/// three-frame sandwich readable at a glance.
/// </para>
/// <para>
/// These are application settings rather than document data — one artist's working aid, not
/// part of the artwork — so they are never written to a project file.
/// </para>
/// </remarks>
public sealed class OnionSkinSettings : ObservableModel
{
    /// <summary>The most frames that may be ghosted on either side.</summary>
    /// <remarks>
    /// Past three the ghosts overlap into an unreadable smear, and each one costs a full
    /// composite of the document on every repaint.
    /// </remarks>
    public const int MaximumFrames = 3;

    private bool _isEnabled = true;
    private int _framesBefore = 1;
    private int _framesAfter = 1;
    private int _opacity = 40;
    private bool _isTinted = true;
    private PixelColor _beforeTint = new(255, 64, 64);
    private PixelColor _afterTint = new(64, 200, 96);

    /// <summary>
    /// Whether ghosts are drawn at all. On by default.
    /// </summary>
    /// <remarks>
    /// Defaulting this on costs a still image nothing — the compositor delegates straight to
    /// the ordinary one when a document has a single frame — and it means an artist who adds
    /// a second frame sees the first one underneath without having to know the feature exists
    /// or what it is called. Discovering onion skinning by needing it is much harder than
    /// switching it off once.
    /// </remarks>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    /// <summary>How many earlier frames to ghost, 0 to <see cref="MaximumFrames"/>.</summary>
    public int FramesBefore
    {
        get => _framesBefore;
        set => SetProperty(ref _framesBefore, Math.Clamp(value, 0, MaximumFrames));
    }

    /// <summary>How many later frames to ghost, 0 to <see cref="MaximumFrames"/>.</summary>
    public int FramesAfter
    {
        get => _framesAfter;
        set => SetProperty(ref _framesAfter, Math.Clamp(value, 0, MaximumFrames));
    }

    /// <summary>
    /// How strongly the nearest ghost shows, as a percentage. Ghosts further away fade
    /// proportionally, so their distance is visible rather than merely implied.
    /// </summary>
    public int Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, Math.Clamp(value, 5, 100));
    }

    /// <summary>
    /// Whether ghosts are recoloured to show which side of the current frame they are on.
    /// Turning it off leaves them in their own colours, which suits checking a colour choice
    /// against the neighbouring drawings.
    /// </summary>
    public bool IsTinted
    {
        get => _isTinted;
        set => SetProperty(ref _isTinted, value);
    }

    /// <summary>The colour earlier frames are tinted.</summary>
    public PixelColor BeforeTint
    {
        get => _beforeTint;
        set => SetProperty(ref _beforeTint, value);
    }

    /// <summary>The colour later frames are tinted.</summary>
    public PixelColor AfterTint
    {
        get => _afterTint;
        set => SetProperty(ref _afterTint, value);
    }

    /// <summary>
    /// The opacity a ghost at a given distance should be drawn at, as a fraction of 1.
    /// </summary>
    /// <param name="distance">How many frames away the ghost is; 1 is the nearest.</param>
    /// <returns>The fraction to multiply the ghost's alpha by.</returns>
    public double GetOpacityAtDistance(int distance)
    {
        int steps = Math.Max(1, distance);
        return Opacity / 100.0 / steps;
    }
}
