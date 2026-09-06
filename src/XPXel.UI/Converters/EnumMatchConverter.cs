using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace XPXel.UI.Converters;

/// <summary>
/// Binds a group of radio buttons to a single enumeration property: each button is checked
/// when the property equals that button's parameter, and checking one writes it back.
/// </summary>
/// <remarks>
/// <para>
/// The alternative is a boolean property per member on the view model — <c>IsAlignLeft</c>,
/// <c>IsAlignCentre</c>, <c>IsAlignRight</c> — each of which has to clear the others. That is
/// three properties and six notifications to express one value, and it goes wrong the first
/// time a member is added and one of the setters is forgotten.
/// </para>
/// <para>
/// <b>Unchecking writes nothing.</b> Radio buttons raise a change for the button being turned
/// off as well as the one being turned on, and in an unspecified order. Returning
/// <see cref="BindingOperations.DoNothing"/> for the "off" half is what stops the group from
/// briefly writing back the value that is being replaced.
/// </para>
/// </remarks>
public sealed class EnumMatchConverter : IValueConverter
{
    /// <summary>The single shared instance, referenced from XAML by <c>x:Static</c>.</summary>
    public static EnumMatchConverter Instance { get; } = new();

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value, parameter);

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter : BindingOperations.DoNothing;
}
