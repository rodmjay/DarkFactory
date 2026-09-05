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
///   Foundry__Endpoint=https://your-resource.openai.azure.com/ \
///   Foundry__ApiKey=...  (omit to use managed identity) \
///   dotnet test --filter FullyQualifiedName~FoundryIntegrationTests
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
        };

        Skip.If(!options.IsConfigured, "Foundry__Endpoint is not set.");

        var deployment = Environment.GetEnvironmentVariable("Foundry__Deployment") ?? AgentRoles.Architect;

        IModelGateway gateway = new FoundryModelGateway(
            Options.Create(options), NullLogger<FoundryModelGateway>.Instance);

        var completion = await gateway.CompleteAsync(new ModelRequest(
            Deployment: deployment,
            SystemPrompt: "Reply with exactly the word: ok",
            Messages: [new ModelMessage(ModelRole.User, "ready?")],
            MaxOutputTokens: 16));

        Assert.False(string.IsNullOrWhiteSpace(completion.Text));
        Assert.True(completion.Usage.TotalTokens > 0, "the provider reported no token usage");

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
            Deployments = { ["planner"] = "planner-gpt5-eastus" },
        };

        Assert.Equal(expected, options.ResolveDeployment(role));
    }
}
