using System.Text.Json;
using DarkFactory.Contracts;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data;

/// <summary>Registration was refused. The message is written to be shown to whoever tried to register.</summary>
public sealed class ServerRegistrationException(string message) : Exception(message);

/// <summary>Removal was refused because runs still depend on the server (docs/conventions/describe.md).</summary>
public sealed class ServerInUseException(string serverId, IReadOnlyList<string> runIds)
    : Exception(
        $"Server '{serverId}' cannot be removed: {runIds.Count} active run(s) still reference it " +
        $"({string.Join(", ", runIds.Take(5))}{(runIds.Count > 5 ? ", …" : "")}). " +
        "Let them finish or cancel them first.")
{
    public IReadOnlyList<string> RunIds { get; } = runIds;
}

public sealed record ServerRegistration(
    Server Server,
    DescribeResponse Live,
    ManifestLiveDiff Diff,
    IReadOnlyList<ConformanceResult> Conformance);

/// <summary>
/// `df.servers.register` / `list` / `remove` (docs/adr/0018,
/// docs/conventions/describe.md). The rules live here rather than in the MCP
/// tool layer so they are testable without a transport in scope.
/// </summary>
public sealed class ServerRegistry(DarkFactoryDbContext db, IServerProbe probe)
{
    public const string DescribeTool = "df.describe";

    /// <summary>
    /// Short: this is an unregistered server being asked one trivial
    /// question, and an unresponsive one must not be able to hold a
    /// registration request open.
    /// </summary>
    public static readonly TimeSpan DescribeDeadline = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Describe → validate → upsert by (org_id, url) → diff manifest
    /// against live → conformance. Failing at either of the first two steps
    /// registers nothing at all: a server that cannot answer the handshake,
    /// or answers it wrongly, does not get a row.
    /// </summary>
    public async Task<ServerRegistration> RegisterAsync(
        string orgId,
        string url,
        string? manifestJson = null,
        string? projectId = null,
        ServerTier tier = ServerTier.Community,
        CancellationToken cancellationToken = default)
    {
        var live = await DescribeAsync(url, cancellationToken);

        // docs/adr/0038, as amended: a standards server is the org's, whoever
        // connected it; anything else connected to a project must not say it
        // serves a different one. Both settled before anything is stored.
        var isStandards = string.Equals(live.Domain, StandardsIngestService.Domain, StringComparison.Ordinal);
        if (isStandards)
        {
            projectId = null;
        }
        else if (projectId is not null)
        {
            await EnsureServesProjectAsync(url, live, projectId, cancellationToken);
        }

        // A supplied manifest is validated against the same published
        // schema as the live response. It is the same shape and it is going
        // to be diffed against a live response, so accepting a malformed
        // one would just defer the confusion to the diff.
        var manifest = live;
        if (manifestJson is not null)
        {
            var validation = DescribeSchema.TryParse(manifestJson, out var parsed);
            if (!validation.IsValid)
            {
                throw new ServerRegistrationException(
                    $"The manifest supplied for '{url}' is not a valid describe document: {validation.Summarize()}");
            }
            manifest = parsed!;
        }

        var liveJson = JsonSerializer.Serialize(live);
        var effectiveManifestJson = manifestJson ?? liveJson;
        var diff = ManifestLiveDiff.Compute(manifest, live);
        var now = DateTimeOffset.UtcNow;

        var server = await db.Servers.SingleOrDefaultAsync(
            s => s.OrgId == orgId && s.Url == url, cancellationToken);

        if (server is null)
        {
            server = new Server
            {
                Id = Ulid.NewUlid(),
                OrgId = orgId,
                ProjectId = projectId,
                Url = url,
                Name = live.Name,
                Tier = tier,
                Domain = live.Domain,
                ConventionVersion = live.ConventionVersion,
                ManifestJson = effectiveManifestJson,
                LiveDescribeJson = liveJson,
                ManifestDiffJson = null,
                Status = ServerStatus.Registered,
                RegisteredAt = now,
            };
            db.Servers.Add(server);
        }
        else
        {
            // Idempotent by (org_id, url): the same URL re-registered is
            // the same server saying something new about itself, never a
            // second server.
            server.Name = live.Name;
            server.Tier = tier;
            server.Domain = live.Domain;
            server.ConventionVersion = live.ConventionVersion;
            server.LiveDescribeJson = liveJson;
            server.Status = ServerStatus.Registered;

            // Re-registering revives a removed server rather than leaving
            // a row that df.servers.list hides but the unique index still
            // blocks a fresh insert on.
            server.RemovedAt = null;

            if (isStandards)
            {
                server.ProjectId = null;
            }
            else if (projectId is not null)
            {
                server.ProjectId = projectId;
            }

            // A manifest is only replaced when a new one is supplied.
            // Otherwise the manifest is the claim of record and the whole
            // point of the diff is that live may have drifted from it.
            if (manifestJson is not null)
            {
                server.ManifestJson = manifestJson;
            }
        }

        // It answered moments ago, so registration is also its first health
        // check (docs/adr/0038): the monitor's next look is a full interval
        // away rather than immediately repeating what was just established.
        server.LastCheckedAt = now;
        server.LastSeenAt = now;
        server.ConsecutiveFailures = 0;
        server.UnreachableSince = null;
        server.LastError = null;
        server.NextCheckAt = now + ServerHealthService.HealthyInterval;

        // Persisted before conformance so the results have a server row to
        // point at, and so a crash mid-check leaves a registered server
        // with no conformance rather than conformance with no server.
        await db.SaveChangesAsync(cancellationToken);

        var results = await ConformAsync(server, diff, live.Capabilities, cancellationToken);
        return new ServerRegistration(server, live, diff, results);
    }

    /// <summary>
    /// One workspace can hold many projects, each served at its own address
    /// (docs/conventions/corpus.md). A server that says which project it
    /// serves is only connected to that one; connecting drones to smashhit's
    /// address would import the wrong game's specifications with nothing to
    /// say so. A server that names no project is taken at its word.
    /// </summary>
    private async Task EnsureServesProjectAsync(
        string url, DescribeResponse live, string projectId, CancellationToken cancellationToken)
    {
        if (!live.EffectiveConfig.TryGetValue("project", out var value) || value.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var serves = value.GetString()!;
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            ?? throw new ServerRegistrationException($"There is no project '{projectId}' to connect '{url}' to.");

        if (!string.Equals(serves, project.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new ServerRegistrationException(
                $"'{url}' serves project '{serves}', not '{project.Name}'. " +
                $"Connect '{project.Name}' to its own address — for Moonbeam, …/projects/{project.Name}/mcp.");
        }
    }

    /// <summary>
    /// Re-establishes what registration established — the manifest/live
    /// diff and conformance — for a server already in the registry. This is
    /// what healing does once a server answers again (docs/adr/0038), and
    /// what happens when a server starts saying something new about itself.
    /// </summary>
    public async Task<IReadOnlyList<ConformanceResult>> ReverifyAsync(
        Server server, DescribeResponse live, CancellationToken cancellationToken = default)
    {
        var liveJson = JsonSerializer.Serialize(live);

        // Registered without a manifest, a server's "manifest" is only its
        // first describe, not a claim anyone made. Diffing a later describe
        // against it would mark every upgraded server degraded for having
        // changed, so such a manifest moves with the live answer. A manifest
        // somebody actually supplied stays the claim of record.
        DescribeResponse manifest;
        if (string.Equals(server.ManifestJson, server.LiveDescribeJson, StringComparison.Ordinal))
        {
            server.ManifestJson = liveJson;
            manifest = live;
        }
        else
        {
            manifest = DescribeSchema.TryParse(server.ManifestJson, out var parsed).IsValid ? parsed! : live;
        }

        server.Name = live.Name;
        server.Domain = live.Domain;
        server.ConventionVersion = live.ConventionVersion;
        server.LiveDescribeJson = liveJson;

        return await ConformAsync(server, ManifestLiveDiff.Compute(manifest, live), live.Capabilities, cancellationToken);
    }

    private async Task<IReadOnlyList<ConformanceResult>> ConformAsync(
        Server server, ManifestLiveDiff diff, IReadOnlyList<string> capabilities, CancellationToken cancellationToken)
    {
        var conformanceRunId = Ulid.NewUlid();
        var results = await new ConformanceChecker(probe).RunAsync(
            server.Id, server.Url, conformanceRunId, capabilities, cancellationToken);

        db.ConformanceResults.AddRange(results);

        server.ManifestDiffJson = diff.IsEmpty ? null : JsonSerializer.Serialize(diff);
        server.Status = DetermineStatus(diff, results);
        server.LastConformanceAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return results;
    }

    /// <summary>
    /// Degraded is the normal outcome for partial disagreement, not an
    /// error: a server that claims df.deploy.preview and does not have it
    /// stays usable for everything that does work (docs/adr/0018).
    /// </summary>
    private static ServerStatus DetermineStatus(ManifestLiveDiff diff, IReadOnlyList<ConformanceResult> results)
    {
        var probed = results.Where(r => r.Status != ConformanceStatus.NotProbed).ToList();

        if (probed.Count > 0 && probed.All(r => r.Status == ConformanceStatus.Failed))
        {
            return ServerStatus.Failed;
        }

        var anyFailed = probed.Any(r => r.Status == ConformanceStatus.Failed);
        return anyFailed || !diff.IsEmpty ? ServerStatus.Degraded : ServerStatus.Conformant;
    }

    /// <summary>
    /// How long registration waits before asking again after a miss that
    /// may not repeat. A server mid-restart is the common case when someone
    /// has just started it and registered straight away; failing that
    /// registration would make connecting feel fragile for no reason.
    /// </summary>
    public static readonly IReadOnlyList<TimeSpan> DescribeRetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];

    /// <summary>Calls df.describe and validates the answer. Throws with the schema violations when it doesn't hold up.</summary>
    private async Task<DescribeResponse> DescribeAsync(string url, CancellationToken cancellationToken)
    {
        var result = await probe.CallAsync(url, DescribeTool, new Dictionary<string, object?>(), DescribeDeadline, cancellationToken);

        // Only a retryable miss is asked again. A permanent one — no such
        // tool, a refused URL — is the same answer however often it is asked.
        foreach (var delay in DescribeRetryDelays)
        {
            if (result.Ok || result.Failure != FailureClass.Retryable)
            {
                break;
            }
            await Task.Delay(delay, cancellationToken);
            result = await probe.CallAsync(url, DescribeTool, new Dictionary<string, object?>(), DescribeDeadline, cancellationToken);
        }

        if (!result.Ok)
        {
            throw new ServerRegistrationException(
                $"'{url}' could not answer {DescribeTool}: {result.Error}. " +
                $"{DescribeTool} is mandatory (docs/adr/0018); a server that cannot answer it is not registered.");
        }

        var validation = DescribeSchema.TryParse(result.Json!, out var response);
        if (!validation.IsValid)
        {
            throw new ServerRegistrationException(
                $"'{url}' answered {DescribeTool} but the response is not valid against " +
                $"contracts/schemas/describe.schema.json: {validation.Summarize()}");
        }

        return response!;
    }

    public async Task<IReadOnlyList<Server>> ListAsync(string orgId, CancellationToken cancellationToken = default) =>
        await db.Servers.AsNoTracking()
            .Where(s => s.OrgId == orgId && s.RemovedAt == null)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ConformanceResult>> LatestConformanceAsync(
        string serverId, CancellationToken cancellationToken = default)
    {
        var latest = await db.ConformanceResults.AsNoTracking()
            .Where(c => c.ServerId == serverId)
            .OrderByDescending(c => c.CheckedAt)
            .Select(c => c.ConformanceRunId)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
        {
            return [];
        }

        return await db.ConformanceResults.AsNoTracking()
            .Where(c => c.ServerId == serverId && c.ConformanceRunId == latest)
            .OrderBy(c => c.Capability)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Retires a server, refusing while active runs still depend on it.
    /// Refused, never cascaded: removing a server out from under a run in
    /// flight would strand it mid-stage with no way to finish.
    /// </summary>
    public async Task<Server> RemoveAsync(string orgId, string serverId, CancellationToken cancellationToken = default)
    {
        var server = await db.Servers.SingleOrDefaultAsync(
            s => s.OrgId == orgId && s.Id == serverId, cancellationToken)
            ?? throw new ServerRegistrationException($"No server '{serverId}' registered for org '{orgId}'.");

        var blocking = await ActiveRunsUsingAsync(orgId, server.Url, cancellationToken);
        if (blocking.Count > 0)
        {
            throw new ServerInUseException(serverId, blocking);
        }

        server.RemovedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return server;
    }

    /// <summary>
    /// A run depends on a server either because its project is bound to
    /// that server's URL, or because it has already called it — the audit
    /// log (docs/adr/0012) is the evidence for the second, which catches a
    /// run using a server its project was not statically configured with.
    /// </summary>
    public async Task<IReadOnlyList<string>> ActiveRunsUsingAsync(
        string orgId, string serverUrl, CancellationToken cancellationToken = default)
    {
        var activeStatuses = new[] { RunStatus.Pending, RunStatus.Running, RunStatus.AwaitingApproval };

        var byProject = db.Runs.AsNoTracking()
            .Where(r => r.OrgId == orgId && activeStatuses.Contains(r.Status))
            .Join(db.Projects.AsNoTracking().Where(p => p.WorkspaceMcpUrl == serverUrl),
                r => r.ProjectId, p => p.Id, (r, _) => r.Id);

        var byAudit = db.Runs.AsNoTracking()
            .Where(r => r.OrgId == orgId && activeStatuses.Contains(r.Status))
            .Where(r => db.AuditEntries.AsNoTracking().Any(a => a.RunId == r.Id && a.TargetServer == serverUrl))
            .Select(r => r.Id);

        return await byProject.Union(byAudit).Distinct().ToListAsync(cancellationToken);
    }
}
