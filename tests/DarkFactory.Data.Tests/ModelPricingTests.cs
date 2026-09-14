using DarkFactory.Anthropic;
using DarkFactory.Core;

namespace DarkFactory.Data.Tests;

/// <summary>
/// docs/adr/0032: a model call's cost, from the usage the provider
/// reported. Two things are under test — that the Anthropic gateway counts
/// cached input as input at all, and that each portion is priced at its
/// own rate — because either being wrong under-counts, the direction
/// nobody notices.
/// </summary>
public sealed class ModelPricingTests
{
    [Fact]
    public void CachedInputIsInputTheApiReportsBesideInputTokensNotInsideThem()
    {
        // An intake extraction: a cached corpus of 100k tokens, a small
        // uncached tail, one cache write.
        var usage = AnthropicModelGateway.ToUsage(
            inputTokens: 2_000, cacheReadInputTokens: 100_000, cacheCreationInputTokens: 5_000, outputTokens: 800, thinking: 0);

        Assert.Equal(107_000, usage.InputTokens);
        Assert.Equal(100_000, usage.CachedInputTokens);
        Assert.Equal(5_000, usage.CacheWriteInputTokens);
        Assert.Equal(107_800, usage.TotalTokens);
    }

    [Fact]
    public void EachPortionIsPricedAtItsOwnRate()
    {
        // Opus 5: $5 in, $25 out per million; reads 0.1x, writes 1.25x.
        // 180 fresh x 5 + 700 read x 0.5 + 120 written x 6.25 + 300 out x 25 = 9,500 per million.
        var usage = new ModelUsage(InputTokens: 1_000, OutputTokens: 300, CachedInputTokens: 700, CacheWriteInputTokens: 120);
        Assert.Equal(0.0095m, ModelPricing.CostUsd("claude-opus-5", usage));
    }

    [Theory]
    [InlineData("claude-opus-5", 30.0)]
    [InlineData("claude-sonnet-5", 12.0)]
    [InlineData("claude-haiku-4-5-20251001", 6.0)]
    public void AMillionInAndAMillionOutCostsTheListPrice(string model, double expected)
    {
        var usage = new ModelUsage(InputTokens: 1_000_000, OutputTokens: 1_000_000);
        Assert.Equal((decimal)expected, ModelPricing.CostUsd(model, usage));
    }

    [Theory]
    [InlineData("fake-model")]
    [InlineData("gpt-4o")]
    [InlineData(null)]
    public void AModelWithNoListedPriceHasNoCostRatherThanAGuess(string? model) =>
        Assert.Null(ModelPricing.CostUsd(model, new ModelUsage(1_000, 1_000)));
}
