using System.Text;
using System.Text.RegularExpressions;

namespace DarkFactory.Core;

/// <summary>Derives and de-duplicates project names. See docs/adr/0013-automatic-project-naming.md.</summary>
public static partial class ProjectNaming
{
    public static string Slugify(string input)
    {
        var lowered = input.Trim().ToLowerInvariant();
        var slug = NonAlphanumericRun().Replace(lowered, "-").Trim('-');
        return slug.Length == 0 ? "project" : slug;
    }

    /// <summary>Given a desired slug and the set of names already taken, returns the slug itself
    /// or the slug with the smallest "-N" suffix (starting at -2) that is not already taken.</summary>
    public static string ResolveCollision(string desiredSlug, IReadOnlySet<string> existingNames)
    {
        if (!existingNames.Contains(desiredSlug))
        {
            return desiredSlug;
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{desiredSlug}-{i}";
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumericRun();
}
