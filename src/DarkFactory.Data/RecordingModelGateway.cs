using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DarkFactory.Data;

/// <summary>
/// Wraps a provider gateway and writes the <c>model_calls</c> fact row for
/// every call (docs/adr/0032).
///
/// A decorator rather than a base class or a helper agents remember to
/// call, because docs/adr/0032 says these rows are written by the gateway
/// and never by an agent — and the difference only matters when something
/// goes wrong. An agent trusted to record its own usage is an agent that
/// can forget to, and the call most worth having a row for is the one that
/// failed. Here, there is no path from a caller to a provider that does not
/// pass through this.
///
/// The providers stay ignorant of the database, which is what keeps
/// docs/adr/0027's containment rule intact: this lives in
/// DarkFactory.Data, and the thing it wraps knows nothing about it.
/// </summary>
public sealed class RecordingModelGateway(
    IModelGateway inner,
    DarkFactoryDbContext db,
    ILogger<RecordingModelGateway> logger) : IModelGateway, IModelCallLog
{
    public async Task<ModelCompletion> CompleteAsync(
        ModelRequest request, CancellationToken cancellationToken = default)
    {
        var context = request.Context ?? new ModelCallContext();

        try
        {
            var completion = await inner.CompleteAsync(request, cancellationToken);
            var id = await RecordAsync(request, context, completion, cancellationToken);
            return completion with { ModelCallId = id };
        }
        catch (ModelGatewayException ex)
        {
            // A failed call still spent time, and often tokens. Recording it
            // is the difference between a cost report that reconciles and
            // one that quietly under-counts every retry — and "which
            // deployment fails most" is a question only these rows answer.
            await RecordFailureAsync(request, context, ex, cancellationToken);
            throw;
        }
    }

    private async Task<string> RecordAsync(
        ModelRequest request,
        ModelCallContext context,
        ModelCompletion completion,
        CancellationToken cancellationToken)
    {
        var usage = completion.Usage;

        var call = new ModelCall
        {
            Id = Ulid.NewUlid(),
            // docs/adr/0010: everything is org-scoped. A call with no org is
            // a bug in the caller, but it must not lose the row.
            OrgId = context.OrgId ?? "unknown",
            ProjectId = context.ProjectId,
            RunId = context.RunId,
            BatchId = context.BatchId,
            StageId = context.StageId,
            TaskId = context.TaskId,
            Attempt = context.Attempt,
            TeamMemberId = context.TeamMemberId,
            PersonaId = context.PersonaId,
            Deployment = request.Deployment,
            Provider = completion.Provider ?? "unknown",
            ModelFamily = completion.ModelFamily ?? request.Deployment,

            // Uncached input is what remains once the cache-served portion
            // is taken out: the provider reports the total and the cached
            // part, and storing both halves separately is what makes a cost
            // model possible.
            InputTokensUncached = Math.Max(usage.InputTokens - usage.CachedInputTokens, 0),
            InputTokensCached = usage.CachedInputTokens,
            CacheWriteTokens = usage.CacheWriteInputTokens,
            ContextPackRef = context.ContextPackRef,
            SkillRevisions = context.SkillRevisions.ToArray(),
            PromptTemplateVersion = context.PromptTemplateVersion,
            ThinkingPreset = context.ThinkingPreset,

            OutputTokens = usage.OutputTokens,
            ThinkingTokens = usage.ThinkingTokens,
            LatencyMs = completion.LatencyMs,
            Cost = ModelPricing.CostUsd(completion.ModelFamily ?? request.Deployment, usage),

            Retried = context.Retried,
            Steered = context.Steered,

            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.ModelCalls.Add(call);
        await db.SaveChangesAsync(cancellationToken);
        return call.Id;
    }

    private async Task RecordFailureAsync(
        ModelRequest request, ModelCallContext context, ModelGatewayException ex, CancellationToken cancellationToken)
    {
        try
        {
            db.ModelCalls.Add(new ModelCall
            {
                Id = Ulid.NewUlid(),
                OrgId = context.OrgId ?? "unknown",
                ProjectId = context.ProjectId,
                RunId = context.RunId,
                BatchId = context.BatchId,
                StageId = context.StageId,
                TaskId = context.TaskId,
                Attempt = context.Attempt,
                TeamMemberId = context.TeamMemberId,
                PersonaId = context.PersonaId,
                Deployment = request.Deployment,
                Provider = "unknown",
                ModelFamily = request.Deployment,
                InputTokensUncached = 0,
                InputTokensCached = 0,
                CacheWriteTokens = 0,
                ContextPackRef = context.ContextPackRef,
                SkillRevisions = context.SkillRevisions.ToArray(),
                PromptTemplateVersion = context.PromptTemplateVersion,
                ThinkingPreset = context.ThinkingPreset,
                OutputTokens = 0,
                ThinkingTokens = 0,
                LatencyMs = 0,
                StageResult = $"gateway_error:{ex.Failure}",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception recordingFailure)
        {
            // Never let bookkeeping mask the real error. The caller is
            // waiting on a model failure it can act on; losing that to a
            // database problem in the audit path would be a strictly worse
            // outcome than losing the row.
            logger.LogError(recordingFailure,
                "Failed to record a model_calls row for a failed call to '{Deployment}'", request.Deployment);
        }
    }

    /// <summary>
    /// Completes the outcome half of a row the gateway already wrote. The
    /// caller is the only thing that knows whether the artifact validated,
    /// and it only knows after the fact — so this is an update to an
    /// existing row rather than a second row.
    /// </summary>
    public async Task RecordOutcomeAsync(
        string modelCallId,
        bool? artifactValidFirstTry = null,
        bool? retried = null,
        bool? steered = null,
        string? stageResult = null,
        CancellationToken cancellationToken = default)
    {
        var call = await db.ModelCalls.SingleOrDefaultAsync(c => c.Id == modelCallId, cancellationToken);
        if (call is null)
        {
            logger.LogWarning("No model_calls row '{ModelCallId}' to record an outcome against", modelCallId);
            return;
        }

        call.ArtifactValidFirstTry = artifactValidFirstTry ?? call.ArtifactValidFirstTry;
        call.Retried = retried ?? call.Retried;
        call.Steered = steered ?? call.Steered;
        call.StageResult = stageResult ?? call.StageResult;

        await db.SaveChangesAsync(cancellationToken);
    }
}
