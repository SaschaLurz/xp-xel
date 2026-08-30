using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace XPXel.Core.ComponentModel;

/// <summary>
/// Minimal <see cref="INotifyPropertyChanged"/> base class for <em>domain models</em> in
/// XPXel.Core.
/// </summary>
/// <remarks>
/// <para>
/// XPXel.UI uses CommunityToolkit.Mvvm's source-generated <c>ObservableObject</c> for view
/// models. Core cannot: it is forbidden from taking any dependency outside the BCL so the
/// domain stays reusable by a future CLI, exporter or server-side renderer. Twenty lines of
/// hand-written change notification is a cheap price for that isolation.
/// </para>
/// <para>
/// Only model state that the UI genuinely needs to observe should raise notifications.
/// Bulk pixel changes are <em>not</em> reported this way — they travel through
/// <c>PixelDocument.Changed</c> with a dirty rectangle instead, because raising one event
/// per pixel would be catastrophically slow.
/// </para>
/// </remarks>
public abstract class ObservableModel : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Assigns <paramref name="value"/> to <paramref name="field"/> and raises
    /// <see cref="PropertyChanged"/> if the value actually changed.
    /// </summary>
    /// <typeparam name="TValue">The property type.</typeparam>
    /// <param name="field">A reference to the backing field.</param>
    /// <param name="value">The new value.</param>
    /// <param name="propertyName">
    /// The property name; supplied automatically by the compiler.
    /// </param>
    /// <returns><see langword="true"/> if the value changed and an event was raised.</returns>
    protected bool SetProperty<TValue>(
        ref TValue field,
        TValue value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<TValue>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>Raises <see cref="PropertyChanged"/> for the given property.</summary>
    /// <param name="propertyName">
    /// The property name; supplied automatically by the compiler when called from within
    /// the property itself.
    /// </param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
