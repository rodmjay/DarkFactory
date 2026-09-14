namespace DarkFactory.Core;

/// <summary>
/// What a model call cost, in USD, from the usage the provider reported
/// (docs/adr/0032). docs/adr/0027 keeps provider metering the source of
/// truth for billing; this is the factory's own estimate, so a cost report
/// exists without querying the provider.
///
/// Anthropic first-party list prices per million tokens. A cache read is
/// billed at a tenth of input, a cache write at 1.25× — the five-minute
/// rate, because the gateway marks its breakpoints <c>ephemeral</c> with no
/// TTL. A model not listed has no cost rather than a guessed one.
/// </summary>
public static class ModelPricing
{
    private sealed record Rate(decimal InputPerMillion, decimal OutputPerMillion);

    // Longest prefix first, so a dated id (claude-haiku-4-5-20251001) finds its family.
    private static readonly (string Prefix, Rate Rate)[] Rates =
    [
        ("claude-opus-5", new Rate(5m, 25m)),
        ("claude-sonnet-5", new Rate(2m, 10m)),
        ("claude-haiku-4-5", new Rate(1m, 5m)),
    ];

    public const decimal CacheReadMultiplier = 0.1m;
    public const decimal CacheWriteMultiplier = 1.25m;

    /// <summary>
    /// Null when the model has no listed price. <see cref="ModelUsage"/>'s
    /// cached and cache-write counts are portions of its input, so the
    /// fresh input is what remains once both are taken out.
    /// </summary>
    public static decimal? CostUsd(string? model, ModelUsage usage)
    {
        if (model is null)
        {
            return null;
        }

        var match = Rates.FirstOrDefault(r => model.StartsWith(r.Prefix, StringComparison.Ordinal));
        if (match.Rate is null)
        {
            return null;
        }

        var rate = match.Rate;
        var fresh = Math.Max(usage.InputTokens - usage.CachedInputTokens - usage.CacheWriteInputTokens, 0);
        var perMillion =
            fresh * rate.InputPerMillion
            + usage.CachedInputTokens * rate.InputPerMillion * CacheReadMultiplier
            + usage.CacheWriteInputTokens * rate.InputPerMillion * CacheWriteMultiplier
            + usage.OutputTokens * rate.OutputPerMillion;

        return Math.Round(perMillion / 1_000_000m, 6);
    }
}
