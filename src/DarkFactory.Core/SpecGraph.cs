namespace DarkFactory.Core;

// The spec graph (docs/adr/0016-spec-graph-content-addressed-append-only.md):
// durable project state, content-addressed and append-only. See also
// docs/adr/0017 (conversations) and docs/adr/0007 for the shared failure
// vocabulary these tables don't use (this is data, not calls).

public enum ConversationStatus { Active, Archived }

public sealed class Conversation
{
    public required string Id { get; init; }
    public required string ProjectId { get; init; }
    public required string OrgId { get; init; }
    public string? Title { get; set; }
    public required string CreatedBy { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required ConversationStatus Status { get; set; }
}

public enum TurnRole { User, Assistant, System }

public sealed class Turn
{
    public required string Id { get; init; }
    public required string ConversationId { get; init; }
    public required int Seq { get; init; }
    public required TurnRole Role { get; init; }
    public required string Content { get; init; }
    /// <summary>Serialized ADR-0021 payloads (markdown, spec_diff, ...), when this turn produced any.</summary>
    public string? PayloadsJson { get; set; }
    public string? RetrievalRef { get; set; }
    public int? TokenUsage { get; set; }
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// A stable identity (<see cref="SpecId"/>) whose content lives in
/// immutable <see cref="SpecRevision"/> rows. Never updated except the
/// one-way <see cref="RetiredAt"/> transition.
/// </summary>
public sealed class SpecNode
{
    public required string SpecId { get; init; }
    public required string ProjectId { get; init; }
    public required string OrgId { get; init; }
    public required string Kind { get; init; }
    public required string Layer { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? RetiredAt { get; set; }
}

/// <summary>
/// Immutable, content-addressed: keyed by (SpecId, Hash) where Hash is the
/// SHA-256 of CanonicalText. Never updated after insert — the application's
/// database role has no UPDATE/DELETE grant on this table at all (see
/// src/DarkFactory.Data/Migrations/0002_spec_graph.sql).
/// </summary>
public sealed class SpecRevision
{
    public required string SpecId { get; init; }
    public required string Hash { get; init; }
    public required string ContentJson { get; init; }
    public required string CanonicalText { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string ProvenanceId { get; init; }
}

public sealed class SpecEdge
{
    public required string Id { get; init; }
    public required string ProjectId { get; init; }
    public required string OrgId { get; init; }
    public required string FromSpecId { get; init; }
    public required string ToSpecId { get; init; }
    /// <summary>"depends_on" | "conflicts_with" | "supersedes" | "implements" | "constrains" | a plugin-declared kind.</summary>
    public required string Kind { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string ProvenanceId { get; init; }
    public DateTimeOffset? RetiredAt { get; set; }
}

/// <summary>A named, immutable set of (SpecId, RevisionHash) pairs — see <see cref="SnapshotMember"/>.</summary>
public sealed class SpecSnapshot
{
    public required string Id { get; init; }
    public required string ProjectId { get; init; }
    public required string OrgId { get; init; }
    public required string Name { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string ProvenanceId { get; init; }
}

public sealed class SnapshotMember
{
    public required string SnapshotId { get; init; }
    public required string SpecId { get; init; }
    public required string RevisionHash { get; init; }
}

/// <summary>
/// The edges that were active when the snapshot was taken.
/// <para>
/// The brief describes a snapshot as a set of (spec_id, revision_hash)
/// pairs, which pins node content but says nothing about topology. That
/// leaves only one way to answer "which edges existed then" — compare
/// <c>created_at</c>/<c>retired_at</c> against the snapshot's timestamp —
/// and that makes a snapshot's meaning depend on clock resolution: two
/// snapshots taken in the same tick would disagree with themselves.
/// Recording edge membership explicitly makes a snapshot a complete,
/// self-describing picture of the graph, and makes diff a pure set
/// comparison with no clock in it.
/// </para>
/// </summary>
public sealed class SnapshotEdge
{
    public required string SnapshotId { get; init; }
    public required string EdgeId { get; init; }
}

public enum AmendmentStatus { Proposed, Approved, Rejected }

/// <summary>A proposed diff against the graph (docs/adr/0017). Only an Approved amendment can seed a run.</summary>
public sealed class Amendment
{
    public required string Id { get; init; }
    public required string ProjectId { get; init; }
    public required string OrgId { get; init; }
    public required string ConversationId { get; init; }
    public string? TurnId { get; init; }
    public required string ProposedBy { get; init; }
    public required AmendmentStatus Status { get; set; }
    public required string DiffJson { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public enum ApprovalTargetType { Amendment, Gate }

public enum ApprovalDecision { Approved, Rejected }

public sealed class Approval
{
    public required string Id { get; init; }
    public required ApprovalTargetType TargetType { get; init; }
    public required string TargetId { get; init; }
    public required ApprovalDecision Decision { get; init; }
    public required string ApprovedBy { get; init; }
    public string? Reason { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public enum ActorType { Human, Agent }

/// <summary>
/// Required on every revision, edge, and snapshot. Immutable — every
/// property is init-only, and (like SpecRevision) the application's
/// database role has no UPDATE/DELETE grant on this table.
/// </summary>
public sealed class Provenance
{
    public required string Id { get; init; }
    public string? ConversationId { get; init; }
    public string? TurnId { get; init; }
    public required ActorType ActorType { get; init; }
    public required string ActorId { get; init; }
    public string? ApprovedBy { get; init; }
    public required DateTimeOffset At { get; init; }
}

public enum ServerTier { BuiltIn, Premium, Community }

/// <summary>
/// <c>Degraded</c> is the ADR-0018 flag: the server answered and works for
/// some of what it claims, but its manifest and its live describe disagree,
/// or a conformance probe failed. A server that claims a capability it does
/// not have is degraded, not rejected — it stays usable for everything that
/// did pass, and the dashboard shows what didn't.
///
/// <c>Unreachable</c> is docs/adr/0038's: the server stopped answering the
/// health check. It says nothing about what the server can do — only that
/// the factory cannot currently ask it — and it ends by itself: the next
/// answer re-verifies the server and sets whichever status that earns.
/// </summary>
public enum ServerStatus { Registered, Conformant, Degraded, Failed, Unreachable }

/// <summary>docs/adr/0018 and docs/adr/0019.</summary>
public sealed class Server
{
    public required string Id { get; init; }
    public required string OrgId { get; init; }
    /// <summary>
    /// The project this server serves, or null for an org-wide server — a
    /// standards server always (docs/adr/0038: standards are what the
    /// organisation decided, shared by every project), and a workspace,
    /// which a project is bound to by URL instead.
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>
    /// The MCP endpoint. Together with <see cref="OrgId"/> this is the
    /// registry's natural key: re-registering the same URL for the same org
    /// updates the existing row rather than creating a second one.
    /// </summary>
    public required string Url { get; init; }

    public required string Name { get; set; }
    public required ServerTier Tier { get; set; }
    public required string Domain { get; set; }
    public required string ConventionVersion { get; set; }

    /// <summary>The static manifest this server was listed with (docs/adr/0018).</summary>
    public required string ManifestJson { get; set; }

    /// <summary>The last validated <c>df.describe()</c> response.</summary>
    public string? LiveDescribeJson { get; set; }

    /// <summary>
    /// The stored disagreement between <see cref="ManifestJson"/> and
    /// <see cref="LiveDescribeJson"/>, or null when they agree. Persisted
    /// rather than recomputed on read: what the dashboard shows must be
    /// what registration actually observed, not a fresh comparison against
    /// a describe response that may have changed since.
    /// </summary>
    public string? ManifestDiffJson { get; set; }

    public required ServerStatus Status { get; set; }
    public required DateTimeOffset RegisteredAt { get; init; }
    public DateTimeOffset? LastConformanceAt { get; set; }

    // docs/adr/0038 — how the connection stands now. Written by every health
    // check, so the registry's answer to "is it up" is at most one check old.

    public DateTimeOffset? LastCheckedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>The first miss of the current run of misses; null while it answers.</summary>
    public DateTimeOffset? UnreachableSince { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? NextCheckAt { get; set; }

    /// <summary>When it last answered again after an outage and was re-verified.</summary>
    public DateTimeOffset? HealedAt { get; set; }
    public int HealCount { get; set; }

    /// <summary>
    /// Removal is a retirement, not a delete. A server's conformance
    /// history is an audit record — that this server once failed
    /// <c>df.exec.run</c> stays true after someone removes it, and hard
    /// deletion would either erase that or orphan it. Removed servers are
    /// excluded from <c>df.servers.list</c>; re-registering the same
    /// (org_id, url) revives the row rather than creating a second one.
    /// </summary>
    public DateTimeOffset? RemovedAt { get; set; }
}

public enum ConformanceStatus
{
    Passed,
    Failed,

    /// <summary>
    /// Declared by the server, but this slice ships no probe for it. Not a
    /// failure — an honest statement that the factory did not check, which
    /// is a different thing from "checked and fine".
    /// </summary>
    NotProbed,
}

/// <summary>
/// One row per declared capability per conformance pass — deliberately not
/// a single boolean on the server. "Conformance failed" tells a user
/// nothing; "df.exec.run failed, everything else passed" tells them what to
/// fix, and is what the dashboard renders.
/// </summary>
public sealed class ConformanceResult
{
    public required string Id { get; init; }
    public required string ServerId { get; init; }

    /// <summary>Groups every row written by a single conformance pass, so history is readable.</summary>
    public required string ConformanceRunId { get; init; }

    public required string Capability { get; init; }
    public required ConformanceStatus Status { get; init; }
    public string? Detail { get; init; }
    public required long DurationMs { get; init; }
    public required DateTimeOffset CheckedAt { get; init; }
}

/// <summary>
/// docs/adr/0023. The embedding column is deliberately absent in this
/// slice (pgvector is not provisioned; embeddings are out of scope) — this
/// table exists so the shape is settled, populated later.
/// </summary>
public sealed class StandardsIndexEntry
{
    public required string Id { get; init; }
    public required string ServerId { get; init; }
    public string? ProjectId { get; init; }
    public required string ChunkRef { get; init; }
    public required string Layer { get; init; }
    public required string Text { get; init; }
    public required string SourceRef { get; init; }

    // Written by ingest (StandardsIngestService, docs/adr/0023).
    public string? Title { get; init; }

    /// <summary>The server's own <c>updated</c> for the standard — what a later ingest compares, and what a context pack records.</summary>
    public string? Updated { get; init; }
    public DateTimeOffset? IngestedAt { get; init; }
}

// AgentPolicy (docs/adr/0022) is superseded by team_members and
// assignments (docs/adr/0028) before it was ever built — see that ADR.
// Those tables are step 3c work.
