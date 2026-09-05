using Azure;
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
/// provider's SDK. Everything above the gateway names a role — "architect",
/// "planner" — and never learns what served it. That is why
/// <see cref="ModelRequest"/> carries no endpoint, key or model id.
/// </summary>
public sealed class FoundryModelGateway : IModelGateway
{
    private readonly FoundryOptions _options;
    private readonly AzureOpenAIClient _client;
    private readonly ILogger<FoundryModelGateway> _logger;

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

        var endpoint = new Uri(_options.Endpoint!);

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            // Production. docs/adr/0027: the factory authenticates to
            // Foundry with Entra managed identity and never holds a model
            // vendor's API key.
            _logger.LogInformation("Foundry gateway using managed identity against {Endpoint}", endpoint);
            _client = new AzureOpenAIClient(endpoint, new DefaultAzureCredential());
        }
        else
        {
            // Local development only — same gateway, same call path, so
            // nothing above it behaves differently between the two.
            _logger.LogWarning(
                "Foundry gateway using a key against {Endpoint}. This is for local development; " +
                "production authenticates with managed identity (docs/adr/0027).", endpoint);
            _client = new AzureOpenAIClient(endpoint, new AzureKeyCredential(_options.ApiKey));
        }
    }

    public async Task<ModelCompletion> CompleteAsync(
        ModelRequest request, CancellationToken cancellationToken = default)
    {
        var deployment = _options.ResolveDeployment(request.Deployment);
        var chat = _client.GetChatClient(deployment);

        var messages = new List<ChatMessage> { new SystemChatMessage(request.SystemPrompt) };
        foreach (var message in request.Messages)
        {
            messages.Add(message.Role switch
            {
                ModelRole.Assistant => new AssistantChatMessage(message.Content),
                _ => new UserChatMessage(message.Content),
            });
        }

        var chatOptions = new ChatCompletionOptions { MaxOutputTokenCount = request.MaxOutputTokens };
        if (request.Temperature is { } temperature)
        {
            chatOptions.Temperature = (float)temperature;
        }

        try
        {
            var response = await chat.CompleteChatAsync(messages, chatOptions, cancellationToken);
            var completion = response.Value;

            var text = string.Concat(completion.Content.Where(c => c.Kind == ChatMessageContentPartKind.Text)
                .Select(c => c.Text));

            return new ModelCompletion(
                text,
                new ModelUsage(
                    completion.Usage?.InputTokenCount ?? 0,
                    completion.Usage?.OutputTokenCount ?? 0),
                deployment);
        }
        catch (RequestFailedException ex)
        {
            // Classified per docs/adr/0007 so the engine's existing retry
            // policy governs model calls too. 429 and 5xx are the provider
            // asking us to come back; 4xx is us being wrong.
            var failure = ex.Status is 429 or >= 500 ? FailureClass.Retryable : FailureClass.Permanent;
            throw new ModelGatewayException(
                $"Foundry deployment '{deployment}' failed with HTTP {ex.Status}: {ex.Message}", failure, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ModelGatewayException)
        {
            throw new ModelGatewayException(
                $"Foundry deployment '{deployment}' failed: {ex.Message}", FailureClass.Retryable, ex);
        }
    }
}
