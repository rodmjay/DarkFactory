using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data;

public sealed record BudgetState(int Spent, int? Limit)
{
    public bool Exceeded => Limit is { } limit && Spent > limit;
    public int? Remaining => Limit is { } limit ? Math.Max(limit - Spent, 0) : null;
}

/// <summary>
/// Answers whether a member has gone over budget on a run
/// (docs/adr/0028), reading from the <c>model_calls</c> fact table rather
/// than a ledger of its own (docs/adr/0032).
///
/// That is the whole change: one source of truth for tokens spent. A
/// separate counter would eventually disagree with the facts it was
/// summarising, and the disagreement would surface as a budget that fired
/// at the wrong time — which is exactly when nobody trusts it again.
///
/// The check happens <em>after</em> a call, not before, because the cost of
/// a call is not knowable until it returns; a pre-flight estimate enforced
/// as a limit would be a guess with authority. What that buys is an honest
/// guarantee: the run stops at the first stage that took it over, rather
/// than pretending to prevent an overspend that already happened.
/// </summary>
public sealed class BudgetService(DarkFactoryDbContext db)
{
    /// <summary>What one member has spent on one run, against their limit.</summary>
    public async Task<BudgetState> StateAsync(
        string runId, string teamMemberId, CancellationToken cancellationToken = default)
    {
        var spent = await db.ModelCalls.AsNoTracking()
            .Where(c => c.RunId == runId && c.TeamMemberId == teamMemberId)
            .SumAsync(c => c.InputTokensUncached + c.InputTokensCached + c.OutputTokens, cancellationToken);

        var limit = await db.TeamMembers.AsNoTracking()
            .Where(m => m.Id == teamMemberId)
            .Select(m => m.TokenBudget)
            .SingleOrDefaultAsync(cancellationToken);

        return new BudgetState(spent, limit);
    }

    /// <summary>Total tokens across every member, for the run summary.</summary>
    public async Task<int> TotalForRunAsync(string runId, CancellationToken cancellationToken = default) =>
        await db.ModelCalls.AsNoTracking()
            .Where(c => c.RunId == runId)
            .SumAsync(c => c.InputTokensUncached + c.InputTokensCached + c.OutputTokens, cancellationToken);

    /// <summary>
    /// The message a stage fails with when it takes a member over budget.
    /// Classified <see cref="FailureClass.NeedsHuman"/>, never
    /// <see cref="FailureClass.Retryable"/>: retrying is the one thing that
    /// cannot help, and raising a budget is a person's decision.
    /// </summary>
    public static string OverBudgetMessage(string role, BudgetState state) =>
        $"'{role}' has used {state.Spent} tokens on this run, over its budget of {state.Limit}. " +
        "Raise the member's token budget or split the work; retrying will not help.";
}
