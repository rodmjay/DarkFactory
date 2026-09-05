namespace DarkFactory.Core;

/// <summary>
/// The single hardcoded default theme for v1. A real Theme convention server
/// does not exist yet (docs/conventions/theme.md); this is what
/// <c>plugins.list</c> resolves to and what stage agents use for standards
/// and the verify stage's test command.
/// </summary>
public static class DefaultTheme
{
    public const string Name = "default";

    public const string Standards =
        "Write clear, idiomatic code with no unnecessary abstraction. Prefer small, " +
        "focused changes. Match the surrounding project's existing conventions.";

    public const string TestCommand = "echo 'no test command configured for this project'";
}
