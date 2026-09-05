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

    [JsonPropertyName("assembled_at")] public required DateTimeOffset AssembledAt { get; init; }

    /// <summary>Which attempt this pack served. A retry after a schema failure records its own pack.</summary>
    [JsonPropertyName("attempt")] public required int Attempt { get; init; }
}

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
