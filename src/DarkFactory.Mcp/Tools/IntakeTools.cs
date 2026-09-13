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
        q.IncorporatedInRevision);
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
    int? IncorporatedInRevision);

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
