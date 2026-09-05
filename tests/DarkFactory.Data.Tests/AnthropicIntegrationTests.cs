using DarkFactory.Anthropic;
using DarkFactory.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DarkFactory.Data.Tests;

/// <summary>
/// The Anthropic-direct counterpart to <see cref="FoundryIntegrationTests"/>,
/// and opt-in for the same reason: it is the only test here that talks to a
/// real model, and a suite whose results depend on what a model felt like
/// saying today is not a suite.
///
/// It proves what a fake structurally cannot — that authentication works,
/// that a round trip completes, and that usage comes back so
/// docs/adr/0028's budgets have something real to count.
///
/// Run it with:
///   DARKFACTORY_ANTHROPIC_INTEGRATION=1 \
///   Anthropic__ApiKey=... \
///   Anthropic__Deployment=...   (a role name; defaults to "architect") \
///   dotnet test --filter FullyQualifiedName~AnthropicIntegrationTests
/// </summary>
public sealed class AnthropicIntegrationTests
{
    private const string EnableVariable = "DARKFACTORY_ANTHROPIC_INTEGRATION";

    private static bool Enabled =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnableVariable));

    private static AnthropicOptions OptionsFromEnvironment()
    {
        var options = new AnthropicOptions
        {
            ApiKey = Environment.GetEnvironmentVariable("Anthropic__ApiKey"),
        };

        var baseUrl = Environment.GetEnvironmentVariable("Anthropic__BaseUrl");
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            options.BaseUrl = baseUrl;
        }

        return options;
    }

    [SkippableFact]
    public async Task ARealDeploymentAuthenticatesAndAnswers()
    {
        Skip.IfNot(Enabled, $"Set {EnableVariable}=1 and Anthropic__ApiKey to run this.");

        var options = OptionsFromEnvironment();
        Skip.If(!options.IsConfigured, "Anthropic__ApiKey is not set.");

        var role = Environment.GetEnvironmentVariable("Anthropic__Deployment") ?? AgentRoles.Architect;

        IModelGateway gateway = new AnthropicModelGateway(
            Options.Create(options), new HttpClient(), NullLogger<AnthropicModelGateway>.Instance);

        var completion = await gateway.CompleteAsync(new ModelRequest(
            Deployment: role,
            SystemPrompt: "Reply with the single word: ok",
            Messages: [new ModelMessage(ModelRole.User, "ready?")],
            // Generous for a one-word answer, for the same reason as the
            // Foundry test: a reasoning model can spend its whole output
            // budget thinking and return empty content, failing a round trip
            // that actually worked.
            MaxOutputTokens: 512));

        Assert.True(completion.Usage.TotalTokens > 0, "the provider reported no token usage");

        Assert.False(string.IsNullOrWhiteSpace(completion.Text),
            $"role '{role}' (model '{options.ResolveModel(role)}') authenticated and billed " +
            $"{completion.Usage.InputTokens}+{completion.Usage.OutputTokens} tokens but returned no text. " +
            "Auth and routing are fine; this is the model producing nothing.");

        // The gateway echoes the *role*, not the model id — a caller asked
        // for "architect" and an audit record should say so. The model is a
        // configuration detail that may change under it.
        Assert.Equal(role, completion.Deployment);
    }

    [Fact]
    public void TheGatewayRefusesToConstructWithoutAKey()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new AnthropicModelGateway(
            Options.Create(new AnthropicOptions()), new HttpClient(),
            NullLogger<AnthropicModelGateway>.Instance));

        Assert.Contains("ApiKey is not configured", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AgentRoles.Architect)]
    [InlineData(AgentRoles.Planner)]
    [InlineData(AgentRoles.Implementer)]
    [InlineData(AgentRoles.Reviewer)]
    [InlineData(AgentRoles.Router)]
    public void EveryRoleTheTeamTemplateSeedsResolvesToAModel(string role)
    {
        // The team template seeds these five, so a project would be created
        // with an assignment pointing at a role the gateway cannot serve if
        // any of them were missing — a failure that would surface halfway
        // through a run rather than at startup.
        Assert.False(string.IsNullOrWhiteSpace(new AnthropicOptions { ApiKey = "k" }.ResolveModel(role)));
    }

    [Fact]
    public void ConfigurationOverridesTheDefaultPolicy()
    {
        var options = new AnthropicOptions
        {
            ApiKey = "k",
            Deployments = { [AgentRoles.Implementer] = "claude-something-else" },
        };

        // docs/adr/0022: which model does which work is configuration.
        Assert.Equal("claude-something-else", options.ResolveModel(AgentRoles.Implementer));
        // ...and the roles that were not overridden keep the default policy.
        Assert.Equal(
            AnthropicOptions.DefaultDeployments[AgentRoles.Architect],
            options.ResolveModel(AgentRoles.Architect));
    }

    [Fact]
    public void AnUnknownRoleIsAnErrorRatherThanAGuess()
    {
        var ex = Assert.Throws<ModelGatewayException>(
            () => new AnthropicOptions { ApiKey = "k" }.ResolveModel("archivist"));

        // Silently inventing a model is how a run gets served by something
        // nobody chose, at a price nobody expected.
        Assert.Equal(FailureClass.NeedsHuman, ex.Failure);
        Assert.Contains("archivist", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDefaultPolicyPutsTheStrongestModelWhereFailureWouldGoUnnoticed()
    {
        var defaults = AnthropicOptions.DefaultDeployments;

        // docs/adr/0022's actual criterion is verifiability, not seniority:
        // spec amendments and completion judgment fail in ways no test
        // catches, so they get the strongest model; implementation is
        // checked by a test suite, so it does not have to.
        Assert.Equal(defaults[AgentRoles.Architect], defaults[AgentRoles.Reviewer]);
        Assert.NotEqual(defaults[AgentRoles.Architect], defaults[AgentRoles.Implementer]);
        Assert.NotEqual(defaults[AgentRoles.Implementer], defaults[AgentRoles.Router]);
    }
}
