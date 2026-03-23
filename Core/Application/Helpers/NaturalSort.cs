using System.Text.RegularExpressions;

namespace Translarr.Core.Application.Helpers;

public static partial class NaturalSort
{
    /// <summary>
    /// Returns a sort key that orders numeric segments naturally:
    /// "Season 2" before "Season 10".
    /// </summary>
    public static string Key(string value)
        => DigitPattern().Replace(value, m => m.Value.PadLeft(10, '0'));

    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitPattern();
}
