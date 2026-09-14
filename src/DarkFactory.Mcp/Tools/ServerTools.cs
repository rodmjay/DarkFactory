using System.ComponentModel;
using DarkFactory.Contracts;
using DarkFactory.Core;
using DarkFactory.Data;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace DarkFactory.Mcp.Tools;

/// <summary>
/// The handshake and registry surface (docs/adr/0018,
/// docs/conventions/describe.md). Thin on purpose: every rule that matters
/// — validation, idempotency, the manifest/live diff, refusing removal —
/// lives in <see cref="ServerRegistry"/>, where it is testable without a
/// transport.
/// </summary>
[McpServerToolType]
public static class ServerTools
{
    /// <summary>
    /// Until docs/adr/0025's token-derived identity exists, every call is
    /// attributed to one configured org. Deliberately a single named
    /// constant rather than a literal scattered across tools, so there is
    /// exactly one place to change when tokens arrive.
    /// </summary>
    public static string ResolveOrgId(IConfiguration configuration) =>
        configuration["DARKFACTORY_DEFAULT_ORG"] ?? "org_local";

    [McpServerTool(Name = "df.describe"),
     Description("The Dark Factory handshake: convention version(s), capabilities, domain, requires and effective config.")]
    public static DescribeResponse Describe(FactoryDescribe describe) => describe.Build();

    [McpServerTool(Name = "df.servers.register"),
     Description("Register an MCP server: calls df.describe, validates it against the published schema, stores manifest vs live, and runs a conformance check.")]
    public static async Task<RegisterServerResult> Register(
        ServerRegistry registry,
        IConfiguration configuration,
        [Description("The server's MCP endpoint, e.g. http://localhost:8931/mcp")] string url,
        [Description("Optional static manifest (a describe-shaped document) to compare the live describe against.")] string? manifest = null,
        [Description("Optional project to scope this server to.")] string? project_id = null,
        [Description("built_in | premium | community. Defaults to community.")] string? tier = null,
        CancellationToken cancellationToken = default)
    {
        var registration = await Errors.Surfacing(() => registry.RegisterAsync(
            ResolveOrgId(configuration), url, manifest, project_id, ParseTier(tier), cancellationToken));

        return RegisterServerResult.From(registration);
    }

    [McpServerTool(Name = "df.servers.list"),
     Description("List registered servers for the current org, with their status and any manifest/live disagreement.")]
    public static async Task<IReadOnlyList<ServerSummary>> List(
        ServerRegistry registry,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var servers = await registry.ListAsync(ResolveOrgId(configuration), cancellationToken);
        return servers.Select(ServerSummary.From).ToList();
    }

    [McpServerTool(Name = "df.servers.check"),
     Description("Check a server now rather than at its next scheduled check: a handshake, and full re-verification if it was down or has changed. Returns its status afterwards.")]
    public static async Task<ServerCheckResult> Check(
        ServerHealthService health,
        ServerRegistry registry,
        IConfiguration configuration,
        [Description("The server id returned by df.servers.list.")] string server_id,
        CancellationToken cancellationToken = default)
    {
        var orgId = ResolveOrgId(configuration);
        var check = await Errors.Surfacing(() => health.CheckNowAsync(orgId, server_id, cancellationToken));
        var server = (await registry.ListAsync(orgId, cancellationToken)).Single(s => s.Id == server_id);
        return new ServerCheckResult(check.Outcome.ToString(), ServerSummary.From(server));
    }

    [McpServerTool(Name = "df.standards.ingest"),
     Description("Copy a standards server's documents into the factory's index now (docs/adr/0023). Happens on its own at registration and whenever the server is re-verified or heals; this is for asking again.")]
    public static async Task<StandardsIngest> IngestStandards(
        StandardsIngestService standards,
        [Description("The standards server's id, from df.servers.list.")] string server_id,
        CancellationToken cancellationToken = default) =>
        await Errors.Surfacing(() => standards.IngestAsync(server_id, cancellationToken));

    [McpServerTool(Name = "df.servers.remove"),
     Description("Retire a registered server. Refused while active runs still reference it.")]
    public static async Task<ServerSummary> Remove(
        ServerRegistry registry,
        IConfiguration configuration,
        [Description("The server id returned by df.servers.list.")] string server_id,
        CancellationToken cancellationToken = default)
    {
        var server = await Errors.Surfacing(() => registry.RemoveAsync(ResolveOrgId(configuration), server_id, cancellationToken));
        return ServerSummary.From(server);
    }

    private static ServerTier ParseTier(string? tier) => tier?.ToLowerInvariant() switch
    {
        null or "" or "community" => ServerTier.Community,
        "premium" => ServerTier.Premium,
        "built_in" or "builtin" => ServerTier.BuiltIn,
        _ => throw new ArgumentException($"Unknown tier '{tier}'. Expected built_in, premium or community."),
    };
}

public sealed record ServerSummary(
    string Id,
    string Url,
    string Name,
    string Domain,
    string Tier,
    string ConventionVersion,
    string Status,
    string? ManifestDiff,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? LastConformanceAt,
    // docs/adr/0038: how the connection stands now, not at registration.
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? UnreachableSince,
    int ConsecutiveFailures,
    string? LastError,
    DateTimeOffset? NextCheckAt,
    DateTimeOffset? HealedAt,
    int HealCount)
{
    public static ServerSummary From(Server s) => new(
        s.Id, s.Url, s.Name, s.Domain, s.Tier.ToString(), s.ConventionVersion,
        s.Status.ToString(), s.ManifestDiffJson, s.RegisteredAt, s.LastConformanceAt,
        s.LastCheckedAt, s.LastSeenAt, s.UnreachableSince, s.ConsecutiveFailures, s.LastError,
        s.NextCheckAt, s.HealedAt, s.HealCount);
}

public sealed record ServerCheckResult(string Outcome, ServerSummary Server);

public sealed record CapabilityResult(string Capability, string Status, string? Detail, long DurationMs);

public sealed record RegisterServerResult(
    ServerSummary Server,
    string ManifestDiffSummary,
    IReadOnlyList<CapabilityResult> Conformance)
{
    public static RegisterServerResult From(ServerRegistration registration) => new(
        ServerSummary.From(registration.Server),
        registration.Diff.Summarize(),
        registration.Conformance
            .Select(c => new CapabilityResult(c.Capability, c.Status.ToString(), c.Detail, c.DurationMs))
            .OrderBy(c => c.Capability, StringComparer.Ordinal)
            .ToList());
}
