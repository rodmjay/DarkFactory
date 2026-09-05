namespace DarkFactory.Foundry;

/// <summary>
/// Which wire API a Foundry resource is serving its deployments over.
///
/// This distinction is invisible above <c>IModelGateway</c> and must stay
/// that way (docs/adr/0027) — but it is emphatically not invisible *here*.
/// A resource serving GPT deployments and a resource serving Claude
/// deployments are reached through different endpoints with different SDKs,
/// and pointing the wrong one at the right resource fails in a way that
/// looks exactly like an authentication problem.
/// </summary>
public enum FoundryApi
{
    /// <summary>Infer from the endpoint host. Right almost always; overridable when it isn't.</summary>
    Detect = 0,

    /// <summary>Azure OpenAI: <c>https://{resource}.openai.azure.com/</c>, OpenAI-shaped deployments.</summary>
    AzureOpenAI,

    /// <summary>
    /// Foundry Models unified inference:
    /// <c>https://{resource}.services.ai.azure.com/models</c>. Serves every
    /// model family Foundry hosts — Claude included — behind one shape.
    /// </summary>
    FoundryInference,
}

/// <summary>
/// Configuration for the Microsoft Foundry gateway (docs/adr/0027).
/// Bound from the <c>Foundry</c> configuration section.
/// </summary>
public sealed class FoundryOptions
{
    public const string SectionName = "Foundry";

    /// <summary>
    /// The Foundry endpoint. Its shape determines which API is used unless
    /// <see cref="Api"/> says otherwise:
    /// <c>https://x.openai.azure.com/</c> is Azure OpenAI;
    /// <c>https://x.services.ai.azure.com/models</c> is Foundry Models.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>Override endpoint-shape detection. Leave as <see cref="FoundryApi.Detect"/> unless it guesses wrong.</summary>
    public FoundryApi Api { get; set; } = FoundryApi.Detect;

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

    /// <summary>
    /// The API actually in use. Detection reads the host rather than the
    /// path, because the path varies and the host does not.
    /// </summary>
    public FoundryApi ResolveApi()
    {
        if (Api != FoundryApi.Detect)
        {
            return Api;
        }

        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"{SectionName}:Endpoint '{Endpoint}' is not an absolute URL, so the API cannot be detected. " +
                $"Set {SectionName}:Api explicitly to AzureOpenAI or FoundryInference.");
        }

        var host = uri.Host;

        if (host.EndsWith(".openai.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            return FoundryApi.AzureOpenAI;
        }

        if (host.EndsWith(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".inference.ai.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            return FoundryApi.FoundryInference;
        }

        throw new InvalidOperationException(
            $"Cannot tell which API '{uri}' serves. Azure OpenAI endpoints end in .openai.azure.com; " +
            $"Foundry Models endpoints end in .services.ai.azure.com. Set {SectionName}:Api explicitly " +
            "(AzureOpenAI or FoundryInference) if you are using a custom domain or a private endpoint.");
    }

    /// <summary>
    /// The endpoint the Foundry Models client wants. The portal shows the
    /// resource root; the inference client expects the <c>/models</c> path,
    /// and forgetting it produces a 404 that reads like a missing
    /// deployment.
    /// </summary>
    public Uri ResolveInferenceEndpoint()
    {
        var uri = new Uri(Endpoint!, UriKind.Absolute);
        return uri.AbsolutePath.TrimEnd('/').EndsWith("/models", StringComparison.OrdinalIgnoreCase)
            ? uri
            : new Uri(uri, "/models");
    }
}
