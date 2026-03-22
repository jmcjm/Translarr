using System.Text.RegularExpressions;

namespace Translarr.Frontend.HavitWebApp.Helpers;

public static class SlugHelper
{
    public static string ToSlug(string name)
    {
        return name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("'", "")
            .Replace(".", "");
    }

    /// <summary>
    /// Resolves a slug back to the original name from a list of known names.
    /// Note: slug collisions are theoretically possible but extremely unlikely in real media folder names.
    /// </summary>
    public static string FromSlug(string slug, IEnumerable<string> knownNames)
    {
        return knownNames.FirstOrDefault(n => ToSlug(n) == slug) ?? slug;
    }

    /// <summary>
    /// Natural sort key: "Season 2" sorts before "Season 10".
    /// Pads numeric segments to 10 digits for lexicographic comparison.
    /// </summary>
    public static string NaturalSortKey(string value)
    {
        return Regex.Replace(value, @"\d+", m => m.Value.PadLeft(10, '0'));
    }
}
