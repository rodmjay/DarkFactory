using System.Text.Json;
using DarkFactory.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace DarkFactory.Mcp.Tests;

/// <summary>
/// The factory answers its own handshake, and holds itself to the schema it
/// enforces on everyone else (docs/adr/0018, docs/conventions/describe.md).
/// </summary>
public sealed class FactoryDescribeTests
{
    private static FactoryDescribe Build(
        IEnumerable<string>? toolNames = null,
        Dictionary<string, string?>? configuration = null)
    {
        var options = new McpServerOptions();
        foreach (var name in toolNames ?? ["df.describe", "df.servers.register", "df.servers.list", "df.servers.remove"])
        {
            options.ToolCollection ??= [];
            options.ToolCollection.Add(McpServerTool.Create(
                () => "ok", new McpServerToolCreateOptions { Name = name }));
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configuration ?? [])
            .Build();

        return new FactoryDescribe(Options.Create(options), config);
    }

    [Fact]
    public void TheFactorysOwnDescribeValidatesAgainstThePublishedSchema()
    {
        var result = DescribeSchema.Validate(JsonSerializer.Serialize(Build().Build()));
        Assert.True(result.IsValid, result.Summarize());
    }

    [Fact]
    public void ItReportsDomainFactory()
    {
        Assert.Equal("factory", Build().Build().Domain);
    }

    [Fact]
    public void ItReportsEveryConventionVersionItSupportsNotJustOne()
    {
        var describe = Build().Build();

        // docs/adr/0020 promises an overlap window, which a factory
        // supporting exactly one version could not honour.
        Assert.NotNull(describe.ConventionVersions);
        Assert.True(describe.ConventionVersions!.Count >= 2);
        Assert.Contains(describe.ConventionVersion, describe.ConventionVersions);
    }

    [Fact]
    public void CapabilitiesComeFromTheLiveToolRegistry()
    {
        var describe = Build(["df.describe", "df.servers.list", "system.ping"]).Build();

        Assert.Equal(["df.describe", "df.servers.list"], describe.Capabilities);

        // system.ping is real and callable, but it is not a convention
        // tool, so it is the server's own business and not a capability
        // (ADR-0018).
        Assert.DoesNotContain("system.ping", describe.Capabilities);
    }

    [Fact]
    public void EffectiveConfigCarriesTheEnvironmentAndFoundryResourceAndNothingElse()
    {
        var describe = Build(configuration: new Dictionary<string, string?>
        {
            ["DARKFACTORY_ENVIRONMENT"] = "staging",
            ["FOUNDRY_RESOURCE_NAME"] = "df-foundry-eastus",
            // Present in configuration, and must not leak into a document
            // that gets stored in registries and rendered in dashboards.
            ["ConnectionStrings:DarkFactory"] = "Host=db;Password=hunter2",
            ["ANTHROPIC_API_KEY"] = "sk-should-never-appear",
        }).Build();

        Assert.Equal(["environment", "foundry_resource"], describe.EffectiveConfig.Keys.Order());
        Assert.Equal("staging", describe.EffectiveConfig["environment"].GetString());
        Assert.Equal("df-foundry-eastus", describe.EffectiveConfig["foundry_resource"].GetString());

        var serialized = JsonSerializer.Serialize(describe);
        Assert.DoesNotContain("hunter2", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-should-never-appear", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void ItDeclaresWhatItNeedsFromAWorkspaceServer()
    {
        var describe = Build().Build();

        // ADR-0018's requires[]: a project bound to a server missing any of
        // these is a detectable misconfiguration rather than a run that
        // dies at the implement stage.
        Assert.Contains("df.exec.run", describe.Requires);
        Assert.Contains("df.vcs.open_pr", describe.Requires);
        Assert.All(describe.Requires, r => Assert.StartsWith("df.", r, StringComparison.Ordinal));
    }
}
