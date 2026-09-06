using XPXel.Core.Primitives;
using XPXel.Core.Text;

namespace XPXel.Core.Documents;

/// <summary>
/// A layer whose pixels are generated from a piece of text, and which stays editable as text
/// for the life of the document.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where the pixels live.</b> A text layer's rendered glyphs go into an ordinary
/// <see cref="Cel"/>, exactly like a <see cref="RasterLayer"/>'s artwork. That single
/// decision is what keeps text cheap: the compositor, the exporters, the layer thumbnails
/// and the <c>.xpxel</c> archive all already know how to handle a cel, so none of them
/// needed changing. What makes the layer <em>re-editable</em> is that the source
/// <see cref="Text"/> and <see cref="Style"/> are kept here and written to the manifest, so
/// the pixels can always be regenerated.
/// </para>
/// <para>
/// <b>The cel is a cache, not the truth.</b> Anything that changes the text, the style or
/// the origin must re-render through <see cref="TextLayerRenderer"/>. Editing a text layer's
/// pixels with a drawing tool is deliberately not possible — the next re-render would throw
/// the edit away — which is why <see cref="Editing.DocumentWorkspace.ActiveRasterLayer"/>
/// does not match this type. Artists who want to touch up text rasterise it first by
/// converting the layer, or draw on a layer above.
/// </para>
/// <para>
/// <b>Forward compatibility.</b> An older build that does not know this layer type reads it
/// from a project file as an unknown kind. It still sees the cel, so the text is visible and
/// exports correctly; it simply cannot re-edit the words.
/// </para>
/// </remarks>
public sealed class TextLayer : LayerNode
{
    private string _text = string.Empty;
    private ImagePoint _origin;

    /// <summary>Initialises a text layer.</summary>
    /// <param name="id">The layer's stable identity.</param>
    /// <param name="name">The display name shown in the layer panel.</param>
    /// <param name="style">
    /// The typography to adopt, which is copied rather than referenced so that later changes
    /// to the caller's style — the tool defaults, typically — do not reach into the document.
    /// </param>
    public TextLayer(Guid id, string name, TextStyle? style = null)
        : base(id, name)
    {
        Style = style?.Clone() ?? new TextStyle();

        // A style change is a change to the layer, so the two notify as one. The layer panel
        // and the re-render trigger both listen here rather than to the style separately.
        Style.PropertyChanged += (_, e) => OnPropertyChanged(nameof(Style) + "." + e.PropertyName);
    }

    /// <summary>The text shown by this layer. May contain line breaks.</summary>
    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value ?? string.Empty);
    }

    /// <summary>The typography used to render <see cref="Text"/>.</summary>
    /// <remarks>
    /// The instance never changes for the life of the layer, so callers may subscribe to it
    /// directly. Use <see cref="TextStyle.CopyFrom"/> to replace its values wholesale.
    /// </remarks>
    public TextStyle Style { get; }

    /// <summary>
    /// Where the text block's top-left corner sits in document coordinates.
    /// </summary>
    public ImagePoint Origin
    {
        get => _origin;
        set => SetProperty(ref _origin, value);
    }

    /// <inheritdoc />
    public override LayerNode CreateStructuralCopy(Guid newId, string newName)
    {
        TextLayer copy = new(newId, newName, Style)
        {
            Text = Text,
            Origin = Origin,
        };

        CopySettingsTo(copy);
        return copy;
    }
}
