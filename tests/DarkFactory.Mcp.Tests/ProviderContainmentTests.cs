using System.Runtime.CompilerServices;

namespace DarkFactory.Mcp.Tests;

/// <summary>
/// docs/adr/0027's central claim is that nothing above
/// <c>IModelGateway</c> knows which provider serves a call. That claim is
/// only worth anything if something enforces it — otherwise the first
/// person in a hurry adds a <c>using DarkFactory.Anthropic</c> to a service
/// and the abstraction quietly stops being one.
///
/// This reads the source rather than the assemblies deliberately: a
/// reference that is present but unused is exactly the state this is
/// meant to catch, and it would be invisible to a type-graph check.
/// </summary>
public sealed class ProviderContainmentTests
{
    /// <summary>The composition helper both hosts call, and the only place allowed to know.</summary>
    private const string CompositionProject = "DarkFactory.Hosting";

    private static readonly string[] ProviderNamespaces = ["DarkFactory.Foundry", "DarkFactory.Anthropic"];

    [Fact]
    public void OnlyTheCompositionHelperReferencesAProviderProject()
    {
        var offenders = Directory
            .EnumerateFiles(SourceRoot(), "*.csproj", SearchOption.AllDirectories)
            .Where(path => ProviderNamespaces.Any(File.ReadAllText(path).Contains))
            .Select(path => Path.GetFileNameWithoutExtension(path))
            // A provider project naming itself is not a reference.
            .Where(name => !ProviderNamespaces.Contains(name))
            .Where(name => name != CompositionProject)
            .Order()
            .ToList();

        Assert.True(offenders.Count == 0,
            $"These projects reference a model provider directly: {string.Join(", ", offenders)}. " +
            $"Only {CompositionProject} may (docs/adr/0027) — everything else goes through IModelGateway.");
    }

    [Fact]
    public void OnlyTheCompositionHelperNamesAProviderInCode()
    {
        var offenders = Directory
            .EnumerateFiles(SourceRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(path => ProviderNamespaces.Any(ns => File.ReadAllText(path).Contains($"using {ns};")))
            .Where(path => !ProviderNamespaces.Any(ns =>
                path.Contains($"{Path.DirectorySeparatorChar}{ns}{Path.DirectorySeparatorChar}")))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}{CompositionProject}{Path.DirectorySeparatorChar}"))
            .Select(path => Path.GetRelativePath(SourceRoot(), path))
            .Order()
            .ToList();

        Assert.True(offenders.Count == 0,
            $"These files name a model provider: {string.Join(", ", offenders)}. " +
            "Callers name a role and let the gateway resolve it (docs/adr/0027).");
    }

    private static string SourceRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src"));
}
