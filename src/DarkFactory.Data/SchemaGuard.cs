using Npgsql;

namespace DarkFactory.Data;

/// <summary>
/// Migrations run from an explicit `migrate` entrypoint (DarkFactory.Migrate),
/// never automatically from `factory` or `worker` startup. Both of those
/// call <see cref="EnsureSchemaUpToDateAsync"/> instead: if the database's
/// applied migrations (schema_migrations table) don't cover every .sql file
/// this build ships, the process refuses to start rather than running
/// against a schema it doesn't understand.
/// </summary>
public static class SchemaGuard
{
    public static async Task EnsureSchemaUpToDateAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var pending = await MigrationRunner.GetPendingVersionsAsync(connection, cancellationToken);
        if (pending.Count > 0)
        {
            throw new SchemaOutOfDateException(pending);
        }
    }
}

public sealed class SchemaOutOfDateException(IReadOnlyList<string> pendingMigrations)
    : Exception(
        $"Database schema is behind this build by {pendingMigrations.Count} migration(s): " +
        $"{string.Join(", ", pendingMigrations)}. Run the `migrate` service/entrypoint " +
        "(DarkFactory.Migrate) before starting factory or worker.")
{
    public IReadOnlyList<string> PendingMigrations { get; } = pendingMigrations;
}
