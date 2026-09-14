using System.ComponentModel;
using System.Text.Json;
using DarkFactory.Contracts;
using DarkFactory.Core;
using DarkFactory.Data;
using ModelContextProtocol.Server;

namespace DarkFactory.Mcp.Tools;

/// <summary>
/// Corpus intake (docs/adr/0037): bring an existing body of specifications
/// into the graph by extracting a draft per document, asking about its holes,
/// and proposing it once they are filled. Thin over
/// <see cref="IntakeService"/>. Nothing here writes the graph; the last step
/// files an ordinary amendment, approved like any other.
/// </summary>
[McpServerToolType]
public static class IntakeTools
{
    [McpServerTool(Name = "df.intake.start"),
     Description("Start importing an existing corpus of specification documents into a project. Stores the corpus as submitted; nothing is extracted yet.")]
    public static async Task<IntakeSummary> Start(
        IntakeService intake,
        IConfiguration configuration,
        [Description("The project to import into.")] string project_id,
        [Description("A name for this import, e.g. the corpus it comes from.")] string name,
        [Description("Every document in the corpus: where it came from, its title, and its full text.")] IntakeSourceArg[] sources,
        CancellationToken cancellationToken = default)
    {
        var started = await Errors.Surfacing(() => intake.StartAsync(
            project_id,
            name,
            sources.Select(s => new IntakeSourceInput(s.SourceRef, s.Title, s.Content)).ToList(),
            ServerTools.ResolveOrgId(configuration),
            cancellationToken));

        return await Summarize(intake, started.Intake.Id, cancellationToken);
    }

    [McpServerTool(Name = "df.intake.list"),
     Description("A project's imports: where each came from, how many documents, and how far along extraction and proposal are.")]
    public static async Task<IReadOnlyList<IntakeListItem>> List(
        IntakeService intake,
        [Description("The project.")] string project_id,
        CancellationToken cancellationToken = default)
    {
        var rows = await Errors.Surfacing(() => intake.ListAsync(project_id, cancellationToken));
        return rows.Select(r => new IntakeListItem(
            r.Intake.Id, r.Intake.Name, r.Intake.SourceServerId, r.Intake.ConversationId,
            r.Documents, r.Extracted, r.Proposed, r.OpenQuestions, r.Intake.CreatedAt)).ToList();
    }

    [McpServerTool(Name = "df.intake.preview"),
     Description("What a corpus server would bring in, area by area, before anything is pulled.")]
    public static async Task<IReadOnlyList<CorpusArea>> Preview(
        CorpusImporter importer,
        [Description("The corpus server's id, from df.servers.list.")] string server_id,
        CancellationToken cancellationToken = default) =>
        await Errors.Surfacing(() => importer.PreviewAsync(server_id, cancellationToken));

    [McpServerTool(Name = "df.intake.pull"),
     Description("Start an import by pulling every document from a registered corpus server (docs/conventions/corpus.md). Superseded and rejected documents are left out unless asked for. Each document's text is checked against the hash the server listed.")]
    public static async Task<IntakeSummary> Pull(
        CorpusImporter importer,
        IntakeService intake,
        IConfiguration configuration,
        [Description("The project to import into.")] string project_id,
        [Description("The corpus server's id, from df.servers.list.")] string server_id,
        [Description("A name for this import. Defaults to the server's name.")] string? name = null,
        [Description("Only documents in this area, e.g. \"drones\".")] string? area = null,
        [Description("Also import superseded and rejected documents.")] bool include_retired = false,
        CancellationToken cancellationToken = default)
    {
        var started = await Errors.Surfacing(() => importer.PullAsync(
            project_id, server_id, name, area, include_retired, ServerTools.ResolveOrgId(configuration), cancellationToken));
        return await Summarize(intake, started.Intake.Id, cancellationToken);
    }

    [McpServerTool(Name = "df.intake.drift"),
     Description("What has changed at the corpus server since this import was pulled: documents edited, added or removed. A report — the factory is authoritative after import, so nothing is re-imported.")]
    public static async Task<CorpusDriftResult> Drift(
        CorpusImporter importer,
        [Description("The intake id.")] string intake_id,
        CancellationToken cancellationToken = default)
    {
        var drift = await Errors.Surfacing(() => importer.DriftAsync(intake_id, cancellationToken));
        return new CorpusDriftResult(
            drift.IntakeId,
            drift.ServerName,
            drift.Changed.Select(c => new CorpusDriftChange(c.OriginId, c.SourceId, c.ImportedSha256, c.CurrentSha256)).ToList(),
            drift.Added.Select(d => d.Id).ToList(),
            drift.Removed);
    }

    [McpServerTool(Name = "df.intake.source"),
     Description("One imported document: its full text, its current draft nodes, and every question raised about it.")]
    public static async Task<IntakeSourceView> Source(
        IntakeService intake,
        [Description("The source id, from df.intake.status.")] string source_id,
        CancellationToken cancellationToken = default)
    {
        var detail = await Errors.Surfacing(() => intake.GetSourceAsync(source_id, cancellationToken));
        var s = detail.Source;
        return new IntakeSourceView(
            s.Id, s.IntakeId, s.Seq, s.SourceRef, s.Title, s.Status.ToString().ToLowerInvariant(), s.DraftRevision,
            s.Content, detail.Draft, detail.Questions.Select(q => Row(q, s.SourceRef)).ToList(), s.Failure, s.AmendmentId);
    }

    [McpServerTool(Name = "df.intake.suggest_paths"),
     Description("Offer 2–4 answers, each with its consequence, for every open question on an import that has none (ADR-0039). One model call per document. Suggestions only; a person still chooses.")]
    public static async Task<IntakePathsResult> SuggestPaths(
        IntakeService intake,
        [Description("The intake id.")] string intake_id,
        CancellationToken cancellationToken = default)
    {
        var result = await Errors.Surfacing(() => intake.SuggestPathsAsync(intake_id, cancellationToken));
        return new IntakePathsResult(result.QuestionsUpdated, result.SourcesProcessed, result.Usage.TotalTokens);
    }

    [McpServerTool(Name = "df.intake.next"),
     Description("The one thing a person should do next on an import (ADR-0039): decide a question (with its paths), rebuild a document's draft with the answers, propose a document's specs into pending, extract an unread document, or done. Documents go in corpus order, scope first.")]
    public static async Task<IntakeNextResult> Next(
        IntakeService intake,
        [Description("The intake id.")] string intake_id,
        CancellationToken cancellationToken = default)
    {
        var next = await Errors.Surfacing(() => intake.NextAsync(intake_id, cancellationToken));
        return new IntakeNextResult(
            intake_id,
            next.Step,
            next.Source?.Id,
            next.Source?.SourceRef,
            next.Source?.Title,
            next.Question is null ? null : Row(next.Question, next.Source?.SourceRef),
            next.OpenInSource,
            next.Nodes,
            next.Progress.Documents,
            next.Progress.Settled,
            next.Progress.OpenQuestions,
            next.Progress.WithoutPaths);
    }

    [McpServerTool(Name = "df.intake.guide"),
     Description("Set the project owner's standing guidance for an import — e.g. \"live scope only; deferred documents produce no nodes\". Every later extraction is shown it. Empty clears it.")]
    public static async Task<IntakeGuidance> Guide(
        IntakeService intake,
        [Description("The intake id.")] string intake_id,
        [Description("The guidance, in plain sentences.")] string guidance,
        CancellationToken cancellationToken = default)
    {
        var updated = await Errors.Surfacing(() => intake.GuideAsync(intake_id, guidance, cancellationToken));
        return new IntakeGuidance(updated.Id, updated.Guidance);
    }

    [McpServerTool(Name = "df.intake.refresh"),
     Description("Update an import from its corpus server: re-fetch edited documents nothing has been extracted from yet, add new ones in the imported areas, drop ones deleted or superseded at the source. Documents already extracted are reported, not changed.")]
    public static async Task<IntakeRefresh> Refresh(
        CorpusImporter importer,
        [Description("The intake id.")] string intake_id,
        CancellationToken cancellationToken = default) =>
        await Errors.Surfacing(() => importer.RefreshAsync(intake_id, cancellationToken));

    [McpServerTool(Name = "df.intake.extract"),
     Description("Extract, or re-extract, one source document: a draft of small spec nodes plus the holes an implementer would have to guess at, raised as questions. Re-extracting builds answered questions into the draft.")]
    public static async Task<IntakeExtractResult> Extract(
        IntakeService intake,
        IConfiguration configuration,
        [Description("The source id from df.intake.start or df.intake.status.")] string source_id,
        CancellationToken cancellationToken = default)
    {
        var result = await Errors.Surfacing(() => intake.ExtractAsync(
            source_id, ServerTools.ResolveOrgId(configuration), cancellationToken));
        var source = result.Source;

        return new IntakeExtractResult(
            source.Id,
            source.SourceRef,
            result.Accepted,
            source.Status.ToString().ToLowerInvariant(),
            source.DraftRevision,
            result.Reply,
            source.DraftJson is null ? null : JsonSerializer.Deserialize<SpecDiffDocument>(source.DraftJson),
            result.OpenQuestions.Select(q => Row(q, source.SourceRef)).ToList(),
            result.Errors.Select(e => $"{e.Location}: {e.Message}").ToList(),
            source.ContextPackRef,
            result.Usage.TotalTokens);
    }

    [McpServerTool(Name = "df.intake.status"),
     Description("Where an import stands: every source with its status, draft size and question counts.")]
    public static async Task<IntakeSummary> Status(
        IntakeService intake,
        [Description("The intake id.")] string intake_id,
        CancellationToken cancellationToken = default) =>
        await Summarize(intake, intake_id, cancellationToken);

    [McpServerTool(Name = "df.intake.questions"),
     Description("The questions an import has raised, in corpus order. Filter by status (open, answered, deferred, resolved) or by source.")]
    public static async Task<IReadOnlyList<IntakeQuestionRow>> Questions(
        IntakeService intake,
        [Description("The intake id.")] string intake_id,
        [Description("Only questions in this status. Omit for all.")] string? status = null,
        [Description("Only questions about this source.")] string? source_id = null,
        CancellationToken cancellationToken = default)
    {
        IntakeQuestionStatus? wanted = null;
        if (status is not null)
        {
            if (!Enum.TryParse<IntakeQuestionStatus>(status, ignoreCase: true, out var parsed))
            {
                throw new ModelContextProtocol.McpException(
                    $"'{status}' is not a question status; use open, answered, deferred or resolved.");
            }
            wanted = parsed;
        }

        var rows = await Errors.Surfacing(() => intake.QuestionsAsync(intake_id, wanted, source_id, cancellationToken));
        return rows.Select(r => Row(r.Question, r.SourceRef)).ToList();
    }

    [McpServerTool(Name = "df.intake.answer"),
     Description("Answer an open question. The next df.intake.extract of its source builds the answer into the draft.")]
    public static async Task<IntakeQuestionRow> Answer(
        IntakeService intake,
        IConfiguration configuration,
        [Description("The question id.")] string question_id,
        [Description("The answer, in a sentence or two.")] string answer,
        CancellationToken cancellationToken = default)
    {
        var question = await Errors.Surfacing(() => intake.AnswerAsync(
            question_id, answer, ServerTools.ResolveOrgId(configuration), cancellationToken));
        return Row(question, null);
    }

    [McpServerTool(Name = "df.intake.defer"),
     Description("Leave a question open on purpose. It stops blocking the proposal, and later extractions are told not to guess at it.")]
    public static async Task<IntakeQuestionRow> Defer(
        IntakeService intake,
        IConfiguration configuration,
        [Description("The question id.")] string question_id,
        [Description("Why it is being left open.")] string reason,
        CancellationToken cancellationToken = default)
    {
        var question = await Errors.Surfacing(() => intake.DeferAsync(
            question_id, reason, ServerTools.ResolveOrgId(configuration), cancellationToken));
        return Row(question, null);
    }

    [McpServerTool(Name = "df.intake.propose"),
     Description("File a source's draft as a proposed amendment. Refused while any question is open or any answer is not yet in the draft. Approval is the ordinary df.specs.approve.")]
    public static async Task<IntakeProposed> Propose(
        IntakeService intake,
        [Description("The source id.")] string source_id,
        CancellationToken cancellationToken = default)
    {
        var amendment = await Errors.Surfacing(() => intake.ProposeAsync(source_id, cancellationToken));
        return new IntakeProposed(source_id, amendment.Id, amendment.Status.ToString());
    }

    private static async Task<IntakeSummary> Summarize(
        IntakeService intake, string intakeId, CancellationToken cancellationToken)
    {
        var overview = await Errors.Surfacing(() => intake.GetAsync(intakeId, cancellationToken));
        var i = overview.Intake;

        return new IntakeSummary(
            i.Id,
            i.ProjectId,
            i.Name,
            i.ConversationId,
            i.CorpusRef,
            overview.Sources.Select(s => new IntakeSourceRow(
                s.Source.Id,
                s.Source.Seq,
                s.Source.SourceRef,
                s.Source.Title,
                s.Source.Status.ToString().ToLowerInvariant(),
                s.Source.DraftRevision,
                s.Nodes,
                s.Open,
                s.Answered,
                s.Deferred,
                s.Resolved,
                s.Source.AmendmentId,
                s.Source.Failure)).ToList());
    }

    private static IntakeQuestionRow Row(IntakeQuestion q, string? sourceRef) => new(
        q.Id,
        q.SourceId,
        sourceRef,
        q.Kind,
        q.Status.ToString().ToLowerInvariant(),
        q.Question,
        q.Quote,
        JsonSerializer.Deserialize<int[]>(q.AffectsJson) ?? [],
        q.Answer,
        q.RaisedInRevision,
        q.IncorporatedInRevision,
        q.OptionsJson is null ? [] : JsonSerializer.Deserialize<IntakeOption[]>(q.OptionsJson) ?? []);
}

public sealed record IntakeSourceArg(string SourceRef, string Title, string Content);

public sealed record IntakeSummary(
    string IntakeId,
    string ProjectId,
    string Name,
    string ConversationId,
    string CorpusRef,
    IReadOnlyList<IntakeSourceRow> Sources);

public sealed record IntakeSourceRow(
    string SourceId,
    int Seq,
    string SourceRef,
    string Title,
    string Status,
    int DraftRevision,
    int Nodes,
    int OpenQuestions,
    int AnsweredQuestions,
    int DeferredQuestions,
    int ResolvedQuestions,
    string? AmendmentId,
    string? Failure);

public sealed record IntakeQuestionRow(
    string QuestionId,
    string SourceId,
    string? SourceRef,
    string Kind,
    string Status,
    string Question,
    string? Quote,
    IReadOnlyList<int> Affects,
    string? Answer,
    int RaisedInRevision,
    int? IncorporatedInRevision,
    // The paths open to whoever answers (ADR-0039); empty until suggested.
    IReadOnlyList<IntakeOption> Options);

public sealed record IntakePathsResult(int QuestionsUpdated, int SourcesProcessed, int TokensUsed);

public sealed record IntakeNextResult(
    string IntakeId,
    string Step,
    string? SourceId,
    string? SourceRef,
    string? SourceTitle,
    IntakeQuestionRow? Question,
    int OpenInSource,
    int Nodes,
    int Documents,
    int Settled,
    int OpenQuestions,
    int WithoutPaths);

public sealed record IntakeExtractResult(
    string SourceId,
    string SourceRef,
    bool Accepted,
    string Status,
    int DraftRevision,
    string Reply,
    SpecDiffDocument? Draft,
    IReadOnlyList<IntakeQuestionRow> OpenQuestions,
    IReadOnlyList<string> Errors,
    string? ContextRef,
    int TokensUsed);

public sealed record IntakeProposed(string SourceId, string AmendmentId, string Status);

public sealed record IntakeGuidance(string IntakeId, string? Guidance);

public sealed record IntakeSourceView(
    string SourceId,
    string IntakeId,
    int Seq,
    string SourceRef,
    string Title,
    string Status,
    int DraftRevision,
    string Content,
    SpecDiffDocument? Draft,
    IReadOnlyList<IntakeQuestionRow> Questions,
    string? Failure,
    string? AmendmentId);

public sealed record IntakeListItem(
    string IntakeId,
    string Name,
    string? SourceServerId,
    string ConversationId,
    int Documents,
    int Extracted,
    int Proposed,
    int OpenQuestions,
    DateTimeOffset CreatedAt);

public sealed record CorpusDriftChange(string OriginId, string SourceId, string ImportedSha256, string CurrentSha256);

public sealed record CorpusDriftResult(
    string IntakeId,
    string ServerName,
    IReadOnlyList<CorpusDriftChange> Changed,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed);
