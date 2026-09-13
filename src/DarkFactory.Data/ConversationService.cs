using System.Text.Json;
using System.Text.Json.Serialization;
using DarkFactory.Contracts;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data;

// ADR-0021's rendering vocabulary, as much of it as this slice emits. The
// full versioned vocabulary with per-type schemas is step 4; these two
// types are what a conversational turn actually produces.

/// <summary>
/// docs/adr/0021: plugins bind to the component type, so the wire needs
/// exactly one field that says what a payload is.
///
/// It used to carry two. <c>[JsonDerivedType]</c> emitted <c>$type</c> and
/// an abstract <c>Type</c> property emitted <c>type</c>, with the same
/// value and nothing keeping them equal — a derived type registered with a
/// mismatched property would have made them disagree silently, and which
/// one was normative would have been decided by whichever renderer happened
/// to read the other. Naming the serializer's own discriminator <c>type</c>
/// makes it one field that the serializer enforces, and the ADR's word for
/// it is the one on the wire.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MarkdownPayload), "markdown")]
[JsonDerivedType(typeof(SpecDiffPayload), "spec_diff")]
public abstract record RenderPayload;

public sealed record MarkdownPayload(
    [property: JsonPropertyName("text")] string Text) : RenderPayload;

/// <summary>An amendment awaiting approval, rendered as a diff (docs/adr/0017, docs/adr/0021).</summary>
public sealed record SpecDiffPayload(
    [property: JsonPropertyName("amendment_id")] string AmendmentId,
    [property: JsonPropertyName("diff")] SpecDiffDocument Diff,
    [property: JsonPropertyName("summary")] string Summary) : RenderPayload;

public sealed record ConversationTurnResult(
    string TurnId,
    IReadOnlyList<RenderPayload> Payloads,
    string? AmendmentId,
    string ContextRef,
    ModelUsage Usage);

/// <summary>A conversation as a list shows it: enough to say where it stands without opening it.</summary>
public sealed record ConversationListing(
    Conversation Conversation,
    int TurnCount,
    DateTimeOffset UpdatedAt,
    int Amendments,
    int AwaitingAmendments,
    bool IsIntake);

/// <summary>Where one of a conversation's amendments stands, and why, if it was refused.</summary>
public sealed record AmendmentState(
    string Id, AmendmentStatus Status, string? TurnId, DateTimeOffset CreatedAt, string? RejectedReason);

public sealed record ConversationThread(
    ConversationListing Listing, IReadOnlyList<Turn> Turns, IReadOnlyList<AmendmentState> Amendments);

/// <summary>
/// The conversation (docs/adr/0017) — the product, not a job-submission
/// surface. One user turn in, one architect turn out, and possibly a
/// proposed amendment.
///
/// Two properties this class exists to guarantee:
/// <list type="bullet">
/// <item>Everything the model saw is persisted before the call, and the
/// turn points at it. "Why did it propose that?" is answerable from the
/// database, not reconstructed later from a graph that has since moved.</item>
/// <item>No amendment is ever persisted without validating. Invalid output
/// gets one retry carrying the exact violations, then the turn honestly
/// reports that it could not propose anything.</item>
/// </list>
/// </summary>
public sealed class ConversationService(
    DarkFactoryDbContext db,
    IModelGateway gateway,
    IArtifactStore artifacts,
    TeamService teams,
    SpecGraphService specs,
    SpecDiffTranslator translator)
{
    /// <summary>How many previous turns go into the context pack.</summary>
    public const int ConversationTailSize = 20;

    /// <summary>How many spec nodes the neighbourhood retrieval returns.</summary>
    public const int SpecNeighborhoodSize = 100;

    public const string ContextPackArtifactType = "ContextPack";

    public async Task<Conversation> StartAsync(
        string projectId, string? title, string createdBy, CancellationToken cancellationToken = default)
    {
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            ?? throw new InvalidOperationException($"No project '{projectId}'.");

        var conversation = new Conversation
        {
            Id = Ulid.NewUlid(),
            ProjectId = project.Id,
            OrgId = project.OrgId,
            Title = title,
            CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = ConversationStatus.Active,
        };

        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(cancellationToken);
        return conversation;
    }

    // ---- reads ------------------------------------------------------------

    /// <summary>A project's conversations, most recently active first.</summary>
    public async Task<IReadOnlyList<ConversationListing>> ListAsync(
        string projectId, CancellationToken cancellationToken = default)
    {
        var conversations = await db.Conversations.AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .ToListAsync(cancellationToken);

        return (await ListingsAsync(conversations, cancellationToken))
            .OrderByDescending(l => l.UpdatedAt)
            .ThenByDescending(l => l.Conversation.Id)
            .ToList();
    }

    /// <summary>
    /// One conversation's whole thread: every turn with the payloads it was
    /// stored with, and where each amendment it produced stands now. The
    /// payloads are returned as written rather than rebuilt, so a thread
    /// reads back exactly as it was answered.
    /// </summary>
    public async Task<ConversationThread> GetAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await db.Conversations.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == conversationId, cancellationToken)
            ?? throw new InvalidOperationException($"No conversation '{conversationId}'.");

        var turns = await db.Turns.AsNoTracking()
            .Where(t => t.ConversationId == conversationId)
            .OrderBy(t => t.Seq)
            .ToListAsync(cancellationToken);

        var amendments = await db.Amendments.AsNoTracking()
            .Where(a => a.ConversationId == conversationId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        var ids = amendments.Select(a => a.Id).ToList();
        var rejections = await db.Approvals.AsNoTracking()
            .Where(a => a.TargetType == ApprovalTargetType.Amendment
                && a.Decision == ApprovalDecision.Rejected
                && ids.Contains(a.TargetId))
            .ToListAsync(cancellationToken);

        var listing = (await ListingsAsync([conversation], cancellationToken)).Single();
        return new ConversationThread(
            listing,
            turns,
            amendments.Select(a => new AmendmentState(
                a.Id,
                a.Status,
                a.TurnId,
                a.CreatedAt,
                rejections.Where(r => r.TargetId == a.Id).OrderByDescending(r => r.CreatedAt).FirstOrDefault()?.Reason))
                .ToList());
    }

    private async Task<List<ConversationListing>> ListingsAsync(
        IReadOnlyList<Conversation> conversations, CancellationToken cancellationToken)
    {
        var ids = conversations.Select(c => c.Id).ToList();

        var turns = await db.Turns.AsNoTracking()
            .Where(t => ids.Contains(t.ConversationId))
            .GroupBy(t => t.ConversationId)
            .Select(g => new { ConversationId = g.Key, Count = g.Count(), Last = g.Max(t => t.CreatedAt) })
            .ToListAsync(cancellationToken);

        var amendments = await db.Amendments.AsNoTracking()
            .Where(a => ids.Contains(a.ConversationId))
            .Select(a => new { a.ConversationId, a.Status, a.CreatedAt })
            .ToListAsync(cancellationToken);

        // An intake's conversation is the thread its proposals are filed
        // under (docs/adr/0037). It is listed like any other, but a reader
        // needs to know it is an import rather than a discussion.
        var intakes = (await db.Intakes.AsNoTracking()
            .Where(i => ids.Contains(i.ConversationId))
            .Select(i => i.ConversationId)
            .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

        return conversations.Select(c =>
        {
            var t = turns.FirstOrDefault(x => x.ConversationId == c.Id);
            var mine = amendments.Where(a => a.ConversationId == c.Id).ToList();
            var updated = new[]
            {
                c.CreatedAt,
                t?.Last ?? c.CreatedAt,
                mine.Count == 0 ? c.CreatedAt : mine.Max(a => a.CreatedAt),
            }.Max();

            return new ConversationListing(
                c,
                t?.Count ?? 0,
                updated,
                mine.Count,
                mine.Count(a => a.Status == AmendmentStatus.Proposed),
                intakes.Contains(c.Id));
        }).ToList();
    }

    public async Task<ConversationTurnResult> TurnAsync(
        string conversationId, string message, string actorId, CancellationToken cancellationToken = default)
    {
        var conversation = await db.Conversations.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == conversationId, cancellationToken)
            ?? throw new InvalidOperationException($"No conversation '{conversationId}'.");

        var agent = await teams.ResolveAsync(conversation.ProjectId, AssignmentPoints.Conversation, cancellationToken);

        var nextSeq = await NextSeqAsync(conversationId, cancellationToken);
        var userTurn = new Turn
        {
            Id = Ulid.NewUlid(),
            ConversationId = conversationId,
            Seq = nextSeq,
            Role = TurnRole.User,
            Content = message,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Turns.Add(userTurn);
        await db.SaveChangesAsync(cancellationToken);

        // ---- attempt 1 ----------------------------------------------------

        var pack = await AssembleContextAsync(conversation, agent, attempt: 1, cancellationToken);
        var packRef = await StoreContextAsync(conversation, pack, cancellationToken);

        var messages = await BuildMessagesAsync(conversationId, cancellationToken);
        var completion = await gateway.CompleteAsync(
            new ModelRequest(
                agent.Deployment,
                ArchitectPrompt.SystemPrompt(pack),
                messages,
                // The architect emits a whole spec_diff and was on the same
                // chat-sized cap the implementer truncated against; null
                // takes the model's ceiling, a member override still wins.
                MaxOutputTokens: agent.MaxOutputTokens,
                Context: CallContext(conversation, agent, packRef, attempt: 1),
                CacheableSystemPrefix: ArchitectPrompt.CacheablePrefix(pack)),
            cancellationToken);

        var usage = completion.Usage;
        var parsed = ParseResponse(completion.Text);
        var validation = await ValidateProposalAsync(conversation.ProjectId, parsed, cancellationToken);

        // ---- attempt 2, only if the first produced something unusable -----

        if (!validation.Result.IsValid)
        {
            // A fresh pack for the retry: it is a different prompt, and
            // recording one context for two distinguishable calls would
            // make the audit trail quietly wrong.
            var retryPack = await AssembleContextAsync(conversation, agent, attempt: 2, cancellationToken);
            packRef = await StoreContextAsync(conversation, retryPack, cancellationToken);

            var retryMessages = messages.ToList();
            retryMessages.Add(new ModelMessage(ModelRole.Assistant, completion.Text));
            retryMessages.Add(new ModelMessage(ModelRole.User,
                ArchitectPrompt.RetryMessage(completion.Text, validation.Result)));

            var retry = await gateway.CompleteAsync(
                new ModelRequest(
                    agent.Deployment,
                    ArchitectPrompt.SystemPrompt(retryPack),
                    retryMessages,
                    MaxOutputTokens: agent.MaxOutputTokens,
                    Context: CallContext(conversation, agent, packRef, attempt: 2, retried: true),
                    CacheableSystemPrefix: ArchitectPrompt.CacheablePrefix(retryPack)),
                cancellationToken);

            usage = new ModelUsage(
                usage.InputTokens + retry.Usage.InputTokens,
                usage.OutputTokens + retry.Usage.OutputTokens);

            parsed = ParseResponse(retry.Text);
            validation = await ValidateProposalAsync(conversation.ProjectId, parsed, cancellationToken);
        }

        // ---- persist the turn ---------------------------------------------

        var payloads = new List<RenderPayload>();
        string? amendmentId = null;

        if (validation.Result.IsValid && validation.Document is { } document)
        {
            var amendment = await specs.ProposeAsync(
                conversation.ProjectId,
                conversation.OrgId,
                conversationId,
                userTurn.Id,
                proposedBy: agent.Role,
                SpecDiffTranslator.ToSpecDiff(document),
                cancellationToken);

            amendmentId = amendment.Id;
            payloads.Add(new MarkdownPayload(parsed?.Reply ?? ""));
            payloads.Add(new SpecDiffPayload(amendment.Id, document, Summarize(document)));
        }
        else if (parsed is { Settled: false } or null && validation.NothingProposed)
        {
            // The ordinary case: the conversation has not settled, so there
            // was nothing to validate and nothing to propose.
            payloads.Add(new MarkdownPayload(parsed?.Reply ?? ArchitectPrompt.CouldNotProposeMarkdown(validation.Result)));
        }
        else
        {
            // Two attempts, still not a valid proposal. Say so plainly
            // rather than persisting something that would not survive
            // approval — no invalid amendment reaches df.specs.propose.
            if (!string.IsNullOrWhiteSpace(parsed?.Reply))
            {
                payloads.Add(new MarkdownPayload(parsed!.Reply));
            }
            payloads.Add(new MarkdownPayload(ArchitectPrompt.CouldNotProposeMarkdown(validation.Result)));
        }

        var assistantTurn = new Turn
        {
            Id = Ulid.NewUlid(),
            ConversationId = conversationId,
            Seq = nextSeq + 1,
            Role = TurnRole.Assistant,
            Content = parsed?.Reply ?? "",
            PayloadsJson = JsonSerializer.Serialize(payloads),
            // The whole point of constraint 2: the turn points at exactly
            // what the model was shown when it produced this.
            RetrievalRef = packRef,
            TokenUsage = usage.TotalTokens,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Turns.Add(assistantTurn);
        await db.SaveChangesAsync(cancellationToken);

        return new ConversationTurnResult(assistantTurn.Id, payloads, amendmentId, packRef, usage);
    }

    /// <summary>
    /// What docs/adr/0032 records against the call. A conversational turn
    /// has no run, no batch and no stage — those columns stay null rather
    /// than being invented, because a fact table with honest nulls beats
    /// one whose dimensions cannot be trusted.
    /// </summary>
    private static ModelCallContext CallContext(
        Conversation conversation, ResolvedAgent agent, string packRef, int attempt, bool? retried = null) =>
        new()
        {
            OrgId = conversation.OrgId,
            ProjectId = conversation.ProjectId,
            StageId = AssignmentPoints.Conversation,
            Attempt = attempt,
            TeamMemberId = agent.TeamMemberId,
            ContextPackRef = packRef,
            PromptTemplateVersion = ArchitectPrompt.TemplateVersion,
            Retried = retried,
        };

    // ---- context assembly -------------------------------------------------

    private async Task<ContextPack> AssembleContextAsync(
        Conversation conversation, ResolvedAgent agent, int attempt, CancellationToken cancellationToken)
    {
        var nodes = await db.SpecNodes.AsNoTracking()
            .Where(n => n.ProjectId == conversation.ProjectId)
            .OrderBy(n => n.SpecId)
            .Take(SpecNeighborhoodSize)
            .ToListAsync(cancellationToken);

        var contextNodes = new List<ContextSpecNode>(nodes.Count);
        foreach (var node in nodes)
        {
            var latest = await specs.GetLatestRevisionAsync(node.SpecId, cancellationToken);
            if (latest is null)
            {
                continue;
            }

            contextNodes.Add(new ContextSpecNode(
                node.SpecId, node.Kind, node.Layer, latest.Hash, latest.CanonicalText, node.RetiredAt is not null));
        }

        var specIds = contextNodes.Select(n => n.SpecId).ToList();
        var edges = await db.SpecEdges.AsNoTracking()
            .Where(e => e.ProjectId == conversation.ProjectId && e.RetiredAt == null
                && specIds.Contains(e.FromSpecId) && specIds.Contains(e.ToSpecId))
            .Select(e => new ContextSpecEdge(e.Id, e.FromSpecId, e.ToSpecId, e.Kind))
            .ToListAsync(cancellationToken);

        var tail = await db.Turns.AsNoTracking()
            .Where(t => t.ConversationId == conversation.Id)
            .OrderByDescending(t => t.Seq)
            .Take(ConversationTailSize)
            .ToListAsync(cancellationToken);

        return new ContextPack
        {
            ConversationId = conversation.Id,
            ProjectId = conversation.ProjectId,
            Agent = new ContextAgent(agent.Role, agent.Deployment, agent.TeamId, agent.TeamMemberId),
            SpecNeighborhood = contextNodes,
            SpecEdges = edges,
            Standards = [ArchitectPrompt.DefaultStandards],
            ConversationTail = tail.OrderBy(t => t.Seq)
                .Select(t => new ContextTurn(t.Seq, t.Role.ToString().ToLowerInvariant(), t.Content))
                .ToList(),
            Skills = [ArchitectPrompt.ArchitectSkill],
            AssembledAt = DateTimeOffset.UtcNow,
            Attempt = attempt,
        };
    }

    private async Task<string> StoreContextAsync(
        Conversation conversation, ContextPack pack, CancellationToken cancellationToken)
    {
        var artifact = await artifacts.PutForConversationAsync(
            conversation.OrgId,
            conversation.ProjectId,
            conversation.Id,
            ContextPackArtifactType,
            JsonSerializer.Serialize(pack),
            cancellationToken);

        return ArtifactRef.Format(artifact.Id);
    }

    private async Task<List<ModelMessage>> BuildMessagesAsync(string conversationId, CancellationToken cancellationToken)
    {
        var turns = await db.Turns.AsNoTracking()
            .Where(t => t.ConversationId == conversationId)
            .OrderByDescending(t => t.Seq)
            .Take(ConversationTailSize)
            .ToListAsync(cancellationToken);

        return turns.OrderBy(t => t.Seq)
            .Select(t => new ModelMessage(
                t.Role == TurnRole.Assistant ? ModelRole.Assistant : ModelRole.User, t.Content))
            .ToList();
    }

    private async Task<int> NextSeqAsync(string conversationId, CancellationToken cancellationToken)
    {
        var max = await db.Turns.AsNoTracking()
            .Where(t => t.ConversationId == conversationId)
            .Select(t => (int?)t.Seq)
            .MaxAsync(cancellationToken);
        return (max ?? 0) + 1;
    }

    // ---- parsing and validation -------------------------------------------

    private sealed record ProposalValidation(
        SchemaValidationResult Result, SpecDiffDocument? Document, bool NothingProposed);

    /// <summary>
    /// Parses the response envelope. Returns null when the model did not
    /// produce the one JSON object it was asked for — treated as a
    /// validation failure downstream, because from the factory's side an
    /// unparseable answer and a malformed one need exactly the same
    /// handling.
    /// </summary>
    private static ArchitectResponse? ParseResponse(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(ArchitectPrompt.StripFences(text));
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("reply", out var reply)
                || reply.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var settled = root.TryGetProperty("settled", out var s) && s.ValueKind == JsonValueKind.True;
            JsonElement? diff = root.TryGetProperty("diff", out var d) && d.ValueKind == JsonValueKind.Object
                ? d.Clone()
                : null;

            return new ArchitectResponse(reply.GetString()!, settled, diff);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<ProposalValidation> ValidateProposalAsync(
        string projectId, ArchitectResponse? parsed, CancellationToken cancellationToken)
    {
        if (parsed is null)
        {
            return new ProposalValidation(
                new SchemaValidationResult(false, [new SchemaValidationError("(root)",
                    "the response was not a single JSON object with a string \"reply\" property")]),
                null,
                NothingProposed: false);
        }

        if (!parsed.Settled || parsed.Diff is null)
        {
            // Not an error. The conversation simply has not settled, which
            // is the common and correct outcome for most turns.
            return new ProposalValidation(SchemaValidationResult.Valid, null, NothingProposed: true);
        }

        var schema = SpecDiffSchema.Validate(parsed.Diff.Value);
        if (!schema.IsValid)
        {
            return new ProposalValidation(schema, null, NothingProposed: false);
        }

        var document = JsonSerializer.Deserialize<SpecDiffDocument>(parsed.Diff.Value.GetRawText())!;

        // Schema-valid is not the same as applicable: ids have to exist, and
        // exist in this project.
        var references = await translator.ValidateReferencesAsync(projectId, document, cancellationToken);
        return references.IsValid
            ? new ProposalValidation(SchemaValidationResult.Valid, document, NothingProposed: false)
            : new ProposalValidation(references, null, NothingProposed: false);
    }

    private static string Summarize(SpecDiffDocument document)
    {
        var parts = new List<string>();
        if (document.Creates.Count > 0) parts.Add($"creates {document.Creates.Count}");
        if (document.Revises.Count > 0) parts.Add($"revises {document.Revises.Count}");
        if (document.Retires.Count > 0) parts.Add($"retires {document.Retires.Count}");
        if (document.EdgeAdds.Count > 0) parts.Add($"adds {document.EdgeAdds.Count} edge(s)");
        if (document.EdgeRetires.Count > 0) parts.Add($"retires {document.EdgeRetires.Count} edge(s)");
        return parts.Count == 0 ? "no changes" : string.Join(", ", parts);
    }
}
