using System.Text.Json;
using DarkFactory.Contracts;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data;

public enum ServerCheckOutcome
{
    /// <summary>Answered, same as before. A handshake and nothing else.</summary>
    Healthy,

    /// <summary>Answered, but says something new about itself; re-verified.</summary>
    Reverified,

    /// <summary>Was down, answers again, and has been re-verified.</summary>
    Healed,

    /// <summary>Did not answer, once. Not yet an outage.</summary>
    Missed,

    /// <summary>Did not answer again, and is now unreachable.</summary>
    WentUnreachable,

    /// <summary>Still not answering; the next check is further off.</summary>
    StillUnreachable,

    /// <summary>Answered with a handshake that no longer validates.</summary>
    BrokenHandshake,
}

public sealed record ServerCheck(
    string ServerId,
    string Name,
    ServerCheckOutcome Outcome,
    ServerStatus Status,
    string? Error,
    DateTimeOffset? NextCheckAt);

/// <summary>
/// Keeps each registered server's status true now, not true at registration
/// (docs/adr/0038).
///
/// A check is one <c>df.describe</c> with a short deadline — the cheapest
/// call every server must answer. Two properties it exists to guarantee:
/// <list type="bullet">
/// <item>One missed answer is not an outage. A server is declared
/// unreachable on its second consecutive miss, and checked less often the
/// longer it stays down, so a restart is ridden out and a dead server is not
/// hammered.</item>
/// <item>Coming back is not the same as being trusted again. A server that
/// answers after an outage is re-verified exactly as at registration —
/// schema, manifest diff, conformance — before its status says it works.
/// That is the heal: the factory restores the connection it relies on, and
/// nobody has to re-register anything.</item>
/// </list>
/// </summary>
public sealed class ServerHealthService(
    DarkFactoryDbContext db,
    IServerProbe probe,
    ServerRegistry registry,
    StandardsIngestService? standards = null)
{
    public static readonly TimeSpan HealthyInterval = TimeSpan.FromSeconds(30);

    /// <summary>Short: a server that takes longer than this to say who it is is not answering.</summary>
    public static readonly TimeSpan PingDeadline = TimeSpan.FromSeconds(5);

    public const int MissesBeforeUnreachable = 2;

    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    /// <summary>15s, 30s, 60s … capped at five minutes.</summary>
    public static TimeSpan Backoff(int consecutiveFailures)
    {
        var seconds = 15 * Math.Pow(2, Math.Max(0, consecutiveFailures - 1));
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxBackoff.TotalSeconds));
    }

    /// <summary>Every live server whose next check has come due — the monitor's one call per tick.</summary>
    public async Task<IReadOnlyList<ServerCheck>> CheckDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var due = await db.Servers
            .Where(s => s.RemovedAt == null && (s.NextCheckAt == null || s.NextCheckAt <= now))
            .OrderBy(s => s.NextCheckAt)
            .ToListAsync(cancellationToken);

        var checks = new List<ServerCheck>(due.Count);
        foreach (var server in due)
        {
            checks.Add(await CheckAsync(server, now, cancellationToken));
        }
        return checks;
    }

    /// <summary>"Reconnect now" — the same check, on demand, whatever the schedule says.</summary>
    public async Task<ServerCheck> CheckNowAsync(string orgId, string serverId, CancellationToken cancellationToken = default)
    {
        var server = await db.Servers.SingleOrDefaultAsync(
            s => s.OrgId == orgId && s.Id == serverId && s.RemovedAt == null, cancellationToken)
            ?? throw new ServerRegistrationException($"No server '{serverId}' registered for org '{orgId}'.");

        return await CheckAsync(server, DateTimeOffset.UtcNow, cancellationToken);
    }

    public async Task<ServerCheck> CheckAsync(Server server, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var check = await CheckConnectionAsync(server, now, cancellationToken);
        await IngestStandardsIfDueAsync(server, check.Outcome, cancellationToken);
        return check;
    }

    /// <summary>
    /// A standards server is ingested when it has never been, and again
    /// whenever it was just re-verified or healed — the moments its content
    /// may have changed. So registering one is enough; nobody has to ask.
    /// A failed ingest is left for the next check to retry: the previous
    /// ingest keeps serving, and a health check must not fail because a
    /// document did.
    /// </summary>
    private async Task IngestStandardsIfDueAsync(Server server, ServerCheckOutcome outcome, CancellationToken cancellationToken)
    {
        if (standards is null
            || !string.Equals(server.Domain, StandardsIngestService.Domain, StringComparison.Ordinal)
            || server.Status is not (ServerStatus.Conformant or ServerStatus.Degraded))
        {
            return;
        }

        var due = outcome is ServerCheckOutcome.Reverified or ServerCheckOutcome.Healed
            || !await db.StandardsIndex.AnyAsync(s => s.ServerId == server.Id, cancellationToken);
        if (!due)
        {
            return;
        }

        try
        {
            await standards.IngestAsync(server.Id, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Retried on the next check; see the summary.
        }
    }

    private async Task<ServerCheck> CheckConnectionAsync(Server server, DateTimeOffset now, CancellationToken cancellationToken)
    {
        server.LastCheckedAt = now;

        var ping = await probe.CallAsync(
            server.Url, ServerRegistry.DescribeTool, new Dictionary<string, object?>(), PingDeadline, cancellationToken);

        if (!ping.Ok)
        {
            return await MissedAsync(server, now, ping.Error, cancellationToken);
        }

        var validation = DescribeSchema.TryParse(ping.Json!, out var live);
        if (!validation.IsValid)
        {
            // It answered, so waiting longer will not fix it — but a
            // redeploy might, so it is still checked on the normal cadence,
            // and the next valid answer is treated as a return from an
            // outage.
            server.ConsecutiveFailures = 0;
            server.UnreachableSince = null;
            server.LastSeenAt = now;
            server.Status = ServerStatus.Failed;
            server.LastError = $"{ServerRegistry.DescribeTool} no longer validates: {validation.Summarize()}";
            server.NextCheckAt = now + HealthyInterval;
            await db.SaveChangesAsync(cancellationToken);
            return Result(server, ServerCheckOutcome.BrokenHandshake);
        }

        // Down means the factory could not rely on it: unreachable, or a
        // handshake that stopped validating. A server that answers but
        // failed conformance at registration is not "down" — re-running the
        // same checks every thirty seconds would only repeat the verdict.
        var wasDown = server.Status == ServerStatus.Unreachable
            || (server.Status == ServerStatus.Failed && server.LastError is not null);
        var changed = !string.Equals(JsonSerializer.Serialize(live), server.LiveDescribeJson, StringComparison.Ordinal);

        server.ConsecutiveFailures = 0;
        server.UnreachableSince = null;
        server.LastError = null;
        server.LastSeenAt = now;
        server.NextCheckAt = now + HealthyInterval;

        if (!wasDown && !changed && server.LastConformanceAt is not null)
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result(server, ServerCheckOutcome.Healthy);
        }

        await registry.ReverifyAsync(server, live!, cancellationToken);

        if (!wasDown)
        {
            return Result(server, ServerCheckOutcome.Reverified);
        }

        server.HealedAt = now;
        server.HealCount++;
        await db.SaveChangesAsync(cancellationToken);
        return Result(server, ServerCheckOutcome.Healed);
    }

    private async Task<ServerCheck> MissedAsync(
        Server server, DateTimeOffset now, string? error, CancellationToken cancellationToken)
    {
        server.ConsecutiveFailures++;
        server.LastError = error;
        server.UnreachableSince ??= now;
        server.NextCheckAt = now + Backoff(server.ConsecutiveFailures);

        ServerCheckOutcome outcome;
        if (server.Status == ServerStatus.Unreachable)
        {
            outcome = ServerCheckOutcome.StillUnreachable;
        }
        else if (server.ConsecutiveFailures >= MissesBeforeUnreachable)
        {
            server.Status = ServerStatus.Unreachable;
            outcome = ServerCheckOutcome.WentUnreachable;
        }
        else
        {
            outcome = ServerCheckOutcome.Missed;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result(server, outcome);
    }

    private static ServerCheck Result(Server server, ServerCheckOutcome outcome) =>
        new(server.Id, server.Name, outcome, server.Status, server.LastError, server.NextCheckAt);
}
