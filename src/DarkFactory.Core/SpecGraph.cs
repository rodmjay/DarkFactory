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

public enum ServerStatus { Registered, Conformant, Failed }

/// <summary>docs/adr/0018 and docs/adr/0019. Registration (df.servers.register) lands in step 3b.</summary>
public sealed class Server
{
    public required string Id { get; init; }
    public required string OrgId { get; init; }
    public string? ProjectId { get; init; }
    public required string Name { get; init; }
    public required ServerTier Tier { get; init; }
    public required string Domain { get; init; }
    public required string ConventionVersion { get; init; }
    public required string ManifestJson { get; init; }
    public string? LiveDescribeJson { get; set; }
    public required ServerStatus Status { get; set; }
    public DateTimeOffset? LastConformanceAt { get; set; }
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
}

// AgentPolicy (docs/adr/0022) is superseded by team_members and
// assignments (docs/adr/0028) before it was ever built — see that ADR.
// Those tables are step 3c work.
