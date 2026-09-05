using System.Text.Json;
using System.Text.Json.Serialization;
using DarkFactory.Contracts;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data;

// ADR-0021's rendering vocabulary, as much of it as this slice emits. The
// full versioned vocabulary with per-type schemas is step 4; these two
// types are what a conversational turn actually produces.

[JsonDerivedType(typeof(MarkdownPayload), "markdown")]
[JsonDerivedType(typeof(SpecDiffPayload), "spec_diff")]
public abstract record RenderPayload
{
    [JsonPropertyName("type")] public abstract string Type { get; }
}

public sealed record MarkdownPayload(string Text) : RenderPayload
{
    public override string Type => "markdown";
}

/// <summary>An amendment awaiting approval, rendered as a diff (docs/adr/0017, docs/adr/0021).</summary>
public sealed record SpecDiffPayload(string AmendmentId, SpecDiffDocument Diff, string Summary) : RenderPayload
{
    public override string Type => "spec_diff";
}

public sealed record ConversationTurnResult(
    string TurnId,
    IReadOnlyList<RenderPayload> Payloads,
    string? AmendmentId,
    string ContextRef,
    ModelUsage Usage);

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
