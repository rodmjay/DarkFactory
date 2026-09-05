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

        services.AddDbContext<DarkFactoryDbContext>(options => options.UseNpgsql(connectionString));

        services.AddHealthChecks().AddNpgSql(
            connectionString,
            name: "postgres",
            failureStatus: HealthStatus.Unhealthy,
            tags: ["ready"]);

        return services;
    }
}
