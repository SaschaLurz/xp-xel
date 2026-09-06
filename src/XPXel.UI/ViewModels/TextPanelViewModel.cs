using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XPXel.Core.Documents;
using XPXel.Core.Editing;
using XPXel.Core.History;
using XPXel.Core.Primitives;
using XPXel.Core.Text;

// Avalonia has a TextAlignment too. The alias keeps the document model unqualified here,
// since that is the one this panel edits.
using TextAlignment = XPXel.Core.Text.TextAlignment;

namespace XPXel.UI.ViewModels;

/// <summary>
/// The text panel: the words of the selected text layer and the typography they are drawn
/// with — font, size, weight, slant, decoration, alignment, line spacing and colour.
/// </summary>
/// <remarks>
/// <para>
/// <b>It edits one of two things.</b> When a text layer is selected, the panel edits that
/// layer and every change is undoable. When none is, it edits
/// <see cref="ToolSettings.TextDefaults"/> — the typography the <em>next</em> piece of text
/// will start with — and changes nothing in the document. The panel says which of the two it
/// is doing rather than greying itself out, because "why is this disabled?" is a worse
/// question to leave an artist with than "what am I changing?".
/// </para>
/// <para>
/// <b>Changes go through the history, not straight onto the layer.</b> Every control here is
/// a discrete action — a checkbox, a stepper, a choice from a list — so each one produces one
/// undo entry, which is what an artist expects from a formatting toolbar. There is
/// deliberately no slider on this panel for that reason: dragging one would fill the history
/// with a hundred entries recording a single decision.
/// </para>
/// <para>
/// <b>While the artist is typing on the canvas, the live session takes over.</b> A restyle
/// during a text edit joins that edit's single history entry instead of making its own, so
/// typing a word and then making it bold is one undo, not two.
/// </para>
/// </remarks>
public sealed partial class TextPanelViewModel : ViewModelBase, IDisposable
{
    private readonly ToolSettings _settings;
    private readonly ColorSlots _colors;
    private readonly ITextRasterizer _rasterizer;

    private DocumentWorkspace? _workspace;
    private TextLayer? _target;

    /// <summary>
    /// Set while the panel is writing its own values back from the model, so that a property
    /// change raised by the write does not come straight back in as an edit.
    /// </summary>
    private bool _isSynchronising;

    /// <summary>Initialises the panel.</summary>
    /// <param name="settings">Where the defaults for new text live.</param>
    /// <param name="colors">The drawing colours, for "use the primary colour".</param>
    /// <param name="rasterizer">The font engine, for the font list and for re-rendering.</param>
    public TextPanelViewModel(ToolSettings settings, ColorSlots colors, ITextRasterizer rasterizer)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(colors);
        ArgumentNullException.ThrowIfNull(rasterizer);

        _settings = settings;
        _colors = colors;
        _rasterizer = rasterizer;

        FontFamilies = rasterizer.FontFamilies;

        RebuildFontSizes();
        PullFromModel();
    }

    /// <summary>Refills the size list for the font currently chosen.</summary>
    /// <remarks>
    /// Rebuilt only when the set actually differs. Clearing a collection a
    /// <c>ComboBox</c> is bound to drops its selection, and the two-way binding then writes
    /// that null back — so an unconditional rebuild would reset the artist's font size every
    /// time the panel read the model, which it does on every keystroke.
    /// </remarks>
    private void RebuildFontSizes()
    {
        IReadOnlyList<decimal> sizes = PixelFont.IsBuiltIn(FontFamily)
            ? [.. PixelFontRasterizer.GetExactSizes().Select(s => (decimal)s)]
            : SystemFontSizes;

        if (AvailableFontSizes.SequenceEqual(sizes))
        {
            return;
        }

        decimal selected = FontSize;

        AvailableFontSizes.Clear();

        foreach (decimal size in sizes)
        {
            AvailableFontSizes.Add(size);
        }

        FontSize = selected;
        OnPropertyChanged(nameof(SelectedFontSize));
    }

    /// <summary>The fonts this machine can render, for the family list.</summary>
    public IReadOnlyList<string> FontFamilies { get; }

    /// <summary>The sizes offered for a system font.</summary>
    /// <remarks>
    /// Starts at <see cref="TextStyle.MinimumSystemFontSize"/>. Smaller sizes are not offered
    /// because an outline font rendered without anti-aliasing stops being readable below it,
    /// and the built-in pixel font is the right answer for anything smaller.
    /// </remarks>
    private static IReadOnlyList<decimal> SystemFontSizes { get; } =
        [10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 40, 48, 64, 96];

    /// <summary>The sizes offered for the font currently chosen.</summary>
    /// <remarks>
    /// The built-in font is offered only the whole multiples it renders exactly, so that an
    /// artist cannot pick a size and silently be given a different one.
    /// </remarks>
    public ObservableCollection<decimal> AvailableFontSizes { get; } = [];

    /// <summary>The smallest size the chosen font may be set to.</summary>
    public decimal MinimumFontSize => (decimal)TextStyle.GetMinimumFontSize(FontFamily);

    /// <summary>
    /// The size as the drop-down sees it: one of the offered sizes, or <see langword="null"/>
    /// when the artist has typed a size that is not among them.
    /// </summary>
    /// <remarks>
    /// The drop-down cannot bind to <see cref="FontSize"/> directly. A <c>ComboBox</c> whose
    /// <c>SelectedItem</c> is not in its list reports null, and a two-way binding then writes
    /// that null into a <see cref="decimal"/> — a failure logged on every keystroke, and the
    /// reason a typed size such as 13 could not survive the panel refreshing itself. Reading
    /// null here and ignoring it on the way back is what lets the box and the list disagree
    /// peacefully.
    /// </remarks>
    public decimal? SelectedFontSize
    {
        get => AvailableFontSizes.Contains(FontSize) ? FontSize : null;

        set
        {
            if (value is { } chosen)
            {
                FontSize = chosen;
            }
        }
    }

    /// <summary>Gets a value indicating whether the built-in pixel font is chosen.</summary>
    public bool IsPixelFont => PixelFont.IsBuiltIn(FontFamily);

    /// <summary>The alignments offered by the alignment buttons.</summary>
    public static IReadOnlyList<TextAlignment> Alignments { get; } = Enum.GetValues<TextAlignment>();

    /// <summary>The text layer being edited, or <see langword="null"/> when none is selected.</summary>
    public TextLayer? Target
    {
        get => _target;
        private set
        {
            if (ReferenceEquals(_target, value))
            {
                return;
            }

            // The layer is edited from two directions — this panel, and the artist typing on
            // the canvas — so the panel follows the layer rather than assuming it is the only
            // thing changing it. Without this the text box shows whatever the layer said at
            // the moment it was selected and never catches up.
            if (_target is not null)
            {
                _target.PropertyChanged -= OnTargetChanged;
            }

            _target = value;

            if (_target is not null)
            {
                _target.PropertyChanged += OnTargetChanged;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTarget));
            OnPropertyChanged(nameof(TargetDescription));
            PullFromModel();
        }
    }

    /// <summary>Gets a value indicating whether a text layer is selected.</summary>
    public bool HasTarget => _target is not null;

    /// <summary>What the panel is currently editing, shown at the top of it.</summary>
    public string TargetDescription => _target is { } layer
        ? $"Editing “{layer.Name}”"
        : "No text layer selected — these are the settings new text will use.";

    /// <summary>The words of the selected layer.</summary>
    [ObservableProperty]
    private string _text = string.Empty;

    /// <summary>The font family name.</summary>
    [ObservableProperty]
    private string _fontFamily = TextStyle.DefaultFontFamily;

    /// <summary>
    /// The font size in pixels.
    /// </summary>
    /// <remarks>
    /// Decimal rather than double, which looks like a needless conversion and is not. Both the
    /// stepper and the size list bind straight to this, and Avalonia's numeric controls are
    /// decimal throughout; feeding them a double leaves the framework to find a conversion at
    /// run time, which it reports as a binding failure rather than a compiler error. Keeping
    /// the panel in the currency its controls use puts the one conversion here, where it is
    /// visible.
    /// </remarks>
    [ObservableProperty]
    private decimal _fontSize = 16;

    /// <summary>Whether the text is bold.</summary>
    [ObservableProperty]
    private bool _isBold;

    /// <summary>Whether the text is italic.</summary>
    [ObservableProperty]
    private bool _isItalic;

    /// <summary>Whether the text is underlined.</summary>
    [ObservableProperty]
    private bool _isUnderline;

    /// <summary>Whether the text is struck through.</summary>
    [ObservableProperty]
    private bool _isStrikethrough;

    /// <summary>How the lines line up with one another.</summary>
    [ObservableProperty]
    private TextAlignment _alignment;

    /// <summary>The line height multiplier.</summary>
    [ObservableProperty]
    private double _lineSpacing = 1.0;

    /// <summary>
    /// Whether glyph edges are smoothed. Off suits sprite work; on suits larger type.
    /// </summary>
    [ObservableProperty]
    private bool _isAntiAliased;

    /// <summary>The glyph colour, as a swatch for the panel.</summary>
    public IBrush ColorBrush => CurrentStyle.Color.ToBrush();

    /// <summary>Points the panel at a different document, or at none.</summary>
    /// <param name="workspace">The document to follow.</param>
    public void Attach(DocumentWorkspace? workspace)
    {
        if (ReferenceEquals(_workspace, workspace))
        {
            return;
        }

        if (_workspace is not null)
        {
            _workspace.PropertyChanged -= OnWorkspaceChanged;
        }

        _workspace = workspace;

        if (_workspace is not null)
        {
            _workspace.PropertyChanged += OnWorkspaceChanged;
        }

        UpdateTarget();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_workspace is not null)
        {
            _workspace.PropertyChanged -= OnWorkspaceChanged;
            _workspace = null;
        }

        if (_target is not null)
        {
            _target.PropertyChanged -= OnTargetChanged;
            _target = null;
        }
    }

    /// <summary>Applies an alignment, used by the alignment buttons.</summary>
    /// <param name="alignment">The alignment to apply.</param>
    [RelayCommand]
    private void SetAlignment(TextAlignment alignment) => Alignment = alignment;

    /// <summary>Sets the text colour to the current primary drawing colour.</summary>
    [RelayCommand]
    private void UsePrimaryColor() => ApplyStyle("Text Colour", style => style.Color = _colors.Primary);

    /// <summary>
    /// The style the panel's values currently describe: the selected layer's own, or the
    /// defaults for new text.
    /// </summary>
    private TextStyle CurrentStyle => _target?.Style ?? _settings.TextDefaults;

    // ---------------------------------------------------------------------------------
    // Property change handlers
    // ---------------------------------------------------------------------------------

    partial void OnTextChanged(string value)
    {
        if (_isSynchronising || _target is not { } layer || _workspace is not { } workspace)
        {
            return;
        }

        // Typing into this box is one edit, not one per letter. Opening a live session is what
        // collapses it: the session holds the state from before the first keystroke and records
        // a single entry when the box loses focus — or when anything else ends the edit, such as
        // clicking the canvas or saving. Without it, binding the box to every keystroke would
        // fill the history with one entry per character, and binding it to lost focus instead
        // meant the canvas showed nothing at all until the artist clicked away.
        if (workspace.ActiveTextEdit is not { } session || !ReferenceEquals(session.Layer, layer))
        {
            workspace.BeginTextEdit(new TextEditSession(workspace, layer, _rasterizer, isNewLayer: false));
        }

        ApplyChange("Edit Text", target => target.Text = value);
    }

    /// <summary>
    /// Ends the editing episode the text box started, recording it as one history entry.
    /// </summary>
    /// <remarks>
    /// Called when the box loses focus. Safe when nothing is being edited, and safe when the
    /// artist is typing on the canvas instead — the session is the same one either way.
    /// </remarks>
    public void CommitPendingText() => _workspace?.CommitTextEdit();

    partial void OnFontFamilyChanged(string value)
    {
        ApplyStyle("Change Font", s => s.FontFamily = value);
        RebuildFontSizes();

        OnPropertyChanged(nameof(MinimumFontSize));
        OnPropertyChanged(nameof(IsPixelFont));
    }

    partial void OnFontSizeChanged(decimal value)
    {
        // A size the font cannot render is corrected to the nearest one it can, rather than
        // refused. Refusing leaves the artist guessing what the limit is — and the limit moves,
        // because the pixel font counts in whole cells and an outline font does not.
        decimal usable = (decimal)TextStyle.ClampFontSize((double)value, FontFamily);

        if (usable != value)
        {
            FontSize = usable;
            return;
        }

        OnPropertyChanged(nameof(SelectedFontSize));
        ApplyStyle("Change Font Size", s => s.FontSize = (double)value);
    }

    partial void OnIsBoldChanged(bool value) => ApplyStyle("Bold", s => s.IsBold = value);

    partial void OnIsItalicChanged(bool value) => ApplyStyle("Italic", s => s.IsItalic = value);

    partial void OnIsUnderlineChanged(bool value) => ApplyStyle("Underline", s => s.IsUnderline = value);

    partial void OnIsStrikethroughChanged(bool value) => ApplyStyle("Strikethrough", s => s.IsStrikethrough = value);

    partial void OnAlignmentChanged(TextAlignment value) => ApplyStyle("Align Text", s => s.Alignment = value);

    partial void OnLineSpacingChanged(double value) => ApplyStyle("Line Spacing", s => s.LineSpacing = value);

    partial void OnIsAntiAliasedChanged(bool value) => ApplyStyle("Anti-alias Text", s => s.IsAntiAliased = value);

    // ---------------------------------------------------------------------------------
    // Applying changes
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Applies a change to the style being edited: to the layer, undoably, or to the defaults
    /// for new text.
    /// </summary>
    private void ApplyStyle(string actionName, Action<TextStyle> mutate)
    {
        if (_isSynchronising)
        {
            return;
        }

        if (_target is null)
        {
            // No layer selected, so this is a preference for the next piece of text. Nothing
            // in the document changes and nothing goes on the undo stack.
            mutate(_settings.TextDefaults);

            // Read straight back, because the style clamps what it is given — a size between
            // two whole multiples of the pixel font's cell, say — and the panel must show what
            // was actually stored rather than what was asked for.
            PullFromModel();
            return;
        }

        ApplyChange(actionName, layer => mutate(layer.Style));
        OnPropertyChanged(nameof(ColorBrush));
    }

    /// <summary>
    /// Applies a change to the selected text layer, recording it in the history unless the
    /// artist is mid-typing, in which case it joins that edit.
    /// </summary>
    private void ApplyChange(string actionName, Action<TextLayer> mutate)
    {
        if (_target is not { } layer || _workspace is not { } workspace)
        {
            return;
        }

        // While a text edit is live, the session already holds the "before" state and will
        // record one entry for the whole episode. Adding a second entry here would make undo
        // step through a restyle the artist experienced as part of typing.
        if (workspace.ActiveTextEdit is { } session && ReferenceEquals(session.Layer, layer))
        {
            mutate(layer);
            session.Refresh();
            return;
        }

        string beforeText = layer.Text;
        TextStyle beforeStyle = layer.Style.Clone();
        ImagePoint beforeOrigin = layer.Origin;

        mutate(layer);

        if (string.Equals(beforeText, layer.Text, StringComparison.Ordinal)
            && beforeStyle.VisuallyEquals(layer.Style)
            && beforeOrigin == layer.Origin)
        {
            return;
        }

        SetTextCommand command = new(
            actionName,
            workspace.Document,
            layer,
            workspace.ActiveFrame,
            _rasterizer,
            beforeText,
            beforeStyle,
            beforeOrigin,
            layer.Text,
            layer.Style,
            layer.Origin);

        // The layer already carries the new values, so the command is pushed rather than
        // executed — but it still has to render, because nothing else has yet.
        TextLayerRenderer.Render(workspace.Document, layer, workspace.ActiveFrame, _rasterizer);
        workspace.History.Push(command);
    }

    /// <summary>
    /// Re-reads the layer after something else changed it — typing on the canvas, an undo, or
    /// a project being loaded.
    /// </summary>
    private void OnTargetChanged(object? sender, PropertyChangedEventArgs e)
    {
        // TextLayer forwards its style's notifications with a "Style." prefix, so one
        // test covers the words, the typography and the position alike.
        if (e.PropertyName is nameof(TextLayer.Text) or nameof(TextLayer.Origin)
            || e.PropertyName?.StartsWith("Style.", StringComparison.Ordinal) == true)
        {
            PullFromModel();
        }
    }

    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentWorkspace.ActiveLayer) or nameof(DocumentWorkspace.ActiveTextEdit))
        {
            UpdateTarget();
        }
    }

    private void UpdateTarget()
    {
        // The layer being typed into wins over the merely selected one. They are normally the
        // same, but a click in the layers panel mid-edit can separate them, and the artist's
        // caret is the better guide to what they think they are formatting.
        Target = _workspace?.ActiveTextEdit?.Layer ?? _workspace?.ActiveTextLayer;
    }

    private void PullFromModel()
    {
        TextStyle style = CurrentStyle;

        _isSynchronising = true;

        try
        {
            Text = _target?.Text ?? string.Empty;
            FontFamily = style.FontFamily;
            FontSize = (decimal)style.FontSize;
            IsBold = style.IsBold;
            IsItalic = style.IsItalic;
            IsUnderline = style.IsUnderline;
            IsStrikethrough = style.IsStrikethrough;
            Alignment = style.Alignment;
            LineSpacing = style.LineSpacing;
            IsAntiAliased = style.IsAntiAliased;
        }
        finally
        {
            _isSynchronising = false;
        }

        RebuildFontSizes();

        OnPropertyChanged(nameof(ColorBrush));
        OnPropertyChanged(nameof(MinimumFontSize));
        OnPropertyChanged(nameof(IsPixelFont));
    }
}
