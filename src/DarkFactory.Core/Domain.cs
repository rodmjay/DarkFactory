namespace DarkFactory.Core;

// Domain entities for the factory's own state (never customer code). Every
// tenant-scoped record carries OrgId (docs/adr/0010) and ProjectId
// (docs/adr/0011). Persistence mapping lives in DarkFactory.Data (step 2);
// these are plain records so Core has no dependency on EF Core.

public sealed class Project
{
    public required string Id { get; init; }
    public required string OrgId { get; init; }
    public required string Name { get; set; }
    public required string WorkspaceMcpUrl { get; set; }
    public string? WorkspaceName { get; set; }
    public string? WorkspaceRoot { get; set; }
    public string[] StackHints { get; set; } = [];
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed class WorkItem
{
    public required string Id { get; init; }
    public required string OrgId { get; init; }
    public required string ProjectId { get; init; }
    public required string Input { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed class Run
{
    public required string Id { get; init; }
    public required string OrgId { get; init; }
    public required string ProjectId { get; init; }
    public required string WorkItemId { get; init; }
    public required StageId CurrentStage { get; set; }
    public required RunStatus Status { get; set; }

    // Lease, not just lock (docs/adr/0008): a worker that dies mid-stage
    // drops its Postgres session lock immediately, but LeaseExpiresAt was
    // committed as part of the claim and survives the crash. The run is
    // only re-claimable once the lease naturally expires, which is what
    // makes crash/resume deterministic rather than a race against however
    // fast the next poll happens to be.
    public string? LeasedBy { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }

    // docs/adr/0004 (amended): a run records the spec snapshot it was built
    // against, seeded from the amendment(s) that produced it. Populated
    // once df.work.create exists (step 3c) — nullable until then since
    // every run created up to and including step 2 predates the spec
    // graph entirely.
    public string? SnapshotId { get; set; }
    public string[]? AmendmentIds { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>A persisted checkpoint: the durability unit for resume-after-crash. See docs/adr/0008.</summary>
public sealed class StageCheckpoint
{
    public required string Id { get; init; }
    public required string RunId { get; init; }
    public required StageId Stage { get; init; }
    public required RunStatus Status { get; init; }
    public string? ArtifactRef { get; set; }
    public int Attempt { get; set; } = 1;
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Artifacts are passed between calls by reference (docs/adr/0004): the
/// body lives here, content-addressed by Sha256, and callers only ever
/// hand each other <see cref="Id"/> (exposed as a "factory://artifacts/{id}"
/// ref — see DarkFactory.Data.IArtifactStore). v1 stores the body inline in
/// Postgres; hosted mode can move bodies to blob storage behind the same
/// interface without touching callers.
/// </summary>
public sealed class Artifact
{
    public required string Id { get; init; }
    public required string OrgId { get; init; }
    public required string ProjectId { get; init; }

    // Exactly one of these is set. Most artifacts belong to a run, but a
    // ContextPack (docs/adr/0023 — what the model was shown for one
    // conversational turn) belongs to a conversation, and the conversation
    // is no longer part of a run at all (docs/adr/0003, as amended).
    // Content addressing and the "factory://artifacts/{id}" ref scheme are
    // worth reusing for both rather than inventing a second store.
    public string? RunId { get; init; }
    public string? ConversationId { get; init; }

    public required string Type { get; init; } // "Plan" | "ChangeSet" | "TestReport" | "ContextPack"
    public required string ContentJson { get; init; }
    public required string Sha256 { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed class Gate
{
    public required string Id { get; init; }
    public required string RunId { get; init; }
    public required GateKind Kind { get; init; }
    public required GateStatus Status { get; set; }
    public string? Reason { get; set; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

/// <summary>
/// The durable event log — and, via <see cref="PublishedAt"/>, the
/// transactional outbox (docs/adr/0008): a stage handler writes its
/// checkpoint and its "stage.completed" event in the same SaveChanges
/// transaction, so a crash between "persisted" and "notified the
/// dashboard" is impossible — there is no separate "notified" step to
/// crash between. A distinct <see cref="DarkFactory.Engine"/>-external
/// publisher (hosted in DarkFactory.Mcp, next to the SignalR hub it
/// broadcasts through) polls rows where PublishedAt is null, broadcasts
/// them, and stamps PublishedAt. Nothing about producing events depends on
/// that publisher being up.
/// </summary>
public sealed class Event
{
    public required string Id { get; init; }
    public required string RunId { get; init; }
    public required string Type { get; init; } // e.g. "stage.completed", "gate.waiting"
    public string? DataJson { get; set; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? PublishedAt { get; set; }
}

/// <summary>An outbound call record. See docs/adr/0012-audit-log.md.</summary>
public sealed class AuditEntry
{
    public required string Id { get; init; }
    public required string OrgId { get; init; }
    public required string ProjectId { get; init; }
    public required string RunId { get; init; }
    public required string StageId { get; init; }
    public required string TargetServer { get; init; }
    public required string ToolName { get; init; }
    public string? InputRef { get; init; }
    public bool Success { get; init; }
    public FailureClass? FailureClass { get; init; }
    public required long DurationMs { get; init; }
    public decimal? CostUsd { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
