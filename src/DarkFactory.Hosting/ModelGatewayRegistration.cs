using DarkFactory.Anthropic;
using DarkFactory.Core;
using DarkFactory.Data;
using DarkFactory.Foundry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DarkFactory.Hosting;

/// <summary>
/// The single place in the solution that chooses a model provider
/// (docs/adr/0027). Composition roots call this; nothing else references
/// <c>DarkFactory.Foundry</c> or <c>DarkFactory.Anthropic</c> at all.
///
/// Two implementations exist for one reason: Foundry is the production
/// target and the bring-your-own story, and Anthropic-direct is what makes
/// the factory buildable and demonstrable before a Foundry resource is
/// provisioned. Having both is also the only honest test of whether the
/// gateway abstraction is real — an interface with a single implementation
/// is a claim, not a seam.
/// </summary>
public static class ModelGatewayRegistration
{
    public const string ProviderKey = "ModelGateway:Provider";

    public static IServiceCollection AddModelGateway(
        this IServiceCollection services, IConfiguration configuration, ILogger? logger = null)
    {
        var requested = configuration[ProviderKey];

        var provider = requested?.Trim().ToLowerInvariant() switch
        {
            "anthropic" => ModelProvider.Anthropic,
            "foundry" => ModelProvider.Foundry,
            null or "" => Detect(configuration),
            _ => throw new InvalidOperationException(
                $"Unknown {ProviderKey} '{requested}'. Expected 'Anthropic' or 'Foundry'."),
        };

        switch (provider)
        {
            case ModelProvider.Anthropic:
                services.Configure<AnthropicOptions>(configuration.GetSection(AnthropicOptions.SectionName));
                services.AddHttpClient<AnthropicModelGateway>();
                AddRecordingGateway<AnthropicModelGateway>(services);
                logger?.LogInformation("Model gateway: Anthropic (development provider, docs/adr/0027).");
                break;

            case ModelProvider.Foundry:
                services.Configure<FoundryOptions>(configuration.GetSection(FoundryOptions.SectionName));
                services.AddSingleton<FoundryModelGateway>();
                AddRecordingGateway<FoundryModelGateway>(services);
                logger?.LogInformation("Model gateway: Microsoft Foundry (docs/adr/0027).");
                break;

            default:
                // Boot anyway. Everything that does not need a model keeps
                // working, and anything that does fails with a sentence
                // saying what is missing rather than with a null reference
                // at the first conversational turn.
                services.AddSingleton<IModelGateway>(new UnconfiguredModelGateway());
                logger?.LogWarning(
                    "No model gateway configured. Set {ProviderKey}, or configure " +
                    "{FoundrySection}:Endpoint or {AnthropicSection}:ApiKey.",
                    ProviderKey, FoundryOptions.SectionName, AnthropicOptions.SectionName);
                break;
        }

        return services;
    }

    /// <summary>
    /// Registers the provider behind <see cref="RecordingModelGateway"/>, so
    /// what gets injected as <see cref="IModelGateway"/> is always the
    /// recorder and never the provider itself.
    ///
    /// docs/adr/0032 says usage fact rows are written by the gateway and
    /// never by an agent. Wiring it here is what makes that structural
    /// rather than a rule someone has to remember: there is no registration
    /// that hands a caller a bare provider, so there is no way to make a
    /// model call that escapes the ledger.
    ///
    /// Scoped, because the recorder writes through the request's DbContext.
    /// </summary>
    private static void AddRecordingGateway<TProvider>(IServiceCollection services)
        where TProvider : class, IModelGateway
    {
        services.AddScoped<IModelGateway>(sp => new RecordingModelGateway(
            sp.GetRequiredService<TProvider>(),
            sp.GetRequiredService<DarkFactoryDbContext>(),
            sp.GetRequiredService<ILogger<RecordingModelGateway>>()));

        // The same instance answers IModelCallLog, so a caller can complete
        // the outcome columns on a row it already caused to be written.
        services.AddScoped<IModelCallLog>(sp => (RecordingModelGateway)sp.GetRequiredService<IModelGateway>());
    }

    /// <summary>
    /// With no explicit provider, infer from what is actually configured.
    /// Deliberately refuses to guess when both are present: two configured
    /// providers is a question about intent and cost, and picking one
    /// silently is how a run gets billed to the wrong account.
    /// </summary>
    private static ModelProvider Detect(IConfiguration configuration)
    {
        var foundry = !string.IsNullOrWhiteSpace(
            configuration[$"{FoundryOptions.SectionName}:Endpoint"]);
        var anthropic = !string.IsNullOrWhiteSpace(
            configuration[$"{AnthropicOptions.SectionName}:ApiKey"]);

        return (foundry, anthropic) switch
        {
            (true, true) => throw new InvalidOperationException(
                $"Both {FoundryOptions.SectionName}:Endpoint and {AnthropicOptions.SectionName}:ApiKey are set. " +
                $"Set {ProviderKey} to 'Foundry' or 'Anthropic' to say which one to use."),
            (true, false) => ModelProvider.Foundry,
            (false, true) => ModelProvider.Anthropic,
            _ => ModelProvider.None,
        };
    }

    private enum ModelProvider { None, Foundry, Anthropic }
}
