using System.Globalization;
using System.Text;
using Avalonia.Data.Converters;

namespace XPXel.UI.Converters;

/// <summary>
/// Turns an enumeration value into something readable: <c>RowPerAnimation</c> becomes
/// "Row per animation", <c>PingPong</c> becomes "Ping-pong".
/// </summary>
/// <remarks>
/// <para>
/// Enumeration names are written for the compiler and read like code when they reach a
/// dropdown. The alternative — a parallel list of display strings per enumeration — is three
/// types and three bindings to say what splitting on capital letters says once, and it goes
/// stale the first time a member is added and the list is not.
/// </para>
/// <para>
/// <b>Deliberately not localised.</b> The whole application is English-only today; when that
/// changes, this is the seam where a resource lookup replaces the splitting, and every
/// dropdown that uses it becomes translatable at once.
/// </para>
/// </remarks>
public sealed class EnumDisplayConverter : IValueConverter
{
    /// <summary>The single shared instance, referenced from XAML by <c>x:Static</c>.</summary>
    public static EnumDisplayConverter Instance { get; } = new();

    /// <summary>
    /// Names whose split form reads badly, and what to show instead.
    /// </summary>
    /// <remarks>
    /// Only for cases where the general rule genuinely produces the wrong words. Anything that
    /// merely looks a little plain belongs in the enumeration's own naming, not here.
    /// </remarks>
    private static readonly Dictionary<string, string> Overrides = new(StringComparer.Ordinal)
    {
        ["PingPong"] = "Ping-pong",
    };

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return null;
        }

        string name = value.ToString() ?? string.Empty;

        if (Overrides.TryGetValue(name, out string? replacement))
        {
            return replacement;
        }

        return Humanise(name);
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Enumeration display names are one-way.");

    /// <summary>Splits a Pascal-cased name into a sentence.</summary>
    private static string Humanise(string name)
    {
        if (name.Length == 0)
        {
            return name;
        }

        StringBuilder builder = new(name.Length + 8);
        builder.Append(name[0]);

        for (int i = 1; i < name.Length; i++)
        {
            char current = name[i];

            // A capital starts a new word, unless it is part of a run of them — so "PNG" stays
            // "PNG" rather than becoming "P N G".
            if (char.IsUpper(current) && !char.IsUpper(name[i - 1]))
            {
                builder.Append(' ').Append(char.ToLowerInvariant(current));
            }
            else
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }
}
