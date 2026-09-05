using System.Runtime.CompilerServices;
using System.Text.Json;
using DarkFactory.Contracts;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DarkFactory.Data.Tests;

/// <summary>
/// docs/adr/0016 (as amended): an amendment records who, when, and why —
/// per change, not per amendment.
///
/// The "why" was being lost in two different ways. Creates and revises were
/// buried: the rationale was serialized inside each element's ContentJson,
/// so it reached storage but nothing read it. Retires and both edge kinds
/// were dropped outright, because the internal records had no field for it.
/// Both showed up identically at the surface — <c>rationale: null</c> on an
/// approval card, with no way to tell "nobody said why" from "we lost it".
/// </summary>
[Collection("SpecGraph")]
public sealed class RationaleProvenanceTests(SpecGraphTestFixture fixture)
{
    private const string CreateWhy = "Operators need to tell process liveness from database reachability.";
    private const string ReviseWhy = "The original wording did not say what 'healthy' meant.";
    private const string RetireWhy = "Superseded by the endpoint rule; keeping both invites drift.";
    private const string EdgeWhy = "The detailed check is meaningless without the connectivity rule.";

    // ---- the round trip the fix exists for --------------------------------

    [Fact]
    public async Task ARationaleOnTheWireReachesTheRenderedAmendmentUnchanged()
    {
        var (projectId, orgId, conversationId) = await fixture.SeedProjectAsync();

        // A node to revise, retire and point an edge at, so every element
        // kind in the diff carries a reason.
        await Graph.ApplyAsync(fixture, projectId, orgId, conversationId, Graph.Diff(creates:
        [
            Graph.Create("An account is delinquent after 30 days unpaid."),
            Graph.Create("Invoices are immutable once issued."),
        ]));

        List<string> existing;
        await using (var db = fixture.NewDb())
        {
            existing = await db.SpecNodes.Where(n => n.ProjectId == projectId)
                .OrderBy(n => n.SpecId).Select(n => n.SpecId).ToListAsync();
        }

        var document = new SpecDiffDocument
        {
            Creates =
            [
                new SpecDiffCreate
                {
                    Kind = "interface", Layer = "api",
                    Text = "GET /health/detailed reports database reachability.",
                    Rationale = CreateWhy,
                },
            ],
            Revises =
            [
                new SpecDiffRevise
                {
                    SpecId = existing[0], Text = "An account is delinquent after 45 days unpaid.",
                    Rationale = ReviseWhy,
                },
            ],
            Retires = [new SpecDiffRetire { SpecId = existing[1], Rationale = RetireWhy }],
            EdgeAdds =
            [
                new SpecDiffEdgeAddDocument
                {
                    FromSpecId = "new:0", ToSpecId = existing[0], Kind = "depends_on", Rationale = EdgeWhy,
                },
            ],
            EdgeRetires = [],
        };

        Amendment amendment;
        await using (var db = fixture.NewDb())
        {
            amendment = await new SpecGraphService(db).ProposeAsync(
                projectId, orgId, conversationId, turnId: null, proposedBy: "architect",
                SpecDiffTranslator.ToSpecDiff(document));
        }

        // It has to survive storage, not just the mapping in memory.
        await using var verify = fixture.NewDb();
        var stored = await verify.Amendments.AsNoTracking().SingleAsync(a => a.Id == amendment.Id);
        var rendered = Mcp.Tools.AmendmentSummary.From(stored).Diff!;

        Assert.Equal(CreateWhy, rendered.Creates[0].Rationale);
        Assert.Equal(ReviseWhy, rendered.Revises[0].Rationale);
        Assert.Equal(RetireWhy, rendered.Retires[0].Rationale);
        Assert.Equal(EdgeWhy, rendered.EdgeAdds[0].Rationale);
    }

    [Fact]
    public async Task ANullRationaleStaysNullRatherThanBecomingAnEmptyString()
    {
        // "Nobody said why" and "we lost it" must not become the same value
        // on the way out — the whole point of the fix is being able to tell.
        var (projectId, orgId, conversationId) = await fixture.SeedProjectAsync();

        var document = new SpecDiffDocument
        {
            Creates = [new SpecDiffCreate { Kind = "rule", Layer = "domain", Text = "No reason given." }],
            Revises = [], Retires = [], EdgeAdds = [], EdgeRetires = [],
        };

        Amendment amendment;
        await using (var db = fixture.NewDb())
        {
            amendment = await new SpecGraphService(db).ProposeAsync(
                projectId, orgId, conversationId, null, "architect", SpecDiffTranslator.ToSpecDiff(document));
        }

        await using var verify = fixture.NewDb();
        var stored = await verify.Amendments.AsNoTracking().SingleAsync(a => a.Id == amendment.Id);
        Assert.Null(Mcp.Tools.AmendmentSummary.From(stored).Diff!.Creates[0].Rationale);
    }

    // ---- each revision keeps its own reason -------------------------------

    [Fact]
    public async Task EachRevisionInANodesHistoryCarriesTheReasonItWasMadeFor()
    {
        var (projectId, orgId, conversationId) = await fixture.SeedProjectAsync();

        await Graph.ApplyAsync(fixture, projectId, orgId, conversationId,
            Graph.Diff(creates: [Graph.Create("Trials last 14 days.")]));

        string specId;
        await using (var db = fixture.NewDb())
        {
            specId = await db.SpecNodes.Where(n => n.ProjectId == projectId).Select(n => n.SpecId).SingleAsync();
        }

        await ProposeAndApproveAsync(projectId, orgId, conversationId, new SpecDiffDocument
        {
            Creates = [],
            Revises = [new SpecDiffRevise { SpecId = specId, Text = "Trials last 30 days.", Rationale = "Sales asked for longer." }],
            Retires = [], EdgeAdds = [], EdgeRetires = [],
        });

        await ProposeAndApproveAsync(projectId, orgId, conversationId, new SpecDiffDocument
        {
            Creates = [],
            Revises = [new SpecDiffRevise { SpecId = specId, Text = "Trials last 21 days.", Rationale = "30 was too long to convert." }],
            Retires = [], EdgeAdds = [], EdgeRetires = [],
        });

        await using var db2 = fixture.NewDb();
        var history = await new SpecGraphService(db2).RevisionHistoryAsync(specId);

        // Three revisions, three different reasons. The useful one is
        // almost never the latest, which is why this is a history.
        Assert.Equal(3, history.Count);
        Assert.Null(history[0].Rationale);
        Assert.Equal("Sales asked for longer.", history[1].Rationale);
        Assert.Equal("30 was too long to convert.", history[2].Rationale);

        // And who, alongside why.
        Assert.All(history, r => Assert.False(string.IsNullOrEmpty(r.ActorId)));
        Assert.Equal("Trials last 21 days.", history[^1].Text);
    }

    // ---- the backfill, against the SQL that actually ships ----------------

    [Fact]
    public async Task TheBackfillRecoversBothTheBuriedAndTheDroppedRationale()
    {
        var (projectId, orgId, conversationId) = await fixture.SeedProjectAsync();

        var amendmentId = Ulid.NewUlid();
        var turnId = Ulid.NewUlid();

        // An amendment exactly as the pre-fix code wrote one: rationale
        // present inside ContentJson for the create, absent everywhere else.
        var oldDiff = """
            {
              "Creates": [{"Kind":"rule","Layer":"domain",
                           "ContentJson":"{\"Text\":\"A rule.\",\"Rationale\":\"Buried in content.\"}",
                           "CanonicalText":"A rule."}],
              "Revises": [],
              "Retires": [{"SpecId":"01ARZ3NDEKTSV4RRFFQ69G5FAV"}],
              "EdgeAdds": [],
              "EdgeRetires": [{"EdgeId":"edge-1"}]
            }
            """;

        // What the model actually said, which is where the dropped halves
        // are still recoverable from.
        var payloads = JsonSerializer.Serialize(new object[]
        {
            new
            {
                type = "spec_diff",
                amendment_id = amendmentId,
                diff = new
                {
                    creates = new[] { new { rationale = "Ignored — the stored one wins." } },
                    revises = Array.Empty<object>(),
                    retires = new[] { new { rationale = "Dropped at translation." } },
                    edge_adds = Array.Empty<object>(),
                    edge_retires = new[] { new { rationale = "Also dropped." } },
                },
            },
        });

        await using (var db = fixture.NewDb())
        {
            db.Turns.Add(new Turn
            {
                Id = turnId, ConversationId = conversationId, Seq = 900,
                Role = TurnRole.Assistant, Content = "", PayloadsJson = payloads,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.Amendments.Add(new Amendment
            {
                Id = amendmentId, ProjectId = projectId, OrgId = orgId,
                ConversationId = conversationId, TurnId = null, ProposedBy = "architect",
                Status = AmendmentStatus.Proposed, DiffJson = oldDiff,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await RunBackfillAsync();

        await using var verify = fixture.NewDb();
        var stored = await verify.Amendments.AsNoTracking().SingleAsync(a => a.Id == amendmentId);
        var rendered = Mcp.Tools.AmendmentSummary.From(stored).Diff!;

        // Buried, recovered from the amendment's own content.
        Assert.Equal("Buried in content.", rendered.Creates[0].Rationale);
        // Dropped, recovered from what the model said.
        Assert.Equal("Dropped at translation.", rendered.Retires[0].Rationale);
        Assert.Equal("Also dropped.", rendered.EdgeRetires[0].Rationale);
    }

    [Fact]
    public async Task TheBackfillNeverOverwritesARationaleThatIsAlreadyThere()
    {
        var (projectId, orgId, conversationId) = await fixture.SeedProjectAsync();
        var amendmentId = Ulid.NewUlid();

        await using (var db = fixture.NewDb())
        {
            db.Turns.Add(new Turn
            {
                Id = Ulid.NewUlid(), ConversationId = conversationId, Seq = 901,
                Role = TurnRole.Assistant, Content = "",
                PayloadsJson = JsonSerializer.Serialize(new object[]
                {
                    new
                    {
                        type = "spec_diff",
                        amendment_id = amendmentId,
                        diff = new { creates = new[] { new { rationale = "From the turn." } } },
                    },
                }),
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.Amendments.Add(new Amendment
            {
                Id = amendmentId, ProjectId = projectId, OrgId = orgId,
                ConversationId = conversationId, TurnId = null, ProposedBy = "architect",
                Status = AmendmentStatus.Approved,
                DiffJson = """
                    {"Creates":[{"Kind":"rule","Layer":"domain","ContentJson":"{\"Text\":\"x\"}",
                                 "CanonicalText":"x","Rationale":"What was approved."}],
                     "Revises":[],"Retires":[],"EdgeAdds":[],"EdgeRetires":[]}
                    """,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await RunBackfillAsync();
        await RunBackfillAsync();   // idempotent: running twice changes nothing

        await using var verify = fixture.NewDb();
        var stored = await verify.Amendments.AsNoTracking().SingleAsync(a => a.Id == amendmentId);

        // The stored value is the one a human approved. A migration doing
        // archaeology does not get to overrule it.
        Assert.Equal("What was approved.", Mcp.Tools.AmendmentSummary.From(stored).Diff!.Creates[0].Rationale);
    }

    // ---- helpers ----------------------------------------------------------

    /// <summary>
    /// Runs the migration's own SQL rather than a copy of it. The fixture
    /// applies migrations to an empty database, so the backfill has nothing
    /// to act on when it first runs — re-running the shipped file against
    /// seeded rows is the only way to test what actually ships.
    /// </summary>
    private async Task RunBackfillAsync([CallerFilePath] string here = "")
    {
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
        var sql = await File.ReadAllTextAsync(Path.Combine(
            repoRoot, "src", "DarkFactory.Data", "Migrations", "0009_backfill_amendment_rationale.sql"));

        await using var connection = new NpgsqlConnection(fixture.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task ProposeAndApproveAsync(
        string projectId, string orgId, string conversationId, SpecDiffDocument document)
    {
        string amendmentId;
        await using (var db = fixture.NewDb())
        {
            amendmentId = (await new SpecGraphService(db).ProposeAsync(
                projectId, orgId, conversationId, null, "architect",
                SpecDiffTranslator.ToSpecDiff(document))).Id;
        }
        await using (var db = fixture.NewDb())
        {
            await new SpecGraphService(db).ApproveAsync(amendmentId, "rod");
        }
    }
}
