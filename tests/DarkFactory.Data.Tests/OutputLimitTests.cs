using DarkFactory.Anthropic;
using DarkFactory.Core;
using DarkFactory.Foundry;

namespace DarkFactory.Data.Tests;

/// <summary>
/// The output limit is resolved against the model (docs/adr/0027), with the
/// per-member column kept as an override rather than as the source of the
/// number.
///
/// A hardcoded 8192 is what truncated the implementer's first attempt in the
/// 3d acceptance demo: it returns whole file contents, the response was cut
/// mid-JSON, and the run paid for a retry that existed only because of a
/// number nobody had chosen for that member. These tests pin both halves —
/// null means the model's own ceiling, and an explicit override still wins,
/// which is what keeps a Foundry-served member configurable at all.
/// </summary>
public sealed class OutputLimitTests
{
    [Fact]
    public void NullMeansTheModelsMaximumRatherThanADefaultNumber()
    {
        var options = new AnthropicOptions { ApiKey = "test" };

        // The API's own numbers, from GET /v1/models/{id}.max_tokens. If
        // these ever disagree with the API, this test is what should be
        // updated — after checking, not before.
        Assert.Equal(128_000, options.ResolveMaxOutputTokens("claude-opus-5", requested: null));
        Assert.Equal(128_000, options.ResolveMaxOutputTokens("claude-sonnet-5", requested: null));
        Assert.Equal(64_000, options.ResolveMaxOutputTokens("claude-haiku-4-5-20251001", requested: null));
    }

    [Fact]
    public void TheImplementersDefaultIsFarAboveTheOldTruncatingCap()
    {
        var options = new AnthropicOptions { ApiKey = "test" };
        var implementerModel = AnthropicOptions.DefaultDeployments[AgentRoles.Implementer];

        var resolved = options.ResolveMaxOutputTokens(implementerModel, requested: null);

        // The regression this exists to prevent: the member that emits whole
        // files capped low enough to be cut off mid-artifact.
        Assert.True(resolved > 8192, $"{implementerModel} resolved to {resolved}, the old truncating cap.");
    }

    [Fact]
    public void AnExplicitOverrideStillWins()
    {
        var options = new AnthropicOptions { ApiKey = "test" };

        // Without this the column is decorative, and a Foundry-served member
        // would have no way to raise its own ceiling.
        Assert.Equal(4096, options.ResolveMaxOutputTokens("claude-sonnet-5", requested: 4096));
    }

    [Fact]
    public void AnOverrideAboveTheModelsCeilingIsClampedRatherThanRejected()
    {
        var options = new AnthropicOptions { ApiKey = "test" };

        // "As much as possible", expressed clumsily. Failing the call over it
        // would help nobody.
        Assert.Equal(128_000, options.ResolveMaxOutputTokens("claude-sonnet-5", requested: 999_999));
    }

    [Fact]
    public void AnUnknownModelFallsBackConservativelyAndSaysSo()
    {
        var options = new AnthropicOptions { ApiKey = "test" };

        // Guessing high on a model whose real ceiling is lower fails the whole
        // call with a 400; guessing low costs a shorter answer that now names
        // itself as truncated. `known` is what lets the gateway warn, so a
        // newly-configured model does not quietly reintroduce truncation.
        var resolved = options.ResolveMaxOutputTokens(
            "some-model-shipped-after-this-build", requested: null, out var known);

        Assert.Equal(AnthropicOptions.UnknownModelMaxOutputTokens, resolved);
        Assert.False(known);
    }

    [Fact]
    public void AConfiguredCeilingOverridesTheBuiltInTable()
    {
        var options = new AnthropicOptions
        {
            ApiKey = "test",
            MaxOutputTokens = { ["claude-sonnet-5"] = 32_000 },
        };

        Assert.Equal(32_000, options.ResolveMaxOutputTokens("claude-sonnet-5", requested: null, out var known));
        Assert.True(known);
    }

    [Fact]
    public void FoundrySendsNoCeilingItCannotHonestlyKnow()
    {
        // A Foundry deployment name says nothing about the model behind it
        // (docs/adr/0027), so there is no table to write and the provider's
        // own default is the right answer.
        var options = new FoundryOptions { Endpoint = "https://example.openai.azure.com/" };

        Assert.Null(options.ResolveMaxOutputTokens("implementer", requested: null));
    }

    [Fact]
    public void AFoundryMemberCanStillRaiseItsOwnCeiling()
    {
        // The reason the per-member column survives: on Foundry it is the
        // only lever there is.
        var options = new FoundryOptions { Endpoint = "https://example.openai.azure.com/" };

        Assert.Equal(64_000, options.ResolveMaxOutputTokens("implementer", requested: 64_000));
    }

    [Fact]
    public void EveryBuiltInTeamMemberDefersToTheModelsMaximum()
    {
        // No member carries a number, because the template cannot name one
        // without reaching across the boundary that keeps it ignorant of
        // models. Including the implementer, which previously did.
        Assert.All(TeamService.DefaultTemplate, member => Assert.Null(member.MaxOutputTokens));
    }
}
