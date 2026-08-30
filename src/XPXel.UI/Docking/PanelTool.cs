using System.Text.Json.Serialization;
using Dock.Model.Mvvm.Controls;

namespace XPXel.UI.Docking;

/// <summary>
/// A docked panel — the toolbox, the layers list, the colour picker — as the docking system
/// sees it.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately a thin wrapper rather than a base class for the panel view models
/// themselves. <see cref="ViewModels.LayersViewModel"/> and its siblings know nothing about
/// docking, which keeps them testable and means the panels would survive a change of
/// docking library. Everything the docking system needs — a title, an identity, whether the
/// panel can be closed or floated — lives here instead.
/// </para>
/// <para>
/// <b>How a panel becomes a view.</b> The docking system renders a dockable by finding a
/// <c>DataTemplate</c> for its type. There is one template for <see cref="PanelTool"/>: a
/// <c>ContentControl</c> bound to <see cref="Content"/>. That indirection is what lets the
/// real templates be keyed on the panel view models, which is the natural MVVM mapping —
/// see <c>MainView.axaml</c>.
/// </para>
/// <para>
/// <b>Adding a panel</b> is therefore: write the view model, write the view, add a
/// <c>DataTemplate</c>, and create one of these in <see cref="XpDockFactory"/>. No existing
/// panel changes.
/// </para>
/// </remarks>
public sealed class PanelTool : Tool
{
    /// <summary>The panel's own view model, which is what actually gets templated.</summary>
    /// <remarks>
    /// <para>
    /// Settable rather than init-only because a layout restored from disk comes back with
    /// structure but no view models — and <see cref="XpDockFactory.RestorePanelContent"/>
    /// puts the real panels back by id.
    /// </para>
    /// <para>
    /// <b>Never serialised.</b> A panel view model is a live object graph that reaches the
    /// document, its layers and their parents, which contains reference cycles. Letting the
    /// layout serialiser walk into it throws, and because saving the layout is deliberately
    /// best-effort the failure is swallowed — the symptom is simply that the arrangement is
    /// never remembered, with nothing in the log to say why. Keep this attribute.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public object? Content { get; set; }
}
