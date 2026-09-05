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

    /// <summary>
    /// Generous, because a member with no override now generates up to the
    /// model's ceiling and this client does not stream.
    /// <para>Streaming is the real fix for very long generations and is
    /// deliberately not implemented here — see docs/adr/0027. At 128k
    /// output tokens a non-streaming request can outlive any timeout worth
    /// setting; this bound is honest about what the current client can wait
    /// for, not a claim that it is enough for every possible response.</para>
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(15);

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
    /// Model id → its maximum output tokens, overridable from configuration
    /// for a model this build predates.
    ///
    /// It lives here because the gateway is the only layer that knows which
    /// model a deployment resolves to (docs/adr/0027): a team member naming
    /// a token count would be naming a fact about a model it is
    /// deliberately kept ignorant of.
    /// </summary>
    public Dictionary<string, int> MaxOutputTokens { get; set; } = [];

    /// <summary>
    /// The API's own numbers, from <c>GET /v1/models/{id}</c>
    /// (<c>max_tokens</c>), pinned at authoring time rather than fetched.
    ///
    /// Pinned deliberately. A lookup per call is a latency and cost
    /// regression, and a lookup that can fail makes truncation — the exact
    /// fault this whole change exists to remove — come back intermittently
    /// and dependent on someone else's uptime. A wrong number here is a
    /// visible, fixable constant; a flaky one is a ghost.
    /// </summary>
    public static IReadOnlyDictionary<string, int> DefaultMaxOutputTokens { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["claude-opus-5"] = 128_000,
            ["claude-sonnet-5"] = 128_000,
            ["claude-haiku-4-5-20251001"] = 64_000,
        };

    /// <summary>
    /// Conservative ceiling for a model this build has never heard of.
    ///
    /// Every current model exceeds this, so the cost of not knowing is a
    /// response that could have been longer — recoverable, and now named as
    /// truncation rather than as malformed output. Assuming the maximum
    /// instead would fail the whole call with a 400 on any model whose real
    /// ceiling is lower, which is the worse direction to be wrong in.
    /// <para>Falling back here is worth a warning, not silence: it is how
    /// an implementer on a newly-configured model would quietly return to
    /// truncating.</para>
    /// </summary>
    public const int UnknownModelMaxOutputTokens = 8192;

    /// <summary>
    /// The ceiling to send: the caller's explicit override when there is
    /// one, otherwise the model's own maximum.
    ///
    /// An override above the model's ceiling is clamped rather than
    /// rejected — it means "as much as possible", and failing the call over
    /// it would help nobody. <paramref name="known"/> reports whether the
    /// model was recognised, so the caller can say so once.
    /// </summary>
    public int ResolveMaxOutputTokens(string model, int? requested, out bool known)
    {
        known = true;
        int ceiling;
        if (MaxOutputTokens.TryGetValue(model, out var configured) && configured > 0)
        {
            ceiling = configured;
        }
        else if (DefaultMaxOutputTokens.TryGetValue(model, out var builtIn))
        {
            ceiling = builtIn;
        }
        else
        {
            known = false;
            ceiling = UnknownModelMaxOutputTokens;
        }

        return requested is { } limit && limit > 0 ? Math.Min(limit, ceiling) : ceiling;
    }

    public int ResolveMaxOutputTokens(string model, int? requested) =>
        ResolveMaxOutputTokens(model, requested, out _);

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
