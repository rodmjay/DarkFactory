using System.Text.Json;
using DarkFactory.Contracts;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data;

public sealed record IntakeSourceInput(string SourceRef, string Title, string Content)
{
    /// <summary>Set when pulled from a corpus server (docs/adr/0038).</summary>
    public string? OriginId { get; init; }
    public string? OriginSha256 { get; init; }
    public string? OriginUpdated { get; init; }
}

public sealed record IntakeStarted(Intake Intake, IReadOnlyList<IntakeSource> Sources);

public sealed record IntakeExtraction(
    IntakeSource Source,
    bool Accepted,
    string Reply,
    IReadOnlyList<IntakeQuestion> OpenQuestions,
    IReadOnlyList<SchemaValidationError> Errors,
    ModelUsage Usage);

public sealed record IntakeSourceOverview(
    IntakeSource Source, int Nodes, int Open, int Answered, int Deferred, int Resolved);

public sealed record IntakeOverview(Intake Intake, IReadOnlyList<IntakeSourceOverview> Sources);

public sealed record IntakeQuestionWithSource(IntakeQuestion Question, string SourceRef);

public sealed record IntakeListing(Intake Intake, int Documents, int Extracted, int Proposed, int OpenQuestions);

/// <summary>One imported document with everything intake knows about it: its text, its draft, its questions.</summary>
public sealed record IntakeSourceDetail(IntakeSource Source, SpecDiffDocument? Draft, IReadOnlyList<IntakeQuestion> Questions);

/// <summary>A hole as the model reported it, after validation.</summary>
public sealed record IntakeHole(
    string? Id, string Kind, string Question, string? Quote, IReadOnlyList<int> Affects,
    IReadOnlyList<IntakeOption>? Options = null);

public sealed record IntakePaths(int QuestionsUpdated, int SourcesProcessed, ModelUsage Usage);

/// <summary>What a person should do next on an import (docs/adr/0039), one step at a time.</summary>
public static class IntakeSteps
{
    /// <summary>Choose a path for <see cref="IntakeNextStep.Question"/>, write one, or defer it.</summary>
    public const string Decide = "decide";

    /// <summary>Answers are in; the document's draft must be rebuilt with them before it can be proposed.</summary>
    public const string Rebuild = "rebuild";

    /// <summary>Nothing blocks the document: put its drafted specs into pending, where approval picks them up.</summary>
    public const string Propose = "propose";

    /// <summary>Nothing is waiting on a person, but a document has not been read yet, or its reading was refused.</summary>
    public const string Extract = "extract";

    /// <summary>Every document is proposed or yielded nothing.</summary>
    public const string Done = "done";
}

/// <summary>
/// <paramref name="Settled"/>: documents proposed, or read and yielding
/// nothing once their questions were closed. <paramref name="WithoutPaths"/>:
/// open questions that have no suggested answers yet.
/// </summary>
public sealed record IntakeProgress(int Documents, int Settled, int OpenQuestions, int WithoutPaths);

public sealed record IntakeNextStep(
    string Step, IntakeSource? Source, IntakeQuestion? Question, int OpenInSource, int Nodes, IntakeProgress Progress);

/// <summary>
/// Corpus intake (docs/adr/0037): extract, find holes, ask, fill, propose.
///
/// Three properties this class exists to guarantee:
/// <list type="bullet">
/// <item>Nothing here writes the graph. The only way out is
/// <see cref="ProposeAsync"/>, which files an ordinary amendment — so
/// imported specifications are approved exactly like conversational ones,
/// and whatever approval becomes (docs/adr/0035) applies to them too.</item>
/// <item>A draft is not proposed while a person still owes it an answer, or
/// while it does not yet reflect one they gave. Either would put a guess in
/// front of a reviewer dressed as a decision.</item>
/// <item>Model output is parsed, never trusted: a draft or a hole that does
/// not validate gets one retry carrying the exact violations, and is then
/// refused rather than stored.</item>
/// </list>
/// </summary>
public sealed class IntakeService(
    DarkFactoryDbContext db,
    IModelGateway gateway,
    IArtifactStore artifacts,
    TeamService teams,
    SpecGraphService specs,
    SpecDiffTranslator translator)
{
    public const string CorpusArtifactType = "IntakeCorpus";
    public const string ContextPackArtifactType = "IntakeContextPack";

    /// <summary>The docs/adr/0032 stage dimension for extraction calls. Not a pipeline stage.</summary>
    public const string StageId = "intake";

    /// <summary>Recorded as the amendment's proposer.</summary>
    public const string ProposedBy = "intake";

    /// <summary>
    /// One document's draft is a few thousand tokens. The whole corpus sits
    /// in the input, so leaving this at the model's ceiling could ask for
    /// more than the context window has left.
    /// </summary>
    public const int DefaultMaxOutputTokens = 32_000;

    private const int MaxQuestionLength = 1000;

    // ---- start -------------------------------------------------------------

    public async Task<IntakeStarted> StartAsync(
        string projectId,
        string name,
        IReadOnlyList<IntakeSourceInput> sources,
        string createdBy,
        CancellationToken cancellationToken = default,
        string? sourceServerId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("An intake needs a name.");
        }
        if (sources.Count == 0)
        {
            throw new InvalidOperationException("An intake needs at least one source document.");
        }
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source.SourceRef) || string.IsNullOrWhiteSpace(source.Title)
                || string.IsNullOrWhiteSpace(source.Content))
            {
                throw new InvalidOperationException(
                    $"Every source needs a source_ref, a title and content; '{source.SourceRef}' is missing one.");
            }
        }
        var duplicate = sources.GroupBy(s => s.SourceRef, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Source '{duplicate.Key}' appears {duplicate.Count()} times in one corpus.");
        }

        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            ?? throw new InvalidOperationException($"No project '{projectId}'.");

        var ordered = sources
            .OrderBy(s => s.SourceRef, StringComparer.Ordinal)
            .Select(s => (Input: s, Sha: SpecGraphService.ComputeHash(s.Content)))
            .ToList();
        var corpusSha = SpecGraphService.ComputeHash(
            string.Join("\n", ordered.Select(o => $"{o.Input.SourceRef} {o.Sha}")));

        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation
        {
            Id = Ulid.NewUlid(),
            ProjectId = project.Id,
            OrgId = project.OrgId,
            Title = $"Intake: {name}",
            CreatedBy = createdBy,
            CreatedAt = now,
            Status = ConversationStatus.Active,
        };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(cancellationToken);

        var corpus = await artifacts.PutForConversationAsync(
            project.OrgId,
            project.Id,
            conversation.Id,
            CorpusArtifactType,
            JsonSerializer.Serialize(ordered.Select(o =>
                new IntakeCorpusDocument(o.Input.SourceRef, o.Input.Title, o.Sha, o.Input.Content))),
            cancellationToken);

        var intake = new Intake
        {
            Id = Ulid.NewUlid(),
            ProjectId = project.Id,
            OrgId = project.OrgId,
            Name = name,
            ConversationId = conversation.Id,
            CorpusRef = ArtifactRef.Format(corpus.Id),
            CorpusSha256 = corpusSha,
            SourceServerId = sourceServerId,
            CreatedBy = createdBy,
            CreatedAt = now,
        };
        db.Intakes.Add(intake);

        var rows = ordered.Select((o, i) => new IntakeSource
        {
            Id = Ulid.NewUlid(),
            IntakeId = intake.Id,
            ProjectId = project.Id,
            OrgId = project.OrgId,
            Seq = i + 1,
            SourceRef = o.Input.SourceRef,
            Title = o.Input.Title,
            Content = o.Input.Content,
            ContentSha256 = o.Sha,
            OriginId = o.Input.OriginId,
            OriginSha256 = o.Input.OriginSha256,
            OriginUpdated = o.Input.OriginUpdated,
            Status = IntakeSourceStatus.Pending,
            DraftRevision = 0,
            CreatedAt = now,
        }).ToList();
        db.IntakeSources.AddRange(rows);

        await db.SaveChangesAsync(cancellationToken);
        return new IntakeStarted(intake, rows);
    }

    // ---- extract -----------------------------------------------------------

    public async Task<IntakeExtraction> ExtractAsync(
        string sourceId, string actorId, CancellationToken cancellationToken = default)
    {
        var source = await db.IntakeSources.SingleOrDefaultAsync(s => s.Id == sourceId, cancellationToken)
            ?? throw new InvalidOperationException($"No intake source '{sourceId}'.");
        EnsureNotProposed(source);

        var intake = await db.Intakes.AsNoTracking().SingleAsync(i => i.Id == source.IntakeId, cancellationToken);
        var corpus = await db.IntakeSources.AsNoTracking()
            .Where(s => s.IntakeId == intake.Id)
            .OrderBy(s => s.Seq)
            .ToListAsync(cancellationToken);
        var questions = await db.IntakeQuestions
            .Where(q => q.SourceId == source.Id)
            .OrderBy(q => q.CreatedAt).ThenBy(q => q.Id)
            .ToListAsync(cancellationToken);
        var agent = await teams.ResolveAsync(source.ProjectId, AssignmentPoints.Conversation, cancellationToken);

        var revision = source.DraftRevision + 1;
        var layers = LayersInUse(corpus.Where(s => s.Id != source.Id));
        var (standards, missingStandards) = await StandardsNamedAsync(source, cancellationToken);
        var prefix = IntakePrompt.CacheablePrefix(corpus, intake.Guidance);
        var system = IntakePrompt.SystemPrompt(source, revision, questions, source.DraftJson, layers, standards, missingStandards);
        var messages = new List<ModelMessage> { new(ModelRole.User, IntakePrompt.Instruction(source)) };

        // ---- attempt 1 ----------------------------------------------------

        var packRef = await StorePackAsync(intake, source, agent, questions, revision, layers, standards, attempt: 1, cancellationToken);
        var completion = await gateway.CompleteAsync(
            Request(agent, prefix, system, messages, CallContext(source, agent, packRef, attempt: 1)),
            cancellationToken);
        var usage = completion.Usage;
        var outcome = await ValidateAsync(source, questions, completion.Text, cancellationToken);

        // ---- attempt 2, only if the first was unusable ---------------------

        if (!outcome.Result.IsValid)
        {
            packRef = await StorePackAsync(intake, source, agent, questions, revision, layers, standards, attempt: 2, cancellationToken);

            var retryMessages = messages.ToList();
            retryMessages.Add(new ModelMessage(ModelRole.Assistant, completion.Text));
            retryMessages.Add(new ModelMessage(ModelRole.User, IntakePrompt.RetryMessage(outcome.Result)));

            var retry = await gateway.CompleteAsync(
                Request(agent, prefix, system, retryMessages, CallContext(source, agent, packRef, attempt: 2, retried: true)),
                cancellationToken);

            usage = usage with
            {
                InputTokens = usage.InputTokens + retry.Usage.InputTokens,
                OutputTokens = usage.OutputTokens + retry.Usage.OutputTokens,
                CachedInputTokens = usage.CachedInputTokens + retry.Usage.CachedInputTokens,
                CacheWriteInputTokens = usage.CacheWriteInputTokens + retry.Usage.CacheWriteInputTokens,
                ThinkingTokens = usage.ThinkingTokens + retry.Usage.ThinkingTokens,
            };
            outcome = await ValidateAsync(source, questions, retry.Text, cancellationToken);
        }

        source.ContextPackRef = packRef;

        if (!outcome.Result.IsValid)
        {
            // A refused extraction leaves the previous draft, if any, exactly
            // as it was: it was valid, and it is still what was last agreed
            // to be a faithful reading.
            source.Failure = outcome.Result.AsBulletList();
            if (source.DraftRevision == 0)
            {
                source.Status = IntakeSourceStatus.Failed;
            }
            await db.SaveChangesAsync(cancellationToken);

            return new IntakeExtraction(
                source,
                Accepted: false,
                outcome.Reply ?? "",
                questions.Where(q => q.Status == IntakeQuestionStatus.Open).ToList(),
                outcome.Result.Errors,
                usage);
        }

        var now = DateTimeOffset.UtcNow;
        source.DraftJson = JsonSerializer.Serialize(Cited(outcome.Draft!, source.SourceRef));
        source.DraftRevision = revision;
        source.Status = IntakeSourceStatus.Extracted;
        source.Failure = null;
        source.ExtractedAt = now;

        var carried = outcome.Holes.Where(h => h.Id is not null).ToDictionary(h => h.Id!, StringComparer.Ordinal);
        foreach (var question in questions)
        {
            if (question.Status == IntakeQuestionStatus.Open)
            {
                if (carried.TryGetValue(question.Id, out var hole))
                {
                    // Still a hole. The draft may have been renumbered, so
                    // what it affects is re-read from this extraction.
                    question.AffectsJson = JsonSerializer.Serialize(hole.Affects);
                    if (hole.Options is { Count: > 0 } offered)
                    {
                        question.OptionsJson = JsonSerializer.Serialize(offered);
                    }
                }
                else
                {
                    question.Status = IntakeQuestionStatus.Resolved;
                    question.IncorporatedInRevision = revision;
                }
            }
            else if (question.Status is IntakeQuestionStatus.Answered or IntakeQuestionStatus.Deferred
                && question.IncorporatedInRevision is null)
            {
                question.IncorporatedInRevision = revision;
            }
        }

        foreach (var hole in outcome.Holes.Where(h => h.Id is null))
        {
            db.IntakeQuestions.Add(new IntakeQuestion
            {
                Id = Ulid.NewUlid(),
                IntakeId = source.IntakeId,
                SourceId = source.Id,
                ProjectId = source.ProjectId,
                OrgId = source.OrgId,
                Kind = hole.Kind,
                Question = hole.Question,
                Quote = hole.Quote,
                AffectsJson = JsonSerializer.Serialize(hole.Affects),
                OptionsJson = hole.Options is { Count: > 0 } offered ? JsonSerializer.Serialize(offered) : null,
                Status = IntakeQuestionStatus.Open,
                RaisedInRevision = revision,
                CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        var open = await db.IntakeQuestions.AsNoTracking()
            .Where(q => q.SourceId == source.Id && q.Status == IntakeQuestionStatus.Open)
            .OrderBy(q => q.CreatedAt).ThenBy(q => q.Id)
            .ToListAsync(cancellationToken);

        return new IntakeExtraction(source, Accepted: true, outcome.Reply ?? "", open, [], usage);
    }

    // ---- guidance ----------------------------------------------------------

    /// <summary>
    /// Records the project owner's standing decisions about the import.
    /// Replaces rather than appends, so what every later extraction sees is
    /// one current statement; empty clears it. Drafts already made are not
    /// redone — re-extracting one is how it takes new guidance.
    /// </summary>
    public async Task<Intake> GuideAsync(string intakeId, string? guidance, CancellationToken cancellationToken = default)
    {
        var intake = await db.Intakes.SingleOrDefaultAsync(i => i.Id == intakeId, cancellationToken)
            ?? throw new InvalidOperationException($"No intake '{intakeId}'.");
        intake.Guidance = string.IsNullOrWhiteSpace(guidance) ? null : guidance.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return intake;
    }

    // ---- answer, defer -----------------------------------------------------

    public async Task<IntakeQuestion> AnswerAsync(
        string questionId, string answer, string actorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            throw new InvalidOperationException("An answer cannot be empty. To leave a question open on purpose, defer it.");
        }

        var question = await LoadAnswerableAsync(questionId, cancellationToken);
        question.Status = IntakeQuestionStatus.Answered;
        question.Answer = answer.Trim();
        question.AnsweredBy = actorId;
        question.AnsweredAt = DateTimeOffset.UtcNow;
        question.IncorporatedInRevision = null;

        await db.SaveChangesAsync(cancellationToken);
        return question;
    }

    /// <summary>
    /// Leaves a question open deliberately. It stops blocking the proposal,
    /// and later extractions are told not to guess at it — which is the
    /// difference between deferring a question and ignoring it.
    /// </summary>
    public async Task<IntakeQuestion> DeferAsync(
        string questionId, string reason, string actorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("Say why the question is being left open; the next reader will want to know.");
        }

        var question = await LoadAnswerableAsync(questionId, cancellationToken);
        question.Status = IntakeQuestionStatus.Deferred;
        question.Answer = reason.Trim();
        question.AnsweredBy = actorId;
        question.AnsweredAt = DateTimeOffset.UtcNow;
        question.IncorporatedInRevision = null;

        await db.SaveChangesAsync(cancellationToken);
        return question;
    }

    private async Task<IntakeQuestion> LoadAnswerableAsync(string questionId, CancellationToken cancellationToken)
    {
        var question = await db.IntakeQuestions.SingleOrDefaultAsync(q => q.Id == questionId, cancellationToken)
            ?? throw new InvalidOperationException($"No intake question '{questionId}'.");

        var source = await db.IntakeSources.AsNoTracking().SingleAsync(s => s.Id == question.SourceId, cancellationToken);
        EnsureNotProposed(source);

        // An answer not yet built into a draft can still be changed; one
        // that has been is history, and changing it would leave the draft
        // claiming to reflect something nobody said any more.
        var changeable = question.Status == IntakeQuestionStatus.Open
            || (question.Status is IntakeQuestionStatus.Answered or IntakeQuestionStatus.Deferred
                && question.IncorporatedInRevision is null);
        if (!changeable)
        {
            throw new InvalidOperationException(
                $"Question {question.Id} is {question.Status.ToString().ToLowerInvariant()} " +
                $"and was settled in draft revision {question.IncorporatedInRevision}. It can no longer be answered here.");
        }

        return question;
    }

    // ---- propose -----------------------------------------------------------

    public async Task<Amendment> ProposeAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        var source = await db.IntakeSources.SingleOrDefaultAsync(s => s.Id == sourceId, cancellationToken)
            ?? throw new InvalidOperationException($"No intake source '{sourceId}'.");
        EnsureNotProposed(source);

        if (source.Status == IntakeSourceStatus.Pending)
        {
            throw new InvalidOperationException($"'{source.SourceRef}' has not been extracted yet. Run df.intake.extract first.");
        }
        if (source.Status == IntakeSourceStatus.Failed)
        {
            throw new InvalidOperationException(
                $"'{source.SourceRef}' has no accepted draft; its extraction was refused:\n{source.Failure}");
        }

        var questions = await db.IntakeQuestions.AsNoTracking()
            .Where(q => q.SourceId == source.Id)
            .ToListAsync(cancellationToken);

        var open = questions.Count(q => q.Status == IntakeQuestionStatus.Open);
        if (open > 0)
        {
            throw new InvalidOperationException(
                $"'{source.SourceRef}' has {open} open question(s). Answer or defer each one before its draft can be proposed.");
        }

        var unincorporated = questions.Count(q =>
            q.Status == IntakeQuestionStatus.Answered && q.IncorporatedInRevision is null);
        if (unincorporated > 0)
        {
            throw new InvalidOperationException(
                $"'{source.SourceRef}' has {unincorporated} answer(s) its draft does not reflect yet. " +
                "Run df.intake.extract so the draft is rebuilt with them, then propose.");
        }

        var draft = JsonSerializer.Deserialize<SpecDiffDocument>(source.DraftJson!)!;
        if (draft.IsEmpty)
        {
            throw new InvalidOperationException($"'{source.SourceRef}' yielded no specifications, so there is nothing to propose.");
        }

        var intake = await db.Intakes.AsNoTracking().SingleAsync(i => i.Id == source.IntakeId, cancellationToken);
        var amendment = await specs.ProposeAsync(
            source.ProjectId,
            source.OrgId,
            intake.ConversationId,
            turnId: null,
            ProposedBy,
            SpecDiffTranslator.ToSpecDiff(draft),
            cancellationToken);

        source.AmendmentId = amendment.Id;
        source.Status = IntakeSourceStatus.Proposed;
        await db.SaveChangesAsync(cancellationToken);

        return amendment;
    }

    // ---- reads -------------------------------------------------------------

    public async Task<IntakeOverview> GetAsync(string intakeId, CancellationToken cancellationToken = default)
    {
        var intake = await db.Intakes.AsNoTracking().SingleOrDefaultAsync(i => i.Id == intakeId, cancellationToken)
            ?? throw new InvalidOperationException($"No intake '{intakeId}'.");

        var sources = await db.IntakeSources.AsNoTracking()
            .Where(s => s.IntakeId == intakeId)
            .OrderBy(s => s.Seq)
            .ToListAsync(cancellationToken);
        var counts = await db.IntakeQuestions.AsNoTracking()
            .Where(q => q.IntakeId == intakeId)
            .GroupBy(q => new { q.SourceId, q.Status })
            .Select(g => new { g.Key.SourceId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int Count(string sourceId, IntakeQuestionStatus status) =>
            counts.Where(c => c.SourceId == sourceId && c.Status == status).Sum(c => c.Count);

        return new IntakeOverview(intake, sources.Select(s => new IntakeSourceOverview(
            s,
            s.DraftJson is null ? 0 : JsonSerializer.Deserialize<SpecDiffDocument>(s.DraftJson)!.Creates.Count,
            Count(s.Id, IntakeQuestionStatus.Open),
            Count(s.Id, IntakeQuestionStatus.Answered),
            Count(s.Id, IntakeQuestionStatus.Deferred),
            Count(s.Id, IntakeQuestionStatus.Resolved))).ToList());
    }

    /// <summary>
    /// The one thing a person should do next (docs/adr/0039), so building
    /// the specs is a sequence of decisions the factory puts in front of
    /// them rather than questions they have to know to ask. Documents go in
    /// corpus order — the scope document sorts first, and settling scope
    /// first is what makes every later answer cheaper. Within a document the
    /// order is the order ProposeAsync enforces: close every question, rebuild
    /// the draft with the answers, then propose. A document still waiting to
    /// be read never blocks one that is ready.
    /// </summary>
    public async Task<IntakeNextStep> NextAsync(string intakeId, CancellationToken cancellationToken = default)
    {
        var overview = await GetAsync(intakeId, cancellationToken);
        var questions = await db.IntakeQuestions.AsNoTracking()
            .Where(q => q.IntakeId == intakeId)
            .OrderBy(q => q.CreatedAt).ThenBy(q => q.Id)
            .ToListAsync(cancellationToken);

        bool Unincorporated(string sourceId) => questions.Any(q =>
            q.SourceId == sourceId && q.Status == IntakeQuestionStatus.Answered && q.IncorporatedInRevision is null);
        bool Settled(IntakeSourceOverview s) =>
            s.Source.Status == IntakeSourceStatus.Proposed
            || (s.Source.Status == IntakeSourceStatus.Extracted && s.Open == 0 && s.Nodes == 0 && !Unincorporated(s.Source.Id));

        var open = questions.Where(q => q.Status == IntakeQuestionStatus.Open).ToList();
        var progress = new IntakeProgress(
            overview.Sources.Count,
            overview.Sources.Count(Settled),
            open.Count,
            open.Count(q => q.OptionsJson is null));

        foreach (var s in overview.Sources.Where(s => s.Source.Status == IntakeSourceStatus.Extracted))
        {
            if (s.Open > 0)
            {
                return new IntakeNextStep(IntakeSteps.Decide, s.Source,
                    open.First(q => q.SourceId == s.Source.Id), s.Open, s.Nodes, progress);
            }
            if (Unincorporated(s.Source.Id))
            {
                return new IntakeNextStep(IntakeSteps.Rebuild, s.Source, null, 0, s.Nodes, progress);
            }
            if (s.Nodes > 0)
            {
                return new IntakeNextStep(IntakeSteps.Propose, s.Source, null, 0, s.Nodes, progress);
            }
        }

        var unread = overview.Sources.FirstOrDefault(s =>
            s.Source.Status is IntakeSourceStatus.Pending or IntakeSourceStatus.Failed);
        return unread is null
            ? new IntakeNextStep(IntakeSteps.Done, null, null, 0, 0, progress)
            : new IntakeNextStep(IntakeSteps.Extract, unread.Source, null, 0, 0, progress);
    }

    /// <summary>
    /// One imported document as the spec graph screen shows it: the text it
    /// was imported with, the current draft, and every question raised about
    /// it — so "where are the specs" has an answer before anything is approved.
    /// </summary>
    public async Task<IntakeSourceDetail> GetSourceAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        var source = await db.IntakeSources.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sourceId, cancellationToken)
            ?? throw new InvalidOperationException($"No intake source '{sourceId}'.");

        var questions = await db.IntakeQuestions.AsNoTracking()
            .Where(q => q.SourceId == sourceId)
            .OrderBy(q => q.CreatedAt).ThenBy(q => q.Id)
            .ToListAsync(cancellationToken);

        var draft = source.DraftJson is null ? null : JsonSerializer.Deserialize<SpecDiffDocument>(source.DraftJson);
        return new IntakeSourceDetail(source, draft, questions);
    }

    /// <summary>A project's imports, oldest first, with how far along each is.</summary>
    public async Task<IReadOnlyList<IntakeListing>> ListAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var intakes = await db.Intakes.AsNoTracking()
            .Where(i => i.ProjectId == projectId)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
        var ids = intakes.Select(i => i.Id).ToList();

        var sources = await db.IntakeSources.AsNoTracking()
            .Where(s => ids.Contains(s.IntakeId))
            .Select(s => new { s.IntakeId, s.Status })
            .ToListAsync(cancellationToken);
        var open = await db.IntakeQuestions.AsNoTracking()
            .Where(q => ids.Contains(q.IntakeId) && q.Status == IntakeQuestionStatus.Open)
            .Select(q => q.IntakeId)
            .ToListAsync(cancellationToken);

        return intakes.Select(i => new IntakeListing(
            i,
            sources.Count(s => s.IntakeId == i.Id),
            sources.Count(s => s.IntakeId == i.Id && s.Status is IntakeSourceStatus.Extracted or IntakeSourceStatus.Proposed),
            sources.Count(s => s.IntakeId == i.Id && s.Status == IntakeSourceStatus.Proposed),
            open.Count(id => id == i.Id))).ToList();
    }

    public async Task<IReadOnlyList<IntakeQuestionWithSource>> QuestionsAsync(
        string intakeId,
        IntakeQuestionStatus? status = null,
        string? sourceId = null,
        CancellationToken cancellationToken = default)
    {
        var query =
            from q in db.IntakeQuestions.AsNoTracking()
            join s in db.IntakeSources.AsNoTracking() on q.SourceId equals s.Id
            where q.IntakeId == intakeId
            select new { q, s.SourceRef, s.Seq };

        if (status is { } wanted)
        {
            query = query.Where(x => x.q.Status == wanted);
        }
        if (sourceId is not null)
        {
            query = query.Where(x => x.q.SourceId == sourceId);
        }

        var rows = await query
            .OrderBy(x => x.Seq).ThenBy(x => x.q.CreatedAt).ThenBy(x => x.q.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(x => new IntakeQuestionWithSource(x.q, x.SourceRef)).ToList();
    }

    // ---- helpers -----------------------------------------------------------

    private static void EnsureNotProposed(IntakeSource source)
    {
        if (source.Status == IntakeSourceStatus.Proposed)
        {
            throw new InvalidOperationException(
                $"'{source.SourceRef}' was already proposed as amendment {source.AmendmentId}. " +
                "From here it changes through the conversation, like any other specification.");
        }
    }

    /// <summary>
    /// The standards the document names in its front matter, from the
    /// project's ingested standards (docs/adr/0023). Named but not ingested
    /// is reported, not hidden: a document governed by a rule nobody can
    /// read is a gap worth seeing.
    /// </summary>
    private async Task<(IReadOnlyList<StandardsIndexEntry> Found, IReadOnlyList<string> Missing)> StandardsNamedAsync(
        IntakeSource source, CancellationToken cancellationToken)
    {
        var named = StandardsIngestService.NamedIn(source.Content).ToList();
        if (named.Count == 0)
        {
            return ([], []);
        }

        // This org's live standards servers only: standards are org-wide
        // (docs/adr/0038), so without the join an id shared by another
        // org's corpus would be as good a match as this one's. Where two of
        // the org's servers both carry an id, the latest ingest wins.
        var rows = await (
                from standard in db.StandardsIndex.AsNoTracking()
                join server in db.Servers.AsNoTracking() on standard.ServerId equals server.Id
                where server.OrgId == source.OrgId
                    && server.RemovedAt == null
                    && named.Contains(standard.ChunkRef)
                    && (standard.ProjectId == source.ProjectId || standard.ProjectId == null)
                select standard)
            .ToListAsync(cancellationToken);

        var found = rows
            .GroupBy(s => s.ChunkRef, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(s => s.IngestedAt).First())
            .OrderBy(s => named.IndexOf(s.ChunkRef))
            .ToList();
        var missing = named.Except(found.Select(s => s.ChunkRef), StringComparer.Ordinal).ToList();
        return (found, missing);
    }

    /// <summary>
    /// Layers are open (docs/adr/0016), and forty independent extractions
    /// would otherwise name the same part of the product forty ways. Each
    /// one is shown what the others chose.
    /// </summary>
    private static IReadOnlyList<string> LayersInUse(IEnumerable<IntakeSource> others) =>
        others
            .Where(s => s.DraftJson is not null)
            .SelectMany(s => JsonSerializer.Deserialize<SpecDiffDocument>(s.DraftJson!)!.Creates.Select(c => c.Layer))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Every node names the document it came from. After intake the factory
    /// is authoritative and the source may be edited or deleted, so this is
    /// the only place "where did this rule come from" stays answerable.
    /// Done by the factory rather than trusted to the prompt, because the
    /// prompt asks and this guarantees.
    /// </summary>
    private static SpecDiffDocument Cited(SpecDiffDocument draft, string sourceRef) => draft with
    {
        Creates = draft.Creates.Select(c => c with { Rationale = Cite(sourceRef, c.Rationale) }).ToList(),
    };

    private static string Cite(string sourceRef, string? rationale)
    {
        var cited = string.IsNullOrWhiteSpace(rationale)
            ? $"From {sourceRef}."
            : rationale.Contains(sourceRef, StringComparison.Ordinal)
                ? rationale
                : $"From {sourceRef}. {rationale}";

        // The specdiff schema caps a rationale at 2000 characters, and the
        // citation must not be what pushes a valid one over.
        return cited.Length <= 2000 ? cited : cited[..1999] + "…";
    }

    private static ModelRequest Request(
        ResolvedAgent agent, string prefix, string system, IReadOnlyList<ModelMessage> messages, ModelCallContext context) =>
        new(
            agent.Deployment,
            system,
            messages,
            MaxOutputTokens: agent.MaxOutputTokens ?? DefaultMaxOutputTokens,
            Context: context,
            CacheableSystemPrefix: prefix);

    private static ModelCallContext CallContext(
        IntakeSource source, ResolvedAgent agent, string packRef, int attempt, bool? retried = null) =>
        new()
        {
            OrgId = source.OrgId,
            ProjectId = source.ProjectId,
            StageId = StageId,
            TaskId = source.Id,
            Attempt = attempt,
            TeamMemberId = agent.TeamMemberId,
            ContextPackRef = packRef,
            PromptTemplateVersion = IntakePrompt.TemplateVersion,
            Retried = retried,
        };

    private async Task<string> StorePackAsync(
        Intake intake,
        IntakeSource source,
        ResolvedAgent agent,
        IReadOnlyList<IntakeQuestion> questions,
        int revision,
        IReadOnlyList<string> layers,
        IReadOnlyList<StandardsIndexEntry> standards,
        int attempt,
        CancellationToken cancellationToken)
    {
        var pack = new IntakeContextPack
        {
            IntakeId = intake.Id,
            SourceId = source.Id,
            SourceRef = source.SourceRef,
            ProjectId = source.ProjectId,
            CorpusRef = intake.CorpusRef,
            CorpusSha256 = intake.CorpusSha256,
            Agent = new ContextAgent(agent.Role, agent.Deployment, agent.TeamId, agent.TeamMemberId),
            Revision = revision,
            Questions = questions
                .Select(q => new ContextIntakeQuestion(q.Id, q.Kind, q.Status.ToString().ToLowerInvariant(), q.Question, q.Answer))
                .ToList(),
            PreviousDraft = source.DraftJson,
            LayersInUse = layers,
            Guidance = intake.Guidance,
            Standards = standards.Select(s => $"{s.SourceRef}@{s.Updated}").ToList(),
            Skills = [IntakePrompt.IntakeSkill],
            AssembledAt = DateTimeOffset.UtcNow,
            Attempt = attempt,
        };

        var artifact = await artifacts.PutForConversationAsync(
            intake.OrgId, intake.ProjectId, intake.ConversationId, ContextPackArtifactType,
            JsonSerializer.Serialize(pack), cancellationToken);
        return ArtifactRef.Format(artifact.Id);
    }

    // ---- parsing and validation -------------------------------------------

    private sealed record Outcome(
        SchemaValidationResult Result, string? Reply, SpecDiffDocument? Draft, IReadOnlyList<IntakeHole> Holes);

    private async Task<Outcome> ValidateAsync(
        IntakeSource source, IReadOnlyList<IntakeQuestion> questions, string text, CancellationToken cancellationToken)
    {
        static Outcome Refused(IReadOnlyList<SchemaValidationError> errors, string? reply = null) =>
            new(new SchemaValidationResult(false, errors), reply, null, []);

        JsonElement root;
        try
        {
            using var parsed = JsonDocument.Parse(ArchitectPrompt.StripFences(text));
            root = parsed.RootElement.Clone();
        }
        catch (JsonException)
        {
            return Refused([new SchemaValidationError("(root)", "the response was not a single JSON object")]);
        }

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("reply", out var replyElement) || replyElement.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("draft", out var draftElement) || draftElement.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("holes", out var holesElement) || holesElement.ValueKind != JsonValueKind.Array)
        {
            return Refused([new SchemaValidationError("(root)",
                "the response must be one JSON object with a string \"reply\", an object \"draft\" and an array \"holes\"")]);
        }

        var reply = replyElement.GetString();

        var schema = SpecDiffSchema.Validate(draftElement);
        if (!schema.IsValid)
        {
            return Refused(schema.Errors.Select(e => e with { Location = "/draft" + Rooted(e.Location) }).ToList(), reply);
        }

        var draft = JsonSerializer.Deserialize<SpecDiffDocument>(draftElement.GetRawText())!;
        var errors = new List<SchemaValidationError>();

        if (draft.Revises.Count > 0 || draft.Retires.Count > 0 || draft.EdgeRetires.Count > 0)
        {
            errors.Add(new SchemaValidationError("/draft",
                "an intake draft only creates nodes and the edges between them; \"revises\", \"retires\" and " +
                "\"edge_retires\" must be empty"));
        }

        // An empty draft is a legitimate reading of a document that
        // specifies nothing; the translator's "empty diff" rule is for a
        // conversation, where proposing nothing should mean saying nothing.
        if (!draft.IsEmpty)
        {
            var references = await translator.ValidateReferencesAsync(source.ProjectId, draft, cancellationToken);
            errors.AddRange(references.Errors.Select(e => e with { Location = "/draft" + Rooted(e.Location) }));
        }

        var open = questions.Where(q => q.Status == IntakeQuestionStatus.Open)
            .Select(q => q.Id).ToHashSet(StringComparer.Ordinal);
        var holes = new List<IntakeHole>();
        var index = 0;
        foreach (var element in holesElement.EnumerateArray())
        {
            var at = $"/holes/{index++}";
            if (element.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new SchemaValidationError(at, "each hole must be an object"));
                continue;
            }

            string? Str(string name) =>
                element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            var kind = Str("kind");
            var question = Str("question")?.Trim();
            var quote = Str("quote")?.Trim();
            var id = Str("id");

            if (kind is null || !IntakeHoleKinds.All.Contains(kind))
            {
                errors.Add(new SchemaValidationError($"{at}/kind",
                    $"'{kind}' is not a hole kind; use one of: {string.Join(", ", IntakeHoleKinds.All.Order())}"));
            }
            if (string.IsNullOrEmpty(question))
            {
                errors.Add(new SchemaValidationError($"{at}/question", "a hole needs a question"));
            }
            else if (question.Length > MaxQuestionLength)
            {
                errors.Add(new SchemaValidationError($"{at}/question",
                    $"a question must be at most {MaxQuestionLength} characters; ask one thing"));
            }
            if (id is not null && !open.Contains(id))
            {
                errors.Add(new SchemaValidationError($"{at}/id",
                    $"'{id}' is not an open question about this document. Use null for a new hole; " +
                    "an answered or deferred question must not be raised again"));
            }

            var affects = new List<int>();
            if (element.TryGetProperty("affects", out var affectsElement) && affectsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in affectsElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var i) && i >= 0 && i < draft.Creates.Count)
                    {
                        affects.Add(i);
                    }
                    else
                    {
                        errors.Add(new SchemaValidationError($"{at}/affects",
                            $"{item.GetRawText()} is not an index into draft.creates, which has {draft.Creates.Count} node(s)"));
                    }
                }
            }
            else if (element.TryGetProperty("affects", out _))
            {
                errors.Add(new SchemaValidationError($"{at}/affects", "affects must be an array of indices"));
            }

            IReadOnlyList<IntakeOption>? options = null;
            if (element.TryGetProperty("options", out var optionsElement) && optionsElement.ValueKind != JsonValueKind.Null)
            {
                options = ParseOptions(optionsElement, $"{at}/options", errors);
            }

            if (kind is not null && question is not null)
            {
                holes.Add(new IntakeHole(id, kind, question, string.IsNullOrEmpty(quote) ? null : quote, affects, options));
            }
        }

        if (holes.Where(h => h.Id is not null).GroupBy(h => h.Id).Any(g => g.Count() > 1))
        {
            errors.Add(new SchemaValidationError("/holes", "the same open question was returned more than once"));
        }

        return errors.Count == 0
            ? new Outcome(SchemaValidationResult.Valid, reply, draft, holes)
            : Refused(errors, reply);
    }

    /// <summary>
    /// Options as decision.schema.json has them: 2 to 4, each a short label
    /// and an optional consequence, at most one recommended. Ids are the
    /// factory's to assign (o1, o2…) — a model has no reason to choose them.
    /// Violations are added to <paramref name="errors"/>; null is returned
    /// when there were any.
    /// </summary>
    internal static IReadOnlyList<IntakeOption>? ParseOptions(JsonElement element, string at, List<SchemaValidationError> errors)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new SchemaValidationError(at, "options must be an array"));
            return null;
        }

        var options = new List<IntakeOption>();
        var before = errors.Count;
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            var where = $"{at}/{index++}";
            var label = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("label", out var l)
                && l.ValueKind == JsonValueKind.String ? l.GetString()?.Trim() : null;
            if (string.IsNullOrEmpty(label) || label.Length > 120)
            {
                errors.Add(new SchemaValidationError($"{where}/label", "each option needs a label of at most 120 characters"));
                continue;
            }

            var consequence = item.TryGetProperty("consequence", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()?.Trim()
                : null;
            if (consequence is { Length: > 400 })
            {
                errors.Add(new SchemaValidationError($"{where}/consequence", "a consequence must be at most 400 characters"));
                continue;
            }

            var recommended = item.TryGetProperty("recommended", out var r) && r.ValueKind == JsonValueKind.True;
            options.Add(new IntakeOption($"o{options.Count + 1}", label, string.IsNullOrEmpty(consequence) ? null : consequence, recommended));
        }

        if (errors.Count == before && (options.Count < 2 || options.Count > 4))
        {
            errors.Add(new SchemaValidationError(at, $"give 2 to 4 options, not {options.Count}"));
        }
        if (errors.Count == before && options.Count(o => o.Recommended) > 1)
        {
            errors.Add(new SchemaValidationError(at, "at most one option may be recommended"));
        }

        return errors.Count == before ? options : null;
    }

    // ---- paths -------------------------------------------------------------

    /// <summary>
    /// Offers paths for every open question that has none (docs/adr/0039):
    /// one model call per document, shown the document, the import's scope
    /// document and its guidance, returning 2–4 options per question with
    /// what each commits to. Suggestions, not answers — a person still
    /// chooses, or writes their own.
    /// </summary>
    public async Task<IntakePaths> SuggestPathsAsync(string intakeId, CancellationToken cancellationToken = default)
    {
        var intake = await db.Intakes.AsNoTracking().SingleOrDefaultAsync(i => i.Id == intakeId, cancellationToken)
            ?? throw new InvalidOperationException($"No intake '{intakeId}'.");

        var waiting = await db.IntakeQuestions
            .Where(q => q.IntakeId == intakeId && q.Status == IntakeQuestionStatus.Open && q.OptionsJson == null)
            .OrderBy(q => q.CreatedAt).ThenBy(q => q.Id)
            .ToListAsync(cancellationToken);
        if (waiting.Count == 0)
        {
            return new IntakePaths(0, 0, new ModelUsage(0, 0));
        }

        // The first document in corpus order sets scope (a northstar sorts
        // first by its number); every question is answered against it.
        var scope = await db.IntakeSources.AsNoTracking()
            .Where(s => s.IntakeId == intakeId)
            .OrderBy(s => s.Seq)
            .FirstAsync(cancellationToken);
        var agent = await teams.ResolveAsync(intake.ProjectId, AssignmentPoints.Conversation, cancellationToken);

        var updated = 0;
        var processed = 0;
        var usage = new ModelUsage(0, 0);

        foreach (var group in waiting.GroupBy(q => q.SourceId))
        {
            var source = await db.IntakeSources.AsNoTracking().SingleAsync(s => s.Id == group.Key, cancellationToken);
            var questions = group.ToList();
            var system = IntakePrompt.PathsPrompt(intake.Guidance, scope, source, questions);
            var messages = new List<ModelMessage> { new(ModelRole.User, IntakePrompt.PathsInstruction) };

            var packRef = await artifacts.PutForConversationAsync(
                intake.OrgId, intake.ProjectId, intake.ConversationId, "IntakePathsPack",
                JsonSerializer.Serialize(new
                {
                    intake_id = intake.Id,
                    source_id = source.Id,
                    scope_source_id = scope.Id,
                    guidance = intake.Guidance,
                    question_ids = questions.Select(q => q.Id),
                    template = IntakePrompt.PathsTemplateVersion,
                }),
                cancellationToken);
            var context = new ModelCallContext
            {
                OrgId = intake.OrgId,
                ProjectId = intake.ProjectId,
                StageId = StageId,
                TaskId = source.Id,
                TeamMemberId = agent.TeamMemberId,
                ContextPackRef = ArtifactRef.Format(packRef.Id),
                PromptTemplateVersion = IntakePrompt.PathsTemplateVersion,
            };

            var completion = await gateway.CompleteAsync(
                new ModelRequest(agent.Deployment, system, messages, MaxOutputTokens: 8_000, Context: context),
                cancellationToken);
            usage = Add(usage, completion.Usage);
            var (paths, errors) = ParsePaths(completion.Text, questions);

            if (errors.Count > 0)
            {
                var retry = messages.ToList();
                retry.Add(new ModelMessage(ModelRole.Assistant, completion.Text));
                retry.Add(new ModelMessage(ModelRole.User,
                    IntakePrompt.RetryMessage(new SchemaValidationResult(false, errors))));
                var again = await gateway.CompleteAsync(
                    new ModelRequest(agent.Deployment, system, retry, MaxOutputTokens: 8_000,
                        Context: context with { Attempt = 2, Retried = true }),
                    cancellationToken);
                usage = Add(usage, again.Usage);
                (paths, _) = ParsePaths(again.Text, questions);
            }

            // Whatever validated is kept; a question still without paths is
            // answered in one's own words, which is always allowed.
            foreach (var question in questions)
            {
                if (paths.TryGetValue(question.Id, out var options))
                {
                    question.OptionsJson = JsonSerializer.Serialize(options);
                    updated++;
                }
            }
            processed++;
            await db.SaveChangesAsync(cancellationToken);
        }

        return new IntakePaths(updated, processed, usage);
    }

    private static (Dictionary<string, IReadOnlyList<IntakeOption>> Paths, List<SchemaValidationError> Errors) ParsePaths(
        string text, IReadOnlyList<IntakeQuestion> questions)
    {
        var errors = new List<SchemaValidationError>();
        var paths = new Dictionary<string, IReadOnlyList<IntakeOption>>(StringComparer.Ordinal);
        var asked = questions.Select(q => q.Id).ToHashSet(StringComparer.Ordinal);

        try
        {
            using var document = JsonDocument.Parse(ArchitectPrompt.StripFences(text));
            if (!document.RootElement.TryGetProperty("paths", out var array) || array.ValueKind != JsonValueKind.Array)
            {
                errors.Add(new SchemaValidationError("(root)", "reply with one JSON object with a \"paths\" array"));
                return (paths, errors);
            }

            var index = 0;
            foreach (var item in array.EnumerateArray())
            {
                var at = $"/paths/{index++}";
                var id = item.TryGetProperty("question_id", out var q) && q.ValueKind == JsonValueKind.String ? q.GetString() : null;
                if (id is null || !asked.Contains(id))
                {
                    errors.Add(new SchemaValidationError($"{at}/question_id", $"'{id}' is not one of the questions asked"));
                    continue;
                }
                if (!item.TryGetProperty("options", out var options))
                {
                    errors.Add(new SchemaValidationError($"{at}/options", "each entry needs options"));
                    continue;
                }

                var parsed = ParseOptions(options, $"{at}/options", errors);
                if (parsed is not null)
                {
                    paths[id] = parsed;
                }
            }

            foreach (var missing in asked.Where(id => !paths.ContainsKey(id)))
            {
                if (!errors.Any(e => e.Message.Contains(missing, StringComparison.Ordinal)))
                {
                    errors.Add(new SchemaValidationError("/paths", $"no options were given for question '{missing}'"));
                }
            }
        }
        catch (JsonException)
        {
            errors.Add(new SchemaValidationError("(root)", "the response was not a single JSON object"));
        }

        return (paths, errors);
    }

    private static ModelUsage Add(ModelUsage a, ModelUsage b) => a with
    {
        InputTokens = a.InputTokens + b.InputTokens,
        OutputTokens = a.OutputTokens + b.OutputTokens,
        CachedInputTokens = a.CachedInputTokens + b.CachedInputTokens,
        CacheWriteInputTokens = a.CacheWriteInputTokens + b.CacheWriteInputTokens,
        ThinkingTokens = a.ThinkingTokens + b.ThinkingTokens,
    };

    private static string Rooted(string location) =>
        location.StartsWith('/') ? location : location == "(root)" ? "" : "/" + location;
}
