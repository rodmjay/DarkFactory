using System.Text.Json;
using DarkFactory.Contracts;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace DarkFactory.Mcp;

/// <summary>
/// The factory answering its own handshake (docs/adr/0018). Dark Factory
/// holds itself to the convention it publishes: same schema, same
/// validation, no exemption.
/// </summary>
public sealed class FactoryDescribe(IOptions<McpServerOptions> serverOptions, IConfiguration configuration)
{
    /// <summary>The version the factory prefers to speak.</summary>
    public const string PreferredConventionVersion = "0.2.0";

    /// <summary>
    /// Every version the factory can speak. docs/adr/0020 requires at least
    /// two concurrently, so this is plural by contract, not by accident —
    /// a factory that supported exactly one version could not honour the
    /// overlap window it promises server authors.
    /// </summary>
    public static readonly string[] SupportedConventionVersions = ["0.1.0", "0.2.0"];

    public DescribeResponse Build() => new()
    {
        Name = "dark-factory",
        ConventionVersion = PreferredConventionVersion,
        ConventionVersions = SupportedConventionVersions,
        Domain = "factory",
        Capabilities = Capabilities(),
        Requires = Requires,
        EffectiveConfig = EffectiveConfig(),
    };

    /// <summary>
    /// Read out of the live MCP tool registry rather than hand-maintained.
    /// A hand-written list is a list that goes stale, and the factory
    /// marking *other* servers degraded for exactly that drift while
    /// misreporting its own would be indefensible.
    /// </summary>
    private IReadOnlyList<string> Capabilities() =>
        (serverOptions.Value.ToolCollection ?? [])
        .Select(tool => tool.ProtocolTool.Name)
        .Where(name => name.StartsWith("df.", StringComparison.Ordinal))
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// What the factory needs from a workspace server to run a pipeline
    /// (docs/conventions/workspace.md). Declared so a project bound to a
    /// server missing any of them is a detectable misconfiguration rather
    /// than a run that fails at the implement stage.
    /// </summary>
    private static readonly string[] Requires =
    [
        "df.files.list",
        "df.files.read_many",
        "df.files.write_many",
        "df.exec.run",
        "df.vcs.branch",
        "df.vcs.commit",
        "df.vcs.open_pr",
    ];

    /// <summary>
    /// Non-secret values only: the environment name and the Foundry
    /// resource name. This is stored in other people's registries and
    /// rendered in dashboards, and "effective config" is exactly the field
    /// where a connection string would look like it belonged.
    /// </summary>
    private IReadOnlyDictionary<string, JsonElement> EffectiveConfig()
    {
        var values = new Dictionary<string, string>
        {
            ["environment"] = configuration["DARKFACTORY_ENVIRONMENT"]
                ?? configuration["ASPNETCORE_ENVIRONMENT"]
                ?? "local",
            ["foundry_resource"] = configuration["FOUNDRY_RESOURCE_NAME"] ?? "(unset)",
        };

        return values.ToDictionary(
            kv => kv.Key,
            kv => JsonSerializer.SerializeToElement(kv.Value));
    }
}
