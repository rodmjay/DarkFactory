using DarkFactory.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;

// The one place that runs migrations (see docs/adr/0008, SchemaGuard.cs,
// and MigrationRunner.cs). `factory` and `worker` never migrate on
// startup — they only verify the schema is already up to date and refuse
// to start otherwise. In Compose, this is the one-shot `migrate` service
// the others depend on (service_completed_successfully).

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString(ServiceCollectionExtensions.ConnectionStringName)
    ?? throw new InvalidOperationException($"Missing connection string '{ServiceCollectionExtensions.ConnectionStringName}'.");

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();

var pending = await MigrationRunner.GetPendingVersionsAsync(connection);
if (pending.Count == 0)
{
    Console.WriteLine("Schema already up to date. Nothing to do.");
    return 0;
}

Console.WriteLine($"Applying {pending.Count} pending migration(s): {string.Join(", ", pending)}");
var applied = await MigrationRunner.ApplyPendingAsync(connection);
Console.WriteLine($"Applied: {string.Join(", ", applied)}");
return 0;
