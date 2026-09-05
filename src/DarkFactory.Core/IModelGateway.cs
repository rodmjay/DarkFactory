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
public sealed record ModelRequest(
    string Deployment,
    string SystemPrompt,
    IReadOnlyList<ModelMessage> Messages,
    int MaxOutputTokens = 8192,
    double? Temperature = null);

/// <param name="Deployment">Echoed back so an audit record says which deployment actually served the call, including after a fallback.</param>
public sealed record ModelCompletion(
    string Text,
    ModelUsage Usage,
    string Deployment);

/// <summary>
/// Token counts as the provider reported them. docs/adr/0027 makes Foundry
/// metering the source of truth for cost; this is what the factory records
/// per turn so usage is visible without querying the provider.
/// </summary>
public sealed record ModelUsage(int InputTokens, int OutputTokens)
{
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
