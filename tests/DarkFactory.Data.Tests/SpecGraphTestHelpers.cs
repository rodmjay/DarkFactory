using System.Text.Json;
using DarkFactory.Core;

namespace DarkFactory.Data.Tests;

/// <summary>
/// Propose-then-approve is the only way content enters the graph
/// (docs/adr/0017), so every test that needs a populated graph goes through
/// it rather than inserting rows behind the service's back — otherwise the
/// tests would be proving things about a state the real system can't reach.
/// </summary>
internal static class Graph
{
    public static string Canonical(string text) => text.Trim();

    public static string Content(string text) => JsonSerializer.Serialize(new { text });

    public static SpecDiffNodeCreate Create(string text, string kind = "rule", string layer = "domain") =>
        new(kind, layer, Content(text), Canonical(text));

    public static SpecDiffNodeRevise Revise(string specId, string text) =>
        new(specId, Content(text), Canonical(text));

    public static SpecDiff Diff(
        IReadOnlyList<SpecDiffNodeCreate>? creates = null,
        IReadOnlyList<SpecDiffNodeRevise>? revises = null,
        IReadOnlyList<SpecDiffNodeRetire>? retires = null,
        IReadOnlyList<SpecDiffEdgeAdd>? edgeAdds = null,
        IReadOnlyList<SpecDiffEdgeRetire>? edgeRetires = null) =>
        new(creates ?? [], revises ?? [], retires ?? [], edgeAdds ?? [], edgeRetires ?? []);

    /// <summary>
    /// Proposes and approves in one step, each on its own DbContext so no
    /// test ever reads a row out of a stale identity map instead of the
    /// database.
    /// </summary>
    public static async Task<Amendment> ApplyAsync(
        SpecGraphTestFixture fixture, string projectId, string orgId, string conversationId, SpecDiff diff)
    {
        Amendment amendment;
        await using (var db = fixture.NewDb())
        {
            amendment = await new SpecGraphService(db)
                .ProposeAsync(projectId, orgId, conversationId, turnId: null, proposedBy: "tester", diff);
        }

        await using (var db = fixture.NewDb())
        {
            return await new SpecGraphService(db).ApproveAsync(amendment.Id, approvedBy: "tester");
        }
    }
}
