namespace DarkFactory.Foundry;

/// <summary>
/// Configuration for the Microsoft Foundry gateway (docs/adr/0027).
/// Bound from the <c>Foundry</c> configuration section.
/// </summary>
public sealed class FoundryOptions
{
    public const string SectionName = "Foundry";

    /// <summary>The Foundry (Azure OpenAI) endpoint, e.g. https://my-resource.openai.azure.com/.</summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// A key, for local development only. When null the gateway
    /// authenticates with Entra managed identity, which is what production
    /// uses: docs/adr/0027 says the factory never holds a model vendor's
    /// API key in production, and "never" has to be enforced somewhere.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>The resource name reported in the factory's own df.describe (non-secret).</summary>
    public string? ResourceName { get; set; }

    /// <summary>
    /// Role name → Foundry deployment name. Callers ask for "architect";
    /// this is where that becomes a concrete deployment, so changing the
    /// model behind a role is configuration (docs/adr/0022, docs/adr/0027).
    /// A role with no mapping falls through to its own name, which is the
    /// convention when deployments are named after roles directly.
    /// </summary>
    public Dictionary<string, string> Deployments { get; set; } = [];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint);

    public string ResolveDeployment(string role) =>
        Deployments.TryGetValue(role, out var deployment) && !string.IsNullOrWhiteSpace(deployment)
            ? deployment
            : role;
}
