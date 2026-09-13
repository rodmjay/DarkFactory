using System.Text.Json.Serialization;

namespace DarkFactory.Core;

// Corpus intake (docs/adr/0037): bringing a body of specifications that
// already exists somewhere else — prose documents written before the
// factory — into the spec graph, without pretending it was always there.
//
// Four steps, in the order a person would take them: extract what a source
// actually says as small nodes, find the holes an implementer would have to
// guess at, ask them, and fill them. Only then does a source's draft become
// an amendment. Approval is untouched by any of this: an intake proposes,
// and the graph moves only when somebody approves (docs/adr/0017,
// docs/adr/0035).

/// <summary>
/// One import of one corpus into one project. It carries a conversation
/// because every amendment and every context pack in the factory belongs to
/// one; an intake's is the thread its proposals are filed under.
/// </summary>
public sealed class Intake
{
    public required string Id { get; init; }
    public required string ProjectId { get; init; }
    public required string OrgId { get; init; }
    public required string Name { get; init; }
    public required string ConversationId { get; init; }

    /// <summary>
    /// The corpus as submitted, stored once as an artifact. After intake the
    /// factory is authoritative, so the originals may be edited or deleted
    /// where they live; this is the copy every extraction was made from.
    /// </summary>
    public required string CorpusRef { get; init; }
    public required string CorpusSha256 { get; init; }
    public required string CreatedBy { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public enum IntakeSourceStatus { Pending, Extracted, Failed, Proposed }

/// <summary>One source document within an intake, and its current draft.</summary>
public sealed class IntakeSource
{
    public required string Id { get; init; }
    public required string IntakeId { get; init; }
    public required string ProjectId { get; init; }
    public required string OrgId { get; init; }

    /// <summary>Corpus order. Sources sort by ref, which for numbered documents is the order decisions were made in.</summary>
    public required int Seq { get; init; }

    /// <summary>Where the document came from, e.g. <c>moonbeam-specs:specs/drones/0079-the-working-swarm.md</c>. Cited in every node it yields.</summary>
    public required string SourceRef { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
    public required string ContentSha256 { get; init; }
    public required IntakeSourceStatus Status { get; set; }

    /// <summary>
    /// The current draft, as a specdiff document. Mutable on purpose — a
    /// draft is not the graph. Each extraction overwrites it until the
    /// source is proposed, and the context packs keep what produced every
    /// earlier one.
    /// </summary>
    public string? DraftJson { get; set; }

    /// <summary>How many extractions have been accepted. Zero until the first.</summary>
    public required int DraftRevision { get; set; }
    public string? ContextPackRef { get; set; }

    /// <summary>Why the last extraction was refused, verbatim. Cleared by the next accepted one.</summary>
    public string? Failure { get; set; }
    public string? AmendmentId { get; set; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExtractedAt { get; set; }
}

/// <summary>
/// The kinds of hole an extraction may report. Closed, for the same reason
/// node kinds are: an open set becomes a synonym soup, and the questions
/// screen groups by this.
/// </summary>
public static class IntakeHoleKinds
{
    /// <summary>The text supports more than one reading, and they would be built differently.</summary>
    public const string Ambiguity = "ambiguity";

    /// <summary>It conflicts with another document in the corpus, or with itself.</summary>
    public const string Contradiction = "contradiction";

    /// <summary>A requirement with no observable condition that would show it is met.</summary>
    public const string Untestable = "untestable";

    /// <summary>The behaviour depends on a term no document defines.</summary>
    public const string UndefinedTerm = "undefined_term";

    /// <summary>The document itself says it is undecided.</summary>
    public const string SourceOpenQuestion = "source_open_question";

    /// <summary>Behaviour the document plainly needs — an error case, a limit, a state — that nothing specifies.</summary>
    public const string Missing = "missing";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Ambiguity, Contradiction, Untestable, UndefinedTerm, SourceOpenQuestion, Missing,
    };
}

/// <summary>
/// <c>Open</c>: waiting on a person. <c>Answered</c>: a person answered, and
/// the next extraction must build the answer in. <c>Deferred</c>: a person
/// chose to leave it open, and it no longer blocks. <c>Resolved</c>: a later
/// extraction found it settled — by the answer to a different question, or
/// by the corpus — and stopped raising it.
/// </summary>
public enum IntakeQuestionStatus { Open, Answered, Deferred, Resolved }

public sealed class IntakeQuestion
{
    public required string Id { get; init; }
    public required string IntakeId { get; init; }
    public required string SourceId { get; init; }
    public required string ProjectId { get; init; }
    public required string OrgId { get; init; }
    public required string Kind { get; init; }
    public required string Question { get; init; }
    public string? Quote { get; init; }

    /// <summary>Indices into the current draft's <c>creates</c> whose wording depends on the answer, as a JSON array.</summary>
    public required string AffectsJson { get; set; }
    public required IntakeQuestionStatus Status { get; set; }

    /// <summary>The answer, or for a deferred question, why it was left open.</summary>
    public string? Answer { get; set; }
    public string? AnsweredBy { get; set; }
    public DateTimeOffset? AnsweredAt { get; set; }
    public required int RaisedInRevision { get; init; }

    /// <summary>
    /// The draft revision that took this question's outcome into account.
    /// Null on an answered question means the answer is not in the draft
    /// yet — which is exactly the state that must block a proposal.
    /// </summary>
    public int? IncorporatedInRevision { get; set; }
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>One document of a stored corpus artifact.</summary>
public sealed record IntakeCorpusDocument(
    [property: JsonPropertyName("source_ref")] string SourceRef,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("content")] string Content);

/// <summary>
/// What the model was shown for one extraction, persisted by reference for
/// the same reason a conversational <see cref="ContextPack"/> is. The corpus
/// is referenced rather than copied: it is identical for every extraction in
/// an intake, and storing it forty times would say nothing the first copy
/// does not.
/// </summary>
public sealed record IntakeContextPack
{
    [JsonPropertyName("intake_id")] public required string IntakeId { get; init; }
    [JsonPropertyName("source_id")] public required string SourceId { get; init; }
    [JsonPropertyName("source_ref")] public required string SourceRef { get; init; }
    [JsonPropertyName("project_id")] public required string ProjectId { get; init; }
    [JsonPropertyName("corpus_ref")] public required string CorpusRef { get; init; }
    [JsonPropertyName("corpus_sha256")] public required string CorpusSha256 { get; init; }
    [JsonPropertyName("agent")] public required ContextAgent Agent { get; init; }

    /// <summary>The draft revision this extraction would become if accepted.</summary>
    [JsonPropertyName("revision")] public required int Revision { get; init; }
    [JsonPropertyName("questions")] public required IReadOnlyList<ContextIntakeQuestion> Questions { get; init; }
    [JsonPropertyName("previous_draft")] public string? PreviousDraft { get; init; }
    [JsonPropertyName("layers_in_use")] public required IReadOnlyList<string> LayersInUse { get; init; }
    [JsonPropertyName("skills")] public required IReadOnlyList<ContextSkill> Skills { get; init; }
    [JsonPropertyName("assembled_at")] public required DateTimeOffset AssembledAt { get; init; }
    [JsonPropertyName("attempt")] public required int Attempt { get; init; }
}

public sealed record ContextIntakeQuestion(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("question")] string Question,
    [property: JsonPropertyName("answer")] string? Answer);
