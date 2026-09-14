using System.Text.Json.Serialization;

namespace DarkFactory.Core;

/// <summary>
/// Everything the model was shown for one conversational turn, assembled
/// before the call and persisted by reference afterwards
/// (<c>turns.retrieval_ref</c>).
///
/// This exists so that "why did it propose that?" is answerable from the
/// database months later. A prompt reconstructed after the fact is a guess:
/// the spec graph will have moved on, the standards will have been
/// reindexed, and the neighbourhood traversal may not even return the same
/// nodes. The only honest answer is the one recorded at the time, which is
/// what this is.
/// </summary>
public sealed record ContextPack
{
    [JsonPropertyName("conversation_id")] public required string ConversationId { get; init; }
    [JsonPropertyName("project_id")] public required string ProjectId { get; init; }

    /// <summary>The team member this turn was routed to (docs/adr/0028), and the deployment it named.</summary>
    [JsonPropertyName("agent")] public required ContextAgent Agent { get; init; }

    /// <summary>The spec neighbourhood retrieved for this turn (docs/adr/0023).</summary>
    [JsonPropertyName("spec_neighborhood")] public required IReadOnlyList<ContextSpecNode> SpecNeighborhood { get; init; }

    [JsonPropertyName("spec_edges")] public required IReadOnlyList<ContextSpecEdge> SpecEdges { get; init; }

    /// <summary>
    /// docs/adr/0023's index is out of scope for this slice, so this is the
    /// one built-in standards resource. The field is a list because the
    /// shape has to survive the index arriving, not because there are
    /// several yet.
    /// </summary>
    [JsonPropertyName("standards")] public required IReadOnlyList<ContextStandard> Standards { get; init; }

    [JsonPropertyName("conversation_tail")] public required IReadOnlyList<ContextTurn> ConversationTail { get; init; }

    /// <summary>
    /// Versioned instruction bundles (docs/adr/0028). The skills tables are
    /// step 3d work; this carries the built-in architect skill in the
    /// meantime so the recorded context is complete even now.
    /// </summary>
    [JsonPropertyName("skills")] public required IReadOnlyList<ContextSkill> Skills { get; init; }

    /// <summary>
    /// The servers this project is connected to and how each stood when the
    /// turn was assembled (docs/adr/0038). Without it the architect cannot
    /// tell a project with nothing connected from one whose specifications
    /// are sitting on a server it was never told about — and it said as
    /// much, to a user whose corpus server had been answering every thirty
    /// seconds for three hours.
    /// </summary>
    [JsonPropertyName("connections")] public IReadOnlyList<ContextConnection> Connections { get; init; } = [];

    /// <summary>
    /// Specifications imported but not yet in the graph (docs/adr/0037).
    /// Title and summary per document, not the text: enough to answer "what
    /// specs are there" and to avoid proposing duplicates, at a fraction of
    /// the corpus's size on every turn.
    /// </summary>
    [JsonPropertyName("imports")] public IReadOnlyList<ContextImport> Imports { get; init; } = [];

    [JsonPropertyName("assembled_at")] public required DateTimeOffset AssembledAt { get; init; }

    /// <summary>Which attempt this pack served. A retry after a schema failure records its own pack.</summary>
    [JsonPropertyName("attempt")] public required int Attempt { get; init; }
}

public sealed record ContextConnection(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("domain")] string Domain,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("last_seen_at")] DateTimeOffset? LastSeenAt,
    [property: JsonPropertyName("unreachable_since")] DateTimeOffset? UnreachableSince,
    [property: JsonPropertyName("last_error")] string? LastError);

public sealed record ContextImport(
    [property: JsonPropertyName("intake_id")] string IntakeId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("extracted")] int Extracted,
    [property: JsonPropertyName("proposed")] int Proposed,
    [property: JsonPropertyName("open_questions")] int OpenQuestions,
    [property: JsonPropertyName("documents")] IReadOnlyList<ContextImportDocument> Documents);

public sealed record ContextImportDocument(
    [property: JsonPropertyName("source_ref")] string SourceRef,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("status")] string Status);

public sealed record ContextAgent(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("deployment")] string Deployment,
    [property: JsonPropertyName("team_id")] string TeamId,
    [property: JsonPropertyName("team_member_id")] string TeamMemberId);

public sealed record ContextSpecNode(
    [property: JsonPropertyName("spec_id")] string SpecId,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("layer")] string Layer,
    [property: JsonPropertyName("revision_hash")] string RevisionHash,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("retired")] bool Retired);

public sealed record ContextSpecEdge(
    [property: JsonPropertyName("edge_id")] string EdgeId,
    [property: JsonPropertyName("from_spec_id")] string FromSpecId,
    [property: JsonPropertyName("to_spec_id")] string ToSpecId,
    [property: JsonPropertyName("kind")] string Kind);

public sealed record ContextStandard(
    [property: JsonPropertyName("source_ref")] string SourceRef,
    [property: JsonPropertyName("layer")] string Layer,
    [property: JsonPropertyName("text")] string Text);

public sealed record ContextTurn(
    [property: JsonPropertyName("seq")] int Seq,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

public sealed record ContextSkill(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("instructions")] string Instructions);

/// <summary>
/// Guidance a human injected mid-run (docs/adr/0015). Carried into the next
/// stage's context, which is what makes steering different from cancelling
/// and restarting with a better prompt.
/// </summary>
public sealed record ContextSteer(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("actor_id")] string ActorId,
    [property: JsonPropertyName("stage")] string Stage);

/// <summary>
/// What a run stage's agent was shown, persisted by reference exactly as a
/// conversational turn's is (<see cref="ContextPack"/>). Same reason: a
/// stage's output is only reviewable if what produced it is recoverable,
/// and reconstructing it later gives you the graph as it is now rather than
/// as the agent saw it.
/// </summary>
public sealed record StageContextPack
{
    [JsonPropertyName("run_id")] public required string RunId { get; init; }
    [JsonPropertyName("project_id")] public required string ProjectId { get; init; }
    [JsonPropertyName("stage")] public required string Stage { get; init; }
    [JsonPropertyName("attempt")] public required int Attempt { get; init; }

    [JsonPropertyName("agent")] public required ContextAgent Agent { get; init; }

    /// <summary>The snapshot the run was created against (docs/adr/0004, as amended).</summary>
    [JsonPropertyName("snapshot_id")] public required string? SnapshotId { get; init; }

    /// <summary>The specifications this run exists to implement, pinned at that snapshot.</summary>
    [JsonPropertyName("specs")] public required IReadOnlyList<ContextSpecNode> Specs { get; init; }

    [JsonPropertyName("spec_edges")] public required IReadOnlyList<ContextSpecEdge> SpecEdges { get; init; }

    [JsonPropertyName("standards")] public required IReadOnlyList<ContextStandard> Standards { get; init; }

    [JsonPropertyName("skills")] public required IReadOnlyList<ContextSkill> Skills { get; init; }

    /// <summary>Everything a human has said to this run so far (docs/adr/0015).</summary>
    [JsonPropertyName("steers")] public required IReadOnlyList<ContextSteer> Steers { get; init; }

    /// <summary>Refs of the artifacts earlier stages produced, so `implement` can read the `plan`.</summary>
    [JsonPropertyName("prior_artifacts")] public required IReadOnlyList<ContextArtifact> PriorArtifacts { get; init; }

    [JsonPropertyName("assembled_at")] public required DateTimeOffset AssembledAt { get; init; }
}

public sealed record ContextArtifact(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("ref")] string Ref,
    [property: JsonPropertyName("stage")] string Stage);
