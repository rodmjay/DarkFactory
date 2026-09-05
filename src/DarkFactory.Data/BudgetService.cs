using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data;

public sealed record BudgetState(int Spent, int? Limit)
{
    public bool Exceeded => Limit is { } limit && Spent > limit;
    public int? Remaining => Limit is { } limit ? Math.Max(limit - Spent, 0) : null;
}

/// <summary>
/// Records what each stage spent and answers whether a member has gone over
/// budget on a run (docs/adr/0028).
///
/// The check happens <em>after</em> the call, not before, because the cost
/// of a call is not knowable until it returns — a pre-flight estimate would
/// be a guess enforced as a limit. What that buys is an honest guarantee:
/// the run stops at the first stage that took it over, rather than
/// pretending it can prevent the overspend that already happened.
/// </summary>
public sealed class BudgetService(DarkFactoryDbContext db)
{
    public async Task<StageUsage> RecordAsync(
        Run run,
        StageId stage,
        int attempt,
        string? teamMemberId,
        string? deployment,
        ModelUsage usage,
        CancellationToken cancellationToken = default)
    {
        var row = new StageUsage
        {
            Id = Ulid.NewUlid(),
            RunId = run.Id,
            Stage = stage,
            Attempt = attempt,
            TeamMemberId = teamMemberId,
            Deployment = deployment,
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.StageUsages.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row;
    }

    /// <summary>What one member has spent on one run, against their limit.</summary>
    public async Task<BudgetState> StateAsync(
        string runId, string teamMemberId, CancellationToken cancellationToken = default)
    {
        var spent = await db.StageUsages.AsNoTracking()
            .Where(u => u.RunId == runId && u.TeamMemberId == teamMemberId)
            .SumAsync(u => u.InputTokens + u.OutputTokens, cancellationToken);

        var limit = await db.TeamMembers.AsNoTracking()
            .Where(m => m.Id == teamMemberId)
            .Select(m => m.TokenBudget)
            .SingleOrDefaultAsync(cancellationToken);

        return new BudgetState(spent, limit);
    }

    /// <summary>Total tokens across every member, for the run summary.</summary>
    public async Task<int> TotalForRunAsync(string runId, CancellationToken cancellationToken = default) =>
        await db.StageUsages.AsNoTracking()
            .Where(u => u.RunId == runId)
            .SumAsync(u => u.InputTokens + u.OutputTokens, cancellationToken);

    /// <summary>
    /// The message a stage fails with when it takes a member over budget.
    /// Classified <see cref="FailureClass.NeedsHuman"/>, never
    /// <see cref="FailureClass.Retryable"/>: retrying is the one thing that
    /// cannot help, and the decision to raise a budget belongs to a person.
    /// </summary>
    public static string OverBudgetMessage(string role, BudgetState state) =>
        $"'{role}' has used {state.Spent} tokens on this run, over its budget of {state.Limit}. " +
        "Raise the member's token budget or split the work; retrying will not help.";
}
