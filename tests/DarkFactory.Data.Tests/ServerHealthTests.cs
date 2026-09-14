using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data.Tests;

/// <summary>
/// docs/adr/0038: a connection is watched, and heals. Under test is the
/// factory's judgement about a server — when a miss becomes an outage, how
/// often a dead server is asked, and that coming back means re-verified, not
/// merely answering — with the clock passed in rather than waited on.
/// </summary>
[Collection("SpecGraph")]
public sealed class ServerHealthTests(SpecGraphTestFixture fixture)
{
    private const string Org = "org_health";

    private static string NewUrl() => $"http://health-{Guid.NewGuid():n}.invalid/mcp";

    private async Task<(string ServerId, FakeServerProbe Probe)> RegisterHealthyAsync()
    {
        var probe = FakeServerProbe.HealthyWorkspace();
        await using var db = fixture.NewDb();
        var registration = await new ServerRegistry(db, probe).RegisterAsync(Org, NewUrl());
        Assert.Equal(ServerStatus.Conformant, registration.Server.Status);
        return (registration.Server.Id, probe);
    }

    private async Task<ServerCheck> CheckAsync(FakeServerProbe probe, string serverId, DateTimeOffset now)
    {
        await using var db = fixture.NewDb();
        var server = await db.Servers.SingleAsync(s => s.Id == serverId);
        return await new ServerHealthService(db, probe, new ServerRegistry(db, probe)).CheckAsync(server, now);
    }

    private async Task<Server> LoadAsync(string serverId)
    {
        await using var db = fixture.NewDb();
        return await db.Servers.AsNoTracking().SingleAsync(s => s.Id == serverId);
    }

    private static void AssertAbout(DateTimeOffset expected, DateTimeOffset? actual) =>
        Assert.InRange(Math.Abs((actual!.Value - expected).TotalMilliseconds), 0, 5);

    [Fact]
    public async Task OneMissIsNotAnOutage()
    {
        var (id, probe) = await RegisterHealthyAsync();
        probe.Fails("df.describe", "connection refused");
        var now = DateTimeOffset.UtcNow;

        var check = await CheckAsync(probe, id, now);

        Assert.Equal(ServerCheckOutcome.Missed, check.Outcome);
        var server = await LoadAsync(id);
        Assert.Equal(ServerStatus.Conformant, server.Status);
        Assert.Equal(1, server.ConsecutiveFailures);
        Assert.Equal("connection refused", server.LastError);
        AssertAbout(now, server.UnreachableSince);
        AssertAbout(now + ServerHealthService.Backoff(1), server.NextCheckAt);
    }

    [Fact]
    public async Task ASecondMissMakesItUnreachableAndADeadServerIsAskedLessOften()
    {
        var (id, probe) = await RegisterHealthyAsync();
        probe.Fails("df.describe", "connection refused");
        var first = DateTimeOffset.UtcNow;

        await CheckAsync(probe, id, first);
        var second = first + ServerHealthService.Backoff(1);
        var went = await CheckAsync(probe, id, second);

        Assert.Equal(ServerCheckOutcome.WentUnreachable, went.Outcome);
        var server = await LoadAsync(id);
        Assert.Equal(ServerStatus.Unreachable, server.Status);
        // Dated from when it stopped answering, not from when that was believed.
        AssertAbout(first, server.UnreachableSince);
        AssertAbout(second + ServerHealthService.Backoff(2), server.NextCheckAt);

        var third = second + ServerHealthService.Backoff(2);
        Assert.Equal(ServerCheckOutcome.StillUnreachable, (await CheckAsync(probe, id, third)).Outcome);
        AssertAbout(third + ServerHealthService.Backoff(3), (await LoadAsync(id)).NextCheckAt);
        Assert.True(ServerHealthService.Backoff(3) > ServerHealthService.Backoff(2));
        Assert.Equal(ServerHealthService.MaxBackoff, ServerHealthService.Backoff(20));
    }

    [Fact]
    public async Task AnsweringAgainIsNotEnoughItIsReverifiedAndThenHealed()
    {
        var (id, probe) = await RegisterHealthyAsync();
        probe.Fails("df.describe", "connection refused");
        var now = DateTimeOffset.UtcNow;
        await CheckAsync(probe, id, now);
        await CheckAsync(probe, id, now.AddSeconds(15));
        Assert.Equal(ServerStatus.Unreachable, (await LoadAsync(id)).Status);

        var writesBefore = probe.CallsTo("df.files.write_many").Count;
        probe.Answers("df.describe", FakeServerProbe.Describe());

        var back = now.AddSeconds(45);
        var check = await CheckAsync(probe, id, back);

        Assert.Equal(ServerCheckOutcome.Healed, check.Outcome);
        // Conformance ran again: the heal is a re-verification, not a ping.
        Assert.Equal(writesBefore + 1, probe.CallsTo("df.files.write_many").Count);

        var server = await LoadAsync(id);
        Assert.Equal(ServerStatus.Conformant, server.Status);
        Assert.Equal(1, server.HealCount);
        AssertAbout(back, server.HealedAt);
        Assert.Null(server.UnreachableSince);
        Assert.Null(server.LastError);
        Assert.Equal(0, server.ConsecutiveFailures);

        await using var db = fixture.NewDb();
        Assert.Equal(2, await db.ConformanceResults
            .Where(c => c.ServerId == id).Select(c => c.ConformanceRunId).Distinct().CountAsync());
    }

    [Fact]
    public async Task AHealthyCheckIsOnlyAHandshake()
    {
        var (id, probe) = await RegisterHealthyAsync();
        var callsBefore = probe.Calls.Count;

        var check = await CheckAsync(probe, id, DateTimeOffset.UtcNow.AddSeconds(31));

        Assert.Equal(ServerCheckOutcome.Healthy, check.Outcome);
        var calls = probe.Calls.Skip(callsBefore).ToList();
        Assert.Equal("df.describe", Assert.Single(calls).Tool);
        Assert.Equal(ServerHealthService.PingDeadline, calls[0].Deadline);
    }

    [Fact]
    public async Task AServerThatChangedWhatItSaysIsReverifiedWithoutBeingCalledDegraded()
    {
        var (id, probe) = await RegisterHealthyAsync();
        probe.Answers("df.describe", FakeServerProbe.Describe(
            capabilities: [.. FakeServerProbe.DefaultCapabilities, "df.files.search"]));

        var check = await CheckAsync(probe, id, DateTimeOffset.UtcNow.AddSeconds(31));

        Assert.Equal(ServerCheckOutcome.Reverified, check.Outcome);
        var server = await LoadAsync(id);
        Assert.Contains("df.files.search", server.LiveDescribeJson, StringComparison.Ordinal);
        // Registered without a manifest, so there is no claim to have drifted from.
        Assert.Null(server.ManifestDiffJson);
        Assert.Equal(ServerStatus.Conformant, server.Status);
    }

    [Fact]
    public async Task EveryConformancePassReusesOneScratchFileInTheCustomersTree()
    {
        var (id, probe) = await RegisterHealthyAsync();
        probe.Fails("df.describe", "down");
        var now = DateTimeOffset.UtcNow;
        await CheckAsync(probe, id, now);
        await CheckAsync(probe, id, now.AddSeconds(15));
        probe.Answers("df.describe", FakeServerProbe.Describe());
        await CheckAsync(probe, id, now.AddSeconds(45));

        var paths = probe.CallsTo("df.files.write_many")
            .Select(c => ((IEnumerable<Dictionary<string, object?>>)c.Arguments["files"]!).Single()["path"] as string)
            .ToList();

        Assert.Equal(2, paths.Count);
        Assert.All(paths, p => Assert.Equal($"{ConformanceChecker.ScratchDirectory}/probe.txt", p));
    }

    [Fact]
    public async Task RegistrationRidesOutAMomentaryMiss()
    {
        var probe = FakeServerProbe.HealthyWorkspace();
        var healthy = probe.Handlers["df.describe"];
        var calls = 0;
        probe.Handlers["df.describe"] = args =>
            ++calls == 1 ? ProbeResult.Failed("still starting", FailureClass.Retryable, 1) : healthy(args);

        await using var db = fixture.NewDb();
        var registration = await new ServerRegistry(db, probe).RegisterAsync(Org, NewUrl());

        Assert.Equal(ServerStatus.Conformant, registration.Server.Status);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task APermanentRefusalAtRegistrationIsNotAskedAgain()
    {
        var probe = FakeServerProbe.HealthyWorkspace().Fails("df.describe", "no such tool", FailureClass.Permanent);

        await using var db = fixture.NewDb();
        await Assert.ThrowsAsync<ServerRegistrationException>(
            () => new ServerRegistry(db, probe).RegisterAsync(Org, NewUrl()));
        Assert.Single(probe.CallsTo("df.describe"));
    }

    [Fact]
    public async Task OnlyServersWhoseCheckIsDueAreChecked()
    {
        var (id, probe) = await RegisterHealthyAsync();
        var registered = (await LoadAsync(id)).NextCheckAt!.Value;

        await using (var db = fixture.NewDb())
        {
            var early = await new ServerHealthService(db, probe, new ServerRegistry(db, probe))
                .CheckDueAsync(registered.AddSeconds(-1));
            Assert.DoesNotContain(early, c => c.ServerId == id);
        }

        await using (var db = fixture.NewDb())
        {
            var due = await new ServerHealthService(db, probe, new ServerRegistry(db, probe))
                .CheckDueAsync(registered.AddSeconds(1));
            Assert.Contains(due, c => c.ServerId == id && c.Outcome == ServerCheckOutcome.Healthy);
        }
    }
}
