using DarkFactory.Core;

namespace DarkFactory.Mcp.Tests;

// Covers docs/adr/0013-automatic-project-naming.md, exercised by
// projects.register once the front MCP surface lands in step 3.
public class ProjectNamingTests
{
    [Theory]
    [InlineData("darkfactory", "darkfactory")]
    [InlineData("Dark Factory", "dark-factory")]
    [InlineData("my_repo.git", "my-repo-git")]
    [InlineData("  leading and trailing  ", "leading-and-trailing")]
    [InlineData("!!!", "project")]
    public void Slugify_produces_a_stable_lowercase_slug(string input, string expected)
    {
        Assert.Equal(expected, ProjectNaming.Slugify(input));
    }

    [Fact]
    public void ResolveCollision_returns_the_slug_unchanged_when_free()
    {
        var result = ProjectNaming.ResolveCollision("darkfactory", new HashSet<string>());

        Assert.Equal("darkfactory", result);
    }

    [Fact]
    public void ResolveCollision_appends_the_smallest_free_numeric_suffix()
    {
        var taken = new HashSet<string> { "darkfactory", "darkfactory-2", "darkfactory-3" };

        var result = ProjectNaming.ResolveCollision("darkfactory", taken);

        Assert.Equal("darkfactory-4", result);
    }
}
