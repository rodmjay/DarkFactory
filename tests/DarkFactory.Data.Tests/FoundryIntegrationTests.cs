using DarkFactory.Core;
using DarkFactory.Foundry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DarkFactory.Data.Tests;

/// <summary>
/// The one test in the suite that talks to a real model, and it is opt-in.
///
/// It exists to prove two things the fake gateway structurally cannot:
/// that authentication actually works, and that a single round trip
/// actually completes. Everything about the factory's own behaviour —
/// context assembly, validation, the retry, what gets persisted — is tested
/// against canned responses, because those tests must not fail because a
/// model had a different idea today.
///
/// Run it with:
///   DARKFACTORY_FOUNDRY_INTEGRATION=1 \
///   Foundry__Endpoint=&lt;your endpoint&gt; \
///   Foundry__ApiKey=...        (omit to use managed identity) \
///   Foundry__Deployment=...    (defaults to "architect") \
///   dotnet test --filter FullyQualifiedName~FoundryIntegrationTests
///
/// The endpoint decides which client is used, and the two are not
/// interchangeable: <c>https://{resource}.openai.azure.com/</c> for
/// OpenAI-family deployments, <c>https://{resource}.services.ai.azure.com/</c>
/// for everything Foundry Models serves, Claude included. Set
/// <c>Foundry__Api</c> to override detection. Getting this wrong produces a
/// 401 or 404 that reads like an authentication failure — which is exactly
/// what this test is for.
/// </summary>
public sealed class FoundryIntegrationTests
{
    private const string EnableVariable = "DARKFACTORY_FOUNDRY_INTEGRATION";

    private static bool Enabled =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnableVariable));

    [SkippableFact]
    public async Task ARealFoundryDeploymentAuthenticatesAndAnswers()
    {
        Skip.IfNot(Enabled,
            $"Set {EnableVariable}=1 (plus Foundry__Endpoint and, for local runs, Foundry__ApiKey) to run this.");

        var options = new FoundryOptions
        {
            Endpoint = Environment.GetEnvironmentVariable("Foundry__Endpoint"),
            // Absent means managed identity, which is what production uses
            // (docs/adr/0027). Both paths go through the same gateway.
            ApiKey = Environment.GetEnvironmentVariable("Foundry__ApiKey"),
            Api = Enum.TryParse<FoundryApi>(Environment.GetEnvironmentVariable("Foundry__Api"), out var api)
                ? api
                : FoundryApi.Detect,
        };

        Skip.If(!options.IsConfigured, "Foundry__Endpoint is not set.");

        var deployment = Environment.GetEnvironmentVariable("Foundry__Deployment") ?? AgentRoles.Architect;

        IModelGateway gateway = new FoundryModelGateway(
            Options.Create(options), NullLogger<FoundryModelGateway>.Instance);

        var completion = await gateway.CompleteAsync(new ModelRequest(
            Deployment: deployment,
            SystemPrompt: "Reply with the single word: ok",
            Messages: [new ModelMessage(ModelRole.User, "ready?")],
            // Deliberately generous for a one-word answer. A reasoning
            // model can spend its entire output budget thinking and return
            // empty content, which would fail this test on a round trip
            // that actually worked — and this test is the gate for whether
            // anything downstream means anything, so a false negative here
            // is far more expensive than a few wasted tokens.
            MaxOutputTokens: 512));

        Assert.True(completion.Usage.TotalTokens > 0, "the provider reported no token usage");

        Assert.False(string.IsNullOrWhiteSpace(completion.Text),
            $"deployment '{deployment}' authenticated and billed " +
            $"{completion.Usage.InputTokens}+{completion.Usage.OutputTokens} tokens but returned no text. " +
            "Auth and routing are fine; this is the model producing nothing, most likely reasoning tokens " +
            "consuming the output budget.");

        // The gateway reports which deployment served the call, which is
        // what an audit record needs after a fallback.
        Assert.Equal(options.ResolveDeployment(deployment), completion.Deployment);
    }

    [Fact]
    public void TheGatewayRefusesToConstructWithoutAnEndpoint()
    {
        // Not opt-in: this one needs no network. Silently doing nothing
        // when unconfigured is how a factory ends up calling a model
        // nobody chose.
        var ex = Assert.Throws<InvalidOperationException>(() => new FoundryModelGateway(
            Options.Create(new FoundryOptions()), NullLogger<FoundryModelGateway>.Instance));

        Assert.Contains("Endpoint is not configured", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheUnconfiguredGatewayFailsWithAnExplanationNotANullReference()
    {
        var ex = await Assert.ThrowsAsync<ModelGatewayException>(
            () => new UnconfiguredModelGateway().CompleteAsync(
                new ModelRequest("architect", "system", [new ModelMessage(ModelRole.User, "hi")])));

        Assert.Equal(FailureClass.NeedsHuman, ex.Failure);
        Assert.Contains("Foundry:Endpoint", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("architect", "architect")]
    [InlineData("planner", "planner-gpt5-eastus")]
    public void DeploymentNamesResolveThroughConfiguration(string role, string expected)
    {
        // The indirection docs/adr/0027 depends on: callers name a role,
        // configuration decides what that is, and swapping the model behind
        // it never touches code.
        var options = new FoundryOptions
        {
            Endpoint = "https://example.invalid/",
            Api = FoundryApi.AzureOpenAI,
            Deployments = { ["planner"] = "planner-gpt5-eastus" },
        };

        Assert.Equal(expected, options.ResolveDeployment(role));
    }

    // ---- which wire API a resource is serving -----------------------------

    [Theory]
    [InlineData("https://my-resource.openai.azure.com/", FoundryApi.AzureOpenAI)]
    [InlineData("https://MY-RESOURCE.OpenAI.Azure.Com/", FoundryApi.AzureOpenAI)]
    [InlineData("https://my-resource.services.ai.azure.com/models", FoundryApi.FoundryInference)]
    [InlineData("https://my-resource.services.ai.azure.com/", FoundryApi.FoundryInference)]
    [InlineData("https://my-resource.inference.ai.azure.com/", FoundryApi.FoundryInference)]
    public void TheApiIsDetectedFromTheEndpointShape(string endpoint, FoundryApi expected)
    {
        // GPT deployments and Claude deployments live behind different
        // endpoints with different clients. Pointing the wrong one at the
        // right resource fails as a 401 or 404, which looks exactly like a
        // credential problem and is not one.
        Assert.Equal(expected, new FoundryOptions { Endpoint = endpoint }.ResolveApi());
    }

    [Fact]
    public void AnExplicitApiOverridesDetection()
    {
        var options = new FoundryOptions
        {
            Endpoint = "https://my-resource.openai.azure.com/",
            Api = FoundryApi.FoundryInference,
        };

        // Custom domains and private endpoints exist; detection must be
        // overridable rather than authoritative.
        Assert.Equal(FoundryApi.FoundryInference, options.ResolveApi());
    }

    [Fact]
    public void AnUnrecognisedEndpointSaysWhatToDoAboutIt()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new FoundryOptions { Endpoint = "https://models.example.com/" }.ResolveApi());

        Assert.Contains("openai.azure.com", ex.Message, StringComparison.Ordinal);
        Assert.Contains("services.ai.azure.com", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Foundry:Api", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://r.services.ai.azure.com/", "https://r.services.ai.azure.com/models")]
    [InlineData("https://r.services.ai.azure.com/models", "https://r.services.ai.azure.com/models")]
    [InlineData("https://r.services.ai.azure.com/models/", "https://r.services.ai.azure.com/models/")]
    public void TheInferenceEndpointGetsTheModelsPathItNeeds(string configured, string expected)
    {
        // The portal shows the resource root; the inference client wants
        // /models, and omitting it 404s in a way that reads like a missing
        // deployment.
        var options = new FoundryOptions { Endpoint = configured, Api = FoundryApi.FoundryInference };
        Assert.Equal(expected, options.ResolveInferenceEndpoint().ToString());
    }

    [Fact]
    public void ConstructingAgainstEitherApiSelectsTheMatchingClient()
    {
        // No network: this only proves the constructor takes the branch the
        // endpoint implies, which is the part that is easy to get wrong and
        // impossible to notice until a call fails.
        var openAi = new FoundryModelGateway(
            Options.Create(new FoundryOptions
            {
                Endpoint = "https://r.openai.azure.com/",
                ApiKey = "local-dev-key",
            }),
            NullLogger<FoundryModelGateway>.Instance);
        Assert.Equal(FoundryApi.AzureOpenAI, openAi.Api);

        var inference = new FoundryModelGateway(
            Options.Create(new FoundryOptions
            {
                Endpoint = "https://r.services.ai.azure.com/",
                ApiKey = "local-dev-key",
            }),
            NullLogger<FoundryModelGateway>.Instance);
        Assert.Equal(FoundryApi.FoundryInference, inference.Api);
    }
}
