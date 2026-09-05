using DarkFactory.Contracts;
using DarkFactory.Core;
using DarkFactory.Data;
using DarkFactory.Engine.StageHandlers;

namespace DarkFactory.Engine.Agents;

public sealed record AgentTurn<T>(T Value, string ContextRef, string? ModelCallId, bool ValidFirstTry);

/// <summary>
/// What every stage agent does around its model call, in one place: build
/// and persist the context, refuse to spend over budget, call the gateway,
/// validate the answer, retry once with the violations, and record the
/// outcome against the fact row.
///
/// Shared because getting any one of those wrong silently is the failure
/// mode that matters. Three agents each re-implementing "validate, then
/// retry once with the errors" would be three chances to skip the
/// validation under time pressure — and docs/adr/0032's outcome columns are
/// only meaningful if <em>every</em> agent reports them the same way.
/// </summary>
public sealed class StageAgent(
    IModelGateway gateway,
    IModelCallLog callLog,
    StageContextBuilder contexts,
    BudgetService budgets)
{
    /// <summary>
    /// Runs one stage's model work. <paramref name="parse"/> turns the raw
    /// response into the artifact, or returns the violations that should be
    /// fed back — the same shape schema and referential failures take, so
    /// the retry prompt does not care which kind it got.
    /// </summary>
    public async Task<AgentTurn<T>> RunAsync<T>(
        StageContext context,
        StageId stage,
        Func<StageContextPack, string> cacheablePrefix,
        Func<StageContextPack, string> systemPrompt,
        Func<StageContextPack, string> userMessage,
        Func<string, Task<(T? Value, SchemaValidationResult Validation)>> parse,
        string promptTemplateVersion,
        CancellationToken cancellationToken)
    {
        var built = await contexts.BuildAsync(context.Run, stage, attempt: 1, cancellationToken);

        // Budget is checked before spending, using what has already been
        // spent (docs/adr/0028, docs/adr/0032). It cannot prevent the
        // overspend that a single expensive call causes — the cost of a
        // call is not knowable until it returns — but it stops the run at
        // the first stage after the line was crossed rather than letting it
        // continue burning.
        var budget = await budgets.StateAsync(context.Run.Id, built.Agent.TeamMemberId, cancellationToken);
        if (budget.Exceeded)
        {
            throw new StageBudgetExceededException(BudgetService.OverBudgetMessage(built.Agent.Role, budget));
        }

        var messages = new List<ModelMessage> { new(ModelRole.User, userMessage(built.Pack)) };

        var completion = await gateway.CompleteAsync(
            new ModelRequest(
                built.Agent.Deployment,
                systemPrompt(built.Pack),
                messages,
                MaxOutputTokens: built.Agent.MaxOutputTokens,
                Context: CallContext(context, built, stage, attempt: 1, promptTemplateVersion),
                CacheableSystemPrefix: cacheablePrefix(built.Pack)),
            cancellationToken);

        var (value, validation) = await parse(completion.Text);
        validation = Explain(validation, completion);
        if (validation.IsValid && value is not null)
        {
            await RecordOutcomeAsync(completion.ModelCallId, validFirstTry: true, cancellationToken);
            return new AgentTurn<T>(value, built.Ref, completion.ModelCallId, ValidFirstTry: true);
        }

        // One retry, carrying the actual violations rather than "that was
        // invalid" — the model gets what a human author would get.
        await RecordOutcomeAsync(completion.ModelCallId, validFirstTry: false, cancellationToken);

        var retryBuilt = await contexts.BuildAsync(context.Run, stage, attempt: 2, cancellationToken);
        var retryMessages = new List<ModelMessage>
        {
            new(ModelRole.User, userMessage(retryBuilt.Pack)),
            new(ModelRole.Assistant, completion.Text),
            new(ModelRole.User,
                $"""
                 Your previous response could not be accepted. These are the problems with it:

                 {validation.AsBulletList()}

                 Reply again in the required format, correcting every problem listed.
                 """),
        };

        var retry = await gateway.CompleteAsync(
            new ModelRequest(
                retryBuilt.Agent.Deployment,
                systemPrompt(retryBuilt.Pack),
                retryMessages,
                MaxOutputTokens: retryBuilt.Agent.MaxOutputTokens,
                Context: CallContext(context, retryBuilt, stage, attempt: 2, promptTemplateVersion, retried: true),
                CacheableSystemPrefix: cacheablePrefix(retryBuilt.Pack)),
            cancellationToken);

        var (retryValue, retryValidation) = await parse(retry.Text);
        retryValidation = Explain(retryValidation, retry);
        if (!retryValidation.IsValid || retryValue is null)
        {
            await RecordOutcomeAsync(retry.ModelCallId, validFirstTry: false, cancellationToken, "invalid_after_retry");
            throw new StageAgentException(
                $"{built.Agent.Role} could not produce a valid result for {stage} after two attempts: " +
                retryValidation.Summarize());
        }

        await RecordOutcomeAsync(retry.ModelCallId, validFirstTry: false, cancellationToken);
        return new AgentTurn<T>(retryValue, retryBuilt.Ref, retry.ModelCallId, ValidFirstTry: false);
    }

    /// <summary>
    /// Names truncation for what it is.
    ///
    /// A response cut off at the output limit fails to parse, and the
    /// parser can only report that it did not parse — which sends both the
    /// model and whoever reads the log looking for a formatting mistake
    /// that is not there. Only the provider knows the difference, so where
    /// it tells us, we say so.
    /// </summary>
    private static SchemaValidationResult Explain(SchemaValidationResult validation, ModelCompletion completion)
    {
        if (validation.IsValid || !completion.Truncated)
        {
            return validation;
        }

        return new SchemaValidationResult(false,
        [
            new SchemaValidationError("(root)",
                "your reply was cut off before it finished because it reached the output token limit — " +
                "return fewer or smaller files in one response rather than a truncated answer"),
            .. validation.Errors,
        ]);
    }

    private static ModelCallContext CallContext(
        StageContext context,
        StageContextBuilder.BuiltStageContext built,
        StageId stage,
        int attempt,
        string promptTemplateVersion,
        bool? retried = null) => new()
        {
            OrgId = context.Run.OrgId,
            ProjectId = context.Run.ProjectId,
            RunId = context.Run.Id,
            StageId = stage.ToString(),
            Attempt = attempt,
            TeamMemberId = built.Agent.TeamMemberId,
            ContextPackRef = built.Ref,
            SkillRevisions = built.Pack.Skills.Select(s => $"{s.Name}@{s.Version}").ToList(),
            PromptTemplateVersion = promptTemplateVersion,
            Retried = retried,
            // docs/adr/0015: a steered run is a different run, and the fact
            // table should be able to say so.
            Steered = built.Pack.Steers.Count > 0 ? true : null,
        };

    private async Task RecordOutcomeAsync(
        string? modelCallId, bool validFirstTry, CancellationToken cancellationToken, string? stageResult = null)
    {
        if (modelCallId is null)
        {
            return;
        }

        await callLog.RecordOutcomeAsync(
            modelCallId, artifactValidFirstTry: validFirstTry, stageResult: stageResult,
            cancellationToken: cancellationToken);
    }
}

/// <summary>The agent could not produce a usable result. Permanent: a third attempt is not a plan.</summary>
public sealed class StageAgentException(string message) : Exception(message);

/// <summary>Over budget. Needs a human to raise the cap or split the work — retrying cannot help.</summary>
public sealed class StageBudgetExceededException(string message) : Exception(message);
