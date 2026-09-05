using DarkFactory.Core;

namespace DarkFactory.Anthropic;

/// <summary>
/// Configuration for the Anthropic-direct gateway (docs/adr/0027).
/// Bound from the <c>Anthropic</c> configuration section.
/// </summary>
public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public string? ApiKey { get; set; }

    /// <summary>Overridable for a proxy or a gateway in front of the API; the default is the API itself.</summary>
    public string BaseUrl { get; set; } = "https://api.anthropic.com";

    /// <summary>
    /// The Messages API version header. Pinned rather than floating,
    /// because a silently-changing wire contract is exactly the thing a
    /// version header exists to prevent.
    /// </summary>
    public string ApiVersion { get; set; } = "2023-06-01";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Role name → model id. The same indirection Foundry deployments give
    /// us (docs/adr/0027): callers name a role, configuration decides what
    /// that means, and swapping the model behind a role stays a config
    /// change rather than a code change — on either provider.
    /// </summary>
    public Dictionary<string, string> Deployments { get; set; } = [];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>
    /// docs/adr/0022's default policy, expressed as models rather than as
    /// prose: the strongest model where a subtle failure would go unnoticed
    /// (spec amendments, completion judgment), and progressively cheaper
    /// ones where a test or a schema can catch the mistake.
    /// </summary>
    public static IReadOnlyDictionary<string, string> DefaultDeployments { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AgentRoles.Architect] = "claude-opus-5",
            [AgentRoles.Reviewer] = "claude-opus-5",
            [AgentRoles.Planner] = "claude-opus-5",
            [AgentRoles.Implementer] = "claude-sonnet-5",
            [AgentRoles.Router] = "claude-haiku-4-5-20251001",
        };

    /// <summary>
    /// Resolves a role to a model id: explicit configuration first, then the
    /// default policy. A role with neither is an error rather than a guess —
    /// silently inventing a model is how a run ends up costing what nobody
    /// expected, or being served by something nobody chose.
    /// </summary>
    public string ResolveModel(string role)
    {
        if (Deployments.TryGetValue(role, out var configured) && !string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (DefaultDeployments.TryGetValue(role, out var fallback))
        {
            return fallback;
        }

        throw new ModelGatewayException(
            $"No model is configured for role '{role}'. Set {SectionName}:Deployments:{role} to a model id.",
            FailureClass.NeedsHuman);
    }
}
