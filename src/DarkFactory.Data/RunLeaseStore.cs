using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data;

/// <summary>
/// Claims runs for processing. Lease, not just lock (docs/adr/0008):
/// `FOR UPDATE SKIP LOCKED` stops two workers claiming the same row in the
/// same instant, but a worker that crashes mid-stage drops its Postgres
/// session (and its row lock) immediately on disconnect. Without a lease,
/// the very next poll — from this worker's own restart, or another worker
/// entirely — would grab the run again before anyone can reason about what
/// state the crashed attempt left behind. <see cref="LeaseExpiresAt"/> is
/// committed as part of the claim, survives the crash, and is what the
/// WHERE clause below actually gates re-claiming on.
/// </summary>
public sealed class RunLeaseStore(DarkFactoryDbContext dbContext)
{
    /// <summary>
    /// Atomically claims the oldest runnable run: one that has never been
    /// claimed (status Pending) or whose lease has lapsed (status Running
    /// but LeaseExpiresAt is in the past — the previous worker went away
    /// mid-stage). Returns null if nothing is claimable right now.
    /// </summary>
    public async Task<Run?> ClaimNextRunnableAsync(
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        var claimed = await dbContext.Runs.FromSqlInterpolated($@"
                UPDATE runs
                SET leased_by = {workerId},
                    lease_expires_at = now() + {leaseDuration},
                    status = 'Running',
                    updated_at = now()
                WHERE id = (
                    SELECT id FROM runs
                    WHERE status IN ('Pending', 'Running')
                      AND (lease_expires_at IS NULL OR lease_expires_at < now())
                    ORDER BY created_at
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1
                )
                RETURNING *")
            .ToListAsync(cancellationToken);

        return claimed.SingleOrDefault();
    }

    /// <summary>
    /// Heartbeat: extends a held lease so a still-in-progress stage doesn't
    /// get stolen out from under its worker. Only succeeds while this
    /// worker is still the recorded lease holder — if it returns false, the
    /// lease has already been reassigned (this worker is stale) and it
    /// must stop processing the run rather than persist a result.
    /// </summary>
    public async Task<bool> RenewLeaseAsync(
        string runId,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        var updated = await dbContext.Runs
            .Where(r => r.Id == runId && r.LeasedBy == workerId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(r => r.LeaseExpiresAt, _ => DateTimeOffset.UtcNow + leaseDuration),
                cancellationToken);

        return updated == 1;
    }
}
