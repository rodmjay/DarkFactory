using System.Text.Json;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data.Tests;

/// <summary>
/// docs/conventions/standards.md: a standards server is asked the four
/// things the convention promises. Until this, all four were recorded
/// NotProbed and moonbeam-standards read "Conformant" having been asked only
/// its name (handoff 0004 left it as the obvious next step).
/// </summary>
[Collection("SpecGraph")]
public sealed class StandardsConformanceTests(SpecGraphTestFixture fixture)
{
    private static readonly string[] Capabilities =
        ["df.describe", "df.standards.list", "df.standards.get", "df.standards.query", "df.standards.index"];

    /// <summary>A standards server that behaves, with one part replaceable per test.</summary>
    private static FakeServerProbe StandardsServer(Action<FakeServerProbe>? breakIt = null)
    {
        var probe = new FakeServerProbe();
        probe.Answers("df.describe", FakeServerProbe.Describe("moonbeam-standards", Capabilities, "0.1.0", domain: "standards"));
        probe.Answers("df.standards.list", new
        {
            standards = new[]
            {
                new { id = "web-game-structure", title = "Web game structure", layers = new[] { "web" }, status = "accepted", updated = "2026-09-07" },
                new { id = "performance-budget", title = "Performance budget", layers = new[] { "all" }, status = "accepted", updated = "2026-09-01" },
            },
        });
        probe.Handlers["df.standards.get"] = args => ProbeResult.Success(JsonSerializer.Serialize(new
        {
            id = (string)args["id"]!,
            title = "Web game structure",
            text = "# Web game structure\n\nRule 11: a system that stops says why.",
            layers = new[] { "web" },
            status = "accepted",
            updated = "2026-09-07",
        }), 1);
        probe.Answers("df.standards.query", new
        {
            matches = new[] { new { id = "web-game-structure", title = "Web game structure", layers = new[] { "web" }, excerpt = "Rule 11" } },
            index_age_seconds = 41,
        });
        probe.Answers("df.standards.index", new { indexed = 2, took_ms = 3, at = "2026-09-14T00:00:00Z" });

        breakIt?.Invoke(probe);
        return probe;
    }

    private async Task<(ServerStatus Status, IReadOnlyList<ConformanceResult> Results, FakeServerProbe Probe)> RegisterAsync(FakeServerProbe probe)
    {
        await using var db = fixture.NewDb();
        var registration = await new ServerRegistry(db, probe)
            .RegisterAsync("org_standards", $"http://standards-{Guid.NewGuid():n}.invalid/mcp");
        return (registration.Server.Status, registration.Conformance, probe);
    }

    private static ConformanceResult Result(IReadOnlyList<ConformanceResult> results, string capability) =>
        results.Single(r => r.Capability == capability);

    [Fact]
    public async Task AStandardsServerThatKeepsItsPromisesIsConformantHavingBeenAskedAllFour()
    {
        var (status, results, probe) = await RegisterAsync(StandardsServer());

        Assert.Equal(ServerStatus.Conformant, status);
        foreach (var capability in Capabilities.Skip(1))
        {
            Assert.Equal(ConformanceStatus.Passed, Result(results, capability).Status);
        }

        // It fetched a document the server actually listed.
        Assert.Equal("web-game-structure", Assert.Single(probe.CallsTo("df.standards.get")).Arguments["id"]);
    }

    [Fact]
    public async Task AQueryWithoutItsIndexAgeFailsBecauseStaleRulesMustNeverLookCurrent()
    {
        var (status, results, _) = await RegisterAsync(StandardsServer(p =>
            p.Answers("df.standards.query", new { matches = Array.Empty<object>() })));

        Assert.Equal(ServerStatus.Degraded, status);
        var query = Result(results, "df.standards.query");
        Assert.Equal(ConformanceStatus.Failed, query.Status);
        Assert.Contains("index_age_seconds", query.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AListWithoutUpdatedFailsBecauseIngestCouldNotTellWhatChanged()
    {
        var (status, results, _) = await RegisterAsync(StandardsServer(p => p.Answers("df.standards.list", new
        {
            standards = new[] { new { id = "web-game-structure", title = "Web game structure" } },
        })));

        Assert.Equal(ServerStatus.Degraded, status);
        Assert.Equal(ConformanceStatus.Failed, Result(results, "df.standards.list").Status);
        // get is not attempted against a list that did not hold up.
        Assert.Equal(ConformanceStatus.Failed, Result(results, "df.standards.get").Status);
    }

    [Fact]
    public async Task AGetWithoutTextAndAnIndexWithoutACountEachFail()
    {
        var (_, results, _) = await RegisterAsync(StandardsServer(p =>
        {
            p.Answers("df.standards.get", new { id = "web-game-structure", title = "Web game structure" });
            p.Answers("df.standards.index", new { took_ms = 3 });
        }));

        Assert.Equal(ConformanceStatus.Failed, Result(results, "df.standards.get").Status);
        Assert.Equal(ConformanceStatus.Failed, Result(results, "df.standards.index").Status);
    }
}
