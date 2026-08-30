using XPXel.Core.Primitives;

namespace XPXel.Core.Tools;

/// <summary>Which pointer button drove an interaction.</summary>
public enum ToolPointerButton
{
    /// <summary>No button, such as a hover with nothing pressed.</summary>
    None = 0,

    /// <summary>The primary button, conventionally bound to the primary colour.</summary>
    Left = 1,

    /// <summary>The secondary button, conventionally bound to the secondary colour.</summary>
    Right = 2,

    /// <summary>The middle button, conventionally reserved for panning.</summary>
    Middle = 3,
}

/// <summary>Modifier keys held during a pointer interaction.</summary>
[Flags]
public enum ToolModifiers
{
    /// <summary>No modifiers.</summary>
    None = 0,

    /// <summary>Shift, conventionally constrains a stroke to a straight line.</summary>
    Shift = 1,

    /// <summary>Control, conventionally temporarily activates the colour picker.</summary>
    Control = 2,

    /// <summary>Alt, conventionally toggles a tool's alternate behaviour.</summary>
    Alt = 4,
}

/// <summary>
/// A single pointer interaction, expressed entirely in image coordinates.
/// </summary>
/// <remarks>
/// <para>
/// This type is the boundary between the UI and the tools. The canvas control converts
/// Avalonia's screen-space pointer events into these — applying the zoom and pan transform
/// once, in one place — so no tool ever deals with zoom, scroll position, device pixel
/// ratio, or Avalonia types at all.
/// </para>
/// <para>
/// Buttons and modifiers are XP-xel's own enumerations rather than the framework's for the
/// same reason: it keeps <c>XPXel.Core</c> free of UI dependencies and means tools can be
/// unit-tested by constructing these directly.
/// </para>
/// </remarks>
public sealed class ToolPointerEventArgs : EventArgs
{
    /// <summary>Initialises the arguments.</summary>
    /// <param name="position">The pointer position in image coordinates.</param>
    /// <param name="button">The button involved.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    public ToolPointerEventArgs(ImagePoint position, ToolPointerButton button, ToolModifiers modifiers = ToolModifiers.None)
    {
        Position = position;
        Button = button;
        Modifiers = modifiers;
    }

    /// <summary>
    /// The pointer position in image coordinates. May lie outside the canvas: the artist can
    /// legitimately drag beyond the edge mid-stroke, and tools clip rather than reject.
    /// </summary>
    public ImagePoint Position { get; }

    /// <summary>The button involved in this interaction.</summary>
    public ToolPointerButton Button { get; }

    /// <summary>The modifier keys held at the time.</summary>
    public ToolModifiers Modifiers { get; }

    /// <summary>Gets a value indicating whether Shift was held.</summary>
    public bool IsShiftDown => Modifiers.HasFlag(ToolModifiers.Shift);

    /// <summary>Gets a value indicating whether Control was held.</summary>
    public bool IsControlDown => Modifiers.HasFlag(ToolModifiers.Control);

    /// <summary>Gets a value indicating whether Alt was held.</summary>
    public bool IsAltDown => Modifiers.HasFlag(ToolModifiers.Alt);
}
