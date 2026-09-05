using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DarkFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DarkFactory.Anthropic;

/// <summary>
/// <see cref="IModelGateway"/> against the Anthropic Messages API
/// (docs/adr/0027).
///
/// The development provider. Foundry remains the production target and the
/// bring-your-own story; this exists so the factory can be built and
/// demonstrated without a provisioned Foundry resource, and it earns its
/// place by going through the same interface. Nothing above the gateway can
/// tell which of the two served a call — that is the whole test of whether
/// ADR-0027's abstraction is real or merely asserted.
///
/// Implemented over HttpClient rather than an SDK deliberately: the Messages
/// API is one JSON POST, and owning the request means owning how usage is
/// reported, which is what the budgets in docs/adr/0028 count.
/// </summary>
public sealed class AnthropicModelGateway : IModelGateway
{
    /// <summary>Recorded on every usage fact row (docs/adr/0032).</summary>
    public const string ProviderName = "anthropic";

    private readonly AnthropicOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger<AnthropicModelGateway> _logger;

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public AnthropicModelGateway(
        IOptions<AnthropicOptions> options,
        HttpClient http,
        ILogger<AnthropicModelGateway> logger)
    {
        _options = options.Value;
        _http = http;
        _logger = logger;

        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException(
                $"{AnthropicOptions.SectionName}:ApiKey is not configured. " +
                "Set it, or register a different IModelGateway.");
        }

        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = _options.Timeout;
        _http.DefaultRequestHeaders.Add("x-api-key", _options.ApiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", _options.ApiVersion);

        _logger.LogInformation(
            "Anthropic gateway ready against {BaseUrl}. This is the development provider; " +
            "production targets Foundry (docs/adr/0027).", _options.BaseUrl);
    }

    public async Task<ModelCompletion> CompleteAsync(
        ModelRequest request, CancellationToken cancellationToken = default)
    {
        var model = _options.ResolveModel(request.Deployment);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var payload = new MessagesRequest
        {
            Model = model,
            MaxTokens = request.MaxOutputTokens,
            System = request.SystemPrompt,
            Temperature = request.Temperature,
            Messages = request.Messages
                .Select(m => new MessagesRequestMessage
                {
                    Role = m.Role == ModelRole.Assistant ? "assistant" : "user",
                    Content = m.Content,
                })
                .ToList(),
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync("v1/messages", payload, Json, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ModelGatewayException(
                $"Anthropic model '{model}' could not be reached: {ex.Message}", FailureClass.Retryable, ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ModelGatewayException(
                $"Anthropic model '{model}' failed with HTTP {(int)response.StatusCode}: {Truncate(body)}",
                Classify(response.StatusCode));
        }

        var completion = await response.Content.ReadFromJsonAsync<MessagesResponse>(Json, cancellationToken)
            ?? throw new ModelGatewayException(
                $"Anthropic model '{model}' returned an empty body.", FailureClass.Retryable);

        var text = string.Concat(completion.Content
            .Where(block => block.Type == "text" && block.Text is not null)
            .Select(block => block.Text));

        // Reported exactly as the Foundry gateway reports it, because
        // docs/adr/0028's budgets sum across providers without knowing or
        // caring which one produced a row.
        //
        // Thinking tokens are counted from the response's thinking blocks
        // rather than read from a usage field, because the Messages API
        // folds them into output_tokens — so this is a breakdown of that
        // total, never an addition to it.
        var thinking = completion.Content
            .Where(block => block.Type is "thinking" or "redacted_thinking")
            .Sum(block => Estimate(block.Thinking ?? block.Text));

        var usage = new ModelUsage(
            InputTokens: completion.Usage?.InputTokens ?? 0,
            OutputTokens: completion.Usage?.OutputTokens ?? 0,
            CachedInputTokens: completion.Usage?.CacheReadInputTokens ?? 0,
            CacheWriteInputTokens: completion.Usage?.CacheCreationInputTokens ?? 0,
            ThinkingTokens: thinking);

        // The deployment name, not the model id: a caller asked for
        // "architect" and an audit record should say what was asked for as
        // well as what answered. The model id is in the log.
        _logger.LogDebug(
            "Anthropic {Role} -> {Model}: {Input}+{Output} tokens " +
            "(cached {Cached}, cache-write {CacheWrite}, thinking ~{Thinking}), stop_reason={StopReason}",
            request.Deployment, model, usage.InputTokens, usage.OutputTokens,
            usage.CachedInputTokens, usage.CacheWriteInputTokens, usage.ThinkingTokens, completion.StopReason);

        return new ModelCompletion(text, usage, request.Deployment)
        {
            LatencyMs = stopwatch.ElapsedMilliseconds,
            Provider = ProviderName,
            ModelFamily = model,
        };
    }

    /// <summary>
    /// docs/adr/0007, so the engine's existing retry policy governs model
    /// calls here the same way it does on Foundry. 429 and 5xx are the
    /// provider asking us to come back; 401 and 403 are configuration a
    /// person has to fix; the rest is us being wrong.
    /// </summary>
    private static FailureClass Classify(HttpStatusCode status) => status switch
    {
        HttpStatusCode.TooManyRequests => FailureClass.Retryable,
        >= HttpStatusCode.InternalServerError => FailureClass.Retryable,
        HttpStatusCode.RequestTimeout => FailureClass.Retryable,
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => FailureClass.NeedsHuman,
        _ => FailureClass.Permanent,
    };

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500] + "…";

    /// <summary>
    /// Approximates the tokens in a thinking block. The Messages API does
    /// not break thinking out of <c>output_tokens</c>, so an exact figure is
    /// not available without a tokenizer — and shipping a tokenizer to
    /// attribute a sub-total we already have in aggregate would be a poor
    /// trade. Deliberately an estimate, and only ever used for attribution
    /// within a total the provider gave us: budgets count
    /// <see cref="ModelUsage.TotalTokens"/>, which is exact.
    /// </summary>
    private static int Estimate(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / 4.0);

    // ---- wire types -------------------------------------------------------

    private sealed class MessagesRequest
    {
        [JsonPropertyName("model")] public required string Model { get; init; }
        [JsonPropertyName("max_tokens")] public required int MaxTokens { get; init; }
        [JsonPropertyName("system")] public string? System { get; init; }
        [JsonPropertyName("temperature")] public double? Temperature { get; init; }
        [JsonPropertyName("messages")] public required List<MessagesRequestMessage> Messages { get; init; }
    }

    private sealed class MessagesRequestMessage
    {
        [JsonPropertyName("role")] public required string Role { get; init; }
        [JsonPropertyName("content")] public required string Content { get; init; }
    }

    private sealed class MessagesResponse
    {
        [JsonPropertyName("content")] public List<ContentBlock> Content { get; init; } = [];
        [JsonPropertyName("usage")] public UsageBlock? Usage { get; init; }
        [JsonPropertyName("stop_reason")] public string? StopReason { get; init; }
    }

    private sealed class ContentBlock
    {
        [JsonPropertyName("type")] public string? Type { get; init; }
        [JsonPropertyName("text")] public string? Text { get; init; }
        [JsonPropertyName("thinking")] public string? Thinking { get; init; }
    }

    private sealed class UsageBlock
    {
        [JsonPropertyName("input_tokens")] public int InputTokens { get; init; }
        [JsonPropertyName("output_tokens")] public int OutputTokens { get; init; }
        [JsonPropertyName("cache_read_input_tokens")] public int? CacheReadInputTokens { get; init; }
        [JsonPropertyName("cache_creation_input_tokens")] public int? CacheCreationInputTokens { get; init; }
    }
}
