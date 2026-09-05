using Azure;
using Azure.AI.Inference;
using Azure.AI.OpenAI;
using Azure.Identity;
using DarkFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace DarkFactory.Foundry;

/// <summary>
/// <see cref="IModelGateway"/> against Microsoft Foundry (docs/adr/0027).
///
/// This assembly is the only place in the solution that references a model
/// provider's SDK — and, since Foundry serves different model families over
/// different wire APIs, it is the only place that knows there is more than
/// one. Everything above names a role and never learns what served it.
///
/// The two paths exist because a Foundry resource hosting GPT deployments
/// and one hosting Claude deployments are genuinely different endpoints
/// with different clients. Getting that wrong produces a 401 or a 404 that
/// reads like an authentication failure, which is a bad hour to spend, so
/// the selection is explicit and the error message says what it picked.
/// </summary>
public sealed class FoundryModelGateway : IModelGateway
{
    /// <summary>Recorded on every usage fact row (docs/adr/0032).</summary>
    public const string ProviderName = "foundry";

    private readonly FoundryOptions _options;
    private readonly FoundryApi _api;
    private readonly ILogger<FoundryModelGateway> _logger;

    private readonly AzureOpenAIClient? _openAi;
    private readonly ChatCompletionsClient? _inference;

    public FoundryModelGateway(
        IOptions<FoundryOptions> options,
        ILogger<FoundryModelGateway> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException(
                $"{FoundryOptions.SectionName}:Endpoint is not configured. " +
                "Set it, or register a different IModelGateway.");
        }

        _api = _options.ResolveApi();
        var usingKey = !string.IsNullOrWhiteSpace(_options.ApiKey);

        if (usingKey)
        {
            // Local development only — same gateway, same call path, so
            // nothing above it behaves differently between the two.
            _logger.LogWarning(
                "Foundry gateway using a key against {Endpoint} via {Api}. This is for local development; " +
                "production authenticates with managed identity (docs/adr/0027).", _options.Endpoint, _api);
        }
        else
        {
            // docs/adr/0027: the factory authenticates to Foundry with
            // Entra managed identity and never holds a model vendor's key.
            _logger.LogInformation(
                "Foundry gateway using managed identity against {Endpoint} via {Api}", _options.Endpoint, _api);
        }

        switch (_api)
        {
            case FoundryApi.AzureOpenAI:
                var openAiEndpoint = new Uri(_options.Endpoint!);
                _openAi = usingKey
                    ? new AzureOpenAIClient(openAiEndpoint, new AzureKeyCredential(_options.ApiKey!))
                    : new AzureOpenAIClient(openAiEndpoint, new DefaultAzureCredential());
                break;

            case FoundryApi.FoundryInference:
                var inferenceEndpoint = _options.ResolveInferenceEndpoint();
                _inference = usingKey
                    ? new ChatCompletionsClient(inferenceEndpoint, new AzureKeyCredential(_options.ApiKey!))
                    : new ChatCompletionsClient(inferenceEndpoint, new DefaultAzureCredential());
                break;

            default:
                throw new InvalidOperationException($"Unsupported Foundry API '{_api}'.");
        }
    }

    /// <summary>Which API this instance resolved to. Surfaced for diagnostics; nothing above the gateway consumes it.</summary>
    public FoundryApi Api => _api;

    public async Task<ModelCompletion> CompleteAsync(
        ModelRequest request, CancellationToken cancellationToken = default)
    {
        var deployment = _options.ResolveDeployment(request.Deployment);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var completion = _api switch
            {
                FoundryApi.AzureOpenAI => CompleteViaAzureOpenAIAsync(request, deployment, cancellationToken),
                FoundryApi.FoundryInference => CompleteViaInferenceAsync(request, deployment, cancellationToken),
                _ => throw new InvalidOperationException($"Unsupported Foundry API '{_api}'."),
            };

            return (await completion) with
            {
                LatencyMs = stopwatch.ElapsedMilliseconds,
                Provider = ProviderName,
                ModelFamily = deployment,
            };
        }
        catch (RequestFailedException ex)
        {
            // Classified per docs/adr/0007 so the engine's existing retry
            // policy governs model calls too. 429 and 5xx are the provider
            // asking us to come back; 4xx is us being wrong.
            var failure = ex.Status is 429 or >= 500 ? FailureClass.Retryable : FailureClass.Permanent;

            // 401/404 against the wrong API is the single most likely
            // misconfiguration, and it does not announce itself — so the
            // message says which API was used and how to change it rather
            // than leaving someone to debug credentials that are fine.
            var hint = ex.Status is 401 or 403 or 404
                ? $" The gateway is using the {_api} API against '{_options.Endpoint}'. " +
                  "If the deployment is a different model family, this is the wrong endpoint rather than " +
                  $"a credential problem — check {FoundryOptions.SectionName}:Endpoint and set " +
                  $"{FoundryOptions.SectionName}:Api explicitly."
                : "";

            throw new ModelGatewayException(
                $"Foundry deployment '{deployment}' failed with HTTP {ex.Status}: {ex.Message}.{hint}", failure, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ModelGatewayException)
        {
            throw new ModelGatewayException(
                $"Foundry deployment '{deployment}' failed: {ex.Message}", FailureClass.Retryable, ex);
        }
    }

    /// <summary>Stable prefix first, so a provider doing automatic prefix caching can find one.</summary>
    private static string SystemPromptOf(ModelRequest request) =>
        string.IsNullOrWhiteSpace(request.CacheableSystemPrefix)
            ? request.SystemPrompt
            : request.CacheableSystemPrefix + "\n\n" + request.SystemPrompt;

    private async Task<ModelCompletion> CompleteViaAzureOpenAIAsync(
        ModelRequest request, string deployment, CancellationToken cancellationToken)
    {
        var chat = _openAi!.GetChatClient(deployment);

        // No explicit breakpoint: the Azure OpenAI path caches stable
        // prefixes automatically, so the two halves simply concatenate in
        // the order that makes the stable part come first.
        var messages = new List<ChatMessage> { new SystemChatMessage(SystemPromptOf(request)) };
        foreach (var message in request.Messages)
        {
            messages.Add(message.Role switch
            {
                ModelRole.Assistant => new AssistantChatMessage(message.Content),
                _ => new UserChatMessage(message.Content),
            });
        }

        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = _options.ResolveMaxOutputTokens(deployment, request.MaxOutputTokens),
        };
        if (request.Temperature is { } temperature)
        {
            options.Temperature = (float)temperature;
        }

        var response = await chat.CompleteChatAsync(messages, options, cancellationToken);
        var completion = response.Value;

        var text = string.Concat(completion.Content
            .Where(c => c.Kind == ChatMessageContentPartKind.Text)
            .Select(c => c.Text));

        return new ModelCompletion(
            text,
            new ModelUsage(
                InputTokens: completion.Usage?.InputTokenCount ?? 0,
                OutputTokens: completion.Usage?.OutputTokenCount ?? 0,
                // Both providers must populate the same breakdown, or a
                // per-dimension query in docs/adr/0032 silently means
                // different things depending on who served the call.
                CachedInputTokens: completion.Usage?.InputTokenDetails?.CachedTokenCount ?? 0,
                ThinkingTokens: completion.Usage?.OutputTokenDetails?.ReasoningTokenCount ?? 0),
            deployment);
    }

    private async Task<ModelCompletion> CompleteViaInferenceAsync(
        ModelRequest request, string deployment, CancellationToken cancellationToken)
    {
        var options = new ChatCompletionsOptions
        {
            Model = deployment,
            MaxTokens = _options.ResolveMaxOutputTokens(deployment, request.MaxOutputTokens),
        };

        options.Messages.Add(new ChatRequestSystemMessage(SystemPromptOf(request)));
        foreach (var message in request.Messages)
        {
            options.Messages.Add(message.Role switch
            {
                ModelRole.Assistant => new ChatRequestAssistantMessage(message.Content),
                _ => (ChatRequestMessage)new ChatRequestUserMessage(message.Content),
            });
        }

        if (request.Temperature is { } temperature)
        {
            options.Temperature = (float)temperature;
        }

        var response = await _inference!.CompleteAsync(options, cancellationToken);
        var completion = response.Value;

        return new ModelCompletion(
            completion.Content ?? "",
            new ModelUsage(
                InputTokens: completion.Usage?.PromptTokens ?? 0,
                OutputTokens: completion.Usage?.CompletionTokens ?? 0),
            deployment);
    }
}
