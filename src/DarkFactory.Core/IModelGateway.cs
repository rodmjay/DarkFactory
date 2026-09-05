namespace DarkFactory.Core;

/// <summary>
/// Every model call in the factory goes through here
/// (docs/adr/0009, docs/adr/0027).
///
/// Note what this interface does <em>not</em> contain: no provider name, no
/// vendor SDK type, no API key, no endpoint, no model identifier. Callers
/// name a <b>deployment</b> — "architect", "planner" — and the gateway
/// resolves what that means. Swapping the model behind a role is then a
/// Foundry change rather than a code change, and the engine, the
/// conversation service and the tests all become provider-agnostic by
/// construction rather than by discipline.
/// </summary>
public interface IModelGateway
{
    Task<ModelCompletion> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default);
}

public enum ModelRole { User, Assistant }

public sealed record ModelMessage(ModelRole Role, string Content);

/// <param name="Deployment">
/// A role-named deployment (docs/adr/0027), never a vendor model id. The
/// gateway is the only thing that knows which model that resolves to.
/// </param>
/// <param name="CacheableSystemPrefix">
/// The part of the system prompt that is identical from call to call —
/// skills, standards, the response contract. Sent ahead of
/// <paramref name="SystemPrompt"/> with a cache breakpoint after it, so a
/// run's repeated calls pay for it once rather than every time.
/// <para>The split is the caller's judgement and it has to be honest: mark
/// something cacheable that actually varies and every call misses the
/// cache, which costs a little more than not trying. Anything that changes
/// within a run — the spec neighbourhood, the conversation tail, prior
/// artifacts — belongs in <paramref name="SystemPrompt"/>.</para>
/// </param>
/// <param name="Context">
/// Who this call is for, so the gateway can write its usage fact row
/// (docs/adr/0032). The gateway cannot record a run or a stage it was
/// never told about, and an agent trusted to record its own usage is an
/// agent that can forget to — so the context travels with the request.
/// </param>
/// <param name="MaxOutputTokens">
/// A ceiling on the response, or <c>null</c> to let the gateway apply the
/// model's own maximum.
/// <para>Null is the normal case, and it is not "some safe default": only
/// the gateway knows which model a deployment resolves to (docs/adr/0027),
/// so only the gateway can answer "how much can this model emit". A number
/// here is a deliberate override — a hard latency bound, or a member whose
/// answers should be short.</para>
/// <para>This was a hardcoded 8192 until the 3d acceptance demo truncated
/// the implementer's first attempt mid-JSON and paid for the retry. One
/// constant cannot be right for both a router emitting a route and an
/// implementer emitting whole files.</para>
/// <para><b>It is not a cost control.</b> <c>max_tokens</c> is a ceiling,
/// not a reservation, so raising it costs nothing by itself — but see
/// docs/adr/0027 on what that leaves bounding a single runaway call.</para>
/// <para><b>Do not give this a non-null default.</b> It was <c>= 8192</c>,
/// and a default that is a real value cannot express "unspecified": the
/// gateway could not tell "the caller did not say" from "the caller wants
/// exactly 8192", so the decision had to be made by every caller and then
/// depended on all of them agreeing. They did not — the conversation path
/// was still passing nothing long after the stage path had been fixed, and
/// it read as fixed because the number was plausible. Null pushes the
/// resolution into the one place that knows the model, and a caller that
/// forgets now gets the model's ceiling rather than a chat-sized cap.</para>
/// </param>
public sealed record ModelRequest(
    string Deployment,
    string SystemPrompt,
    IReadOnlyList<ModelMessage> Messages,
    int? MaxOutputTokens = null,
    double? Temperature = null,
    ModelCallContext? Context = null,
    string? CacheableSystemPrefix = null);

/// <summary>
/// The dimensions docs/adr/0032 records against every call. Almost
/// everything is optional because not every caller has every dimension —
/// a conversational turn has no run, a first attempt has no retry — and a
/// fact table with honest nulls beats one with invented values.
/// </summary>
public sealed record ModelCallContext
{
    public string? OrgId { get; init; }
    public string? ProjectId { get; init; }
    public string? RunId { get; init; }
    public string? BatchId { get; init; }

    /// <summary>The pipeline stage, or a non-stage point like "conversation".</summary>
    public string? StageId { get; init; }

    public string? TaskId { get; init; }
    public int Attempt { get; init; } = 1;
    public string? TeamMemberId { get; init; }
    public string? PersonaId { get; init; }

    /// <summary>Ref of the ContextPack the model was shown, so cost can be joined to context size.</summary>
    public string? ContextPackRef { get; init; }

    public IReadOnlyList<string> SkillRevisions { get; init; } = [];
    public string? PromptTemplateVersion { get; init; }

    /// <summary>quick | balanced | deliberate (docs/adr/0028's persona speed preset).</summary>
    public string? ThinkingPreset { get; init; }

    /// <summary>
    /// Outcome, known only after the caller has judged the result. Set by
    /// the caller on the row the gateway already wrote — see
    /// <see cref="IModelCallLog"/>.
    /// </summary>
    public bool? Retried { get; init; }

    public bool? Steered { get; init; }
}

/// <summary>
/// Lets a caller complete the outcome half of a usage fact row it could not
/// know at call time — whether the artifact validated first try, and how
/// the stage ended (docs/adr/0032).
///
/// Deliberately separate from <see cref="IModelGateway"/>: the gateway
/// writes the row unconditionally, and this only ever annotates a row that
/// already exists. A caller that never annotates loses the outcome
/// columns, not the call.
/// </summary>
public interface IModelCallLog
{
    Task RecordOutcomeAsync(
        string modelCallId,
        bool? artifactValidFirstTry = null,
        bool? retried = null,
        bool? steered = null,
        string? stageResult = null,
        CancellationToken cancellationToken = default);
}

/// <param name="Deployment">Echoed back so an audit record says which deployment actually served the call, including after a fallback.</param>
public sealed record ModelCompletion(
    string Text,
    ModelUsage Usage,
    string Deployment)
{
    /// <summary>
    /// The <c>model_calls</c> row this call wrote (docs/adr/0032), so the
    /// caller can annotate its outcome once it knows one. Null when no
    /// recorder is wired — the providers themselves never set it.
    /// </summary>
    public string? ModelCallId { get; init; }

    /// <summary>Wall-clock time of the provider call, recorded on the fact row.</summary>
    public long LatencyMs { get; init; }

    /// <summary>Which provider served it, for the fact row's per-dimension queries.</summary>
    public string? Provider { get; init; }

    /// <summary>The concrete model behind the deployment name.</summary>
    public string? ModelFamily { get; init; }

    /// <summary>
    /// Why generation stopped. Carried because "the response was not valid
    /// JSON" and "the response was cut off at the output limit" are the
    /// same symptom with completely different fixes, and only the provider
    /// can tell them apart.
    /// </summary>
    public string? StopReason { get; init; }

    /// <summary>True when the model ran out of output budget mid-answer.</summary>
    public bool Truncated => StopReason is "max_tokens" or "length";
}

/// <summary>
/// Token counts as the provider reported them. docs/adr/0027 makes provider
/// metering the source of truth for cost; this is what the factory records
/// so usage is visible without querying the provider.
///
/// <para><b>Cached and thinking counts are breakdowns, not additions.</b>
/// <see cref="CachedInputTokens"/> and <see cref="CacheWriteInputTokens"/>
/// are portions of <see cref="InputTokens"/>; <see cref="ThinkingTokens"/>
/// is a portion of <see cref="OutputTokens"/>. Summing all five would
/// double-count, which is why <see cref="TotalTokens"/> does not.</para>
///
/// <para>They are carried separately because they are billed differently —
/// a cache read costs a fraction of a fresh input token — so a budget or a
/// cost report built on the totals alone would be wrong in the direction
/// that matters.</para>
/// </summary>
public sealed record ModelUsage(
    int InputTokens,
    int OutputTokens,
    int CachedInputTokens = 0,
    int CacheWriteInputTokens = 0,
    int ThinkingTokens = 0)
{
    /// <summary>Billable volume. Deliberately not a sum of every field — see the note above.</summary>
    public int TotalTokens => InputTokens + OutputTokens;

    public static ModelUsage None { get; } = new(0, 0);
}

/// <summary>
/// A model call failed. Carries a <see cref="FailureClass"/> so the engine's
/// existing retry policy (docs/adr/0007) applies to model calls the same way
/// it applies to tool calls, rather than each caller inventing its own rules.
/// </summary>
public sealed class ModelGatewayException(string message, FailureClass failure, Exception? inner = null)
    : Exception(message, inner)
{
    public FailureClass Failure { get; } = failure;
}

/// <summary>
/// Registered when no model provider is configured. It exists so the
/// factory still starts, still serves its health checks, and still answers
/// every tool that does not need a model — while any tool that does need
/// one fails with a sentence explaining exactly what is missing.
///
/// The alternative, silently falling back to some default provider, is
/// worse than an error: it produces plausible output from a model nobody
/// chose, billed to an account nobody nominated.
/// </summary>
public sealed class UnconfiguredModelGateway : IModelGateway
{
    public Task<ModelCompletion> CompleteAsync(
        ModelRequest request, CancellationToken cancellationToken = default) =>
        throw new ModelGatewayException(
            $"No model gateway is configured, so deployment '{request.Deployment}' cannot be called. " +
            "Set Foundry:Endpoint (and Foundry:ApiKey for local development) — see docs/adr/0027.",
            FailureClass.NeedsHuman);
}
