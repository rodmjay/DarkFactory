using HealthChecks.NpgSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DarkFactory.Data;

public static class ServiceCollectionExtensions
{
    public const string ConnectionStringName = "DarkFactory";

    public static IServiceCollection AddDarkFactoryData(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Missing connection string '{ConnectionStringName}'.");

        services.AddDbContext<DarkFactoryDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IArtifactStore, PostgresArtifactStore>();
        services.AddScoped<RunLeaseStore>();
        services.AddScoped<SpecGraphService>();
        services.AddScoped<SpecDiffTranslator>();
        services.AddScoped<TeamService>();
        services.AddScoped<ConversationService>();
        services.AddScoped<IntakeService>();
        services.AddScoped<ProjectService>();
        services.AddScoped<WorkService>();
        services.AddScoped<ServerRegistry>();
        services.AddScoped<BudgetService>();
        services.AddScoped<RunObservationService>();
        services.AddScoped<StageContextBuilder>();

        // Artifact URLs are only available when signing is configured. A
        // null ArtifactUrlSigner is a deliberate signal, not an oversight:
        // the endpoint that serves artifacts refuses everything without one,
        // rather than the factory booting with unsigned URLs.
        var artifactSection = configuration.GetSection(ArtifactUrlOptions.SectionName);
        var artifactOptions = new ArtifactUrlOptions
        {
            PublicBaseUrl = artifactSection[nameof(ArtifactUrlOptions.PublicBaseUrl)],
            SigningKey = artifactSection[nameof(ArtifactUrlOptions.SigningKey)],
        };
        if (TimeSpan.TryParse(artifactSection[nameof(ArtifactUrlOptions.Lifetime)], out var lifetime))
        {
            artifactOptions.Lifetime = lifetime;
        }
        if (!string.IsNullOrWhiteSpace(artifactOptions.SigningKey)
            && !string.IsNullOrWhiteSpace(artifactOptions.PublicBaseUrl))
        {
            services.AddSingleton(new ArtifactUrlSigner(artifactOptions));
        }

        services.AddHealthChecks().AddNpgSql(
            connectionString,
            name: "postgres",
            failureStatus: HealthStatus.Unhealthy,
            tags: ["ready"]);

        return services;
    }
}
