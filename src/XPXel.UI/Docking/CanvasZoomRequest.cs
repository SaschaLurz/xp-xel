namespace XPXel.UI.Docking;

/// <summary>
/// A zoom action a menu command or keyboard shortcut is asking a canvas to perform.
/// </summary>
/// <remarks>
/// These are requests rather than values because two of them — <see cref="Fit"/> in
/// particular — depend on the viewport size, which only the canvas control knows. Sending an
/// intent instead of a number keeps that knowledge in the one place that has it.
/// </remarks>
public enum CanvasZoomRequest
{
    /// <summary>Zoom in one step, centred on the canvas.</summary>
    In = 0,

    /// <summary>Zoom out one step, centred on the canvas.</summary>
    Out,

    /// <summary>Return to one screen pixel per image pixel.</summary>
    ActualSize,

    /// <summary>Scale the image so the whole of it is visible.</summary>
    Fit,
}
