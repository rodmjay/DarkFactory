using System.Reflection;
using Npgsql;

namespace DarkFactory.Data;

/// <summary>
/// Applies plain, hand-written .sql files under Migrations/ in order,
/// tracking what's been applied in a `schema_migrations` table — no EF Core
/// Migrations codegen involved (see Migrations/0001_initial.sql's header).
/// Files are embedded resources so a published DarkFactory.Data.dll carries
/// them with no separate file-copy step in the Docker image.
///
/// Only DarkFactory.Migrate calls <see cref="ApplyPendingAsync"/>. `factory`
/// and `worker` only ever call <see cref="GetPendingVersionsAsync"/> (via
/// SchemaGuard) to verify there's nothing left to apply, and refuse to
/// start otherwise.
/// </summary>
public static class MigrationRunner
{
    private const string TableName = "schema_migrations";

    public static IReadOnlyList<string> GetEmbeddedMigrationVersions()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var prefix = $"{assembly.GetName().Name}.Migrations.";

        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal))
            .Select(name => name[prefix.Length..^".sql".Length])
            .OrderBy(version => version, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Read-only. If the tracking table doesn't exist yet, that means
    /// nothing has been migrated, which is an answer — not a reason to
    /// create it. `factory` and `worker` reach this path (via SchemaGuard)
    /// connected as the restricted application role, which has no CREATE
    /// privilege on the schema and should not: a process whose whole job is
    /// to refuse to run against an unfamiliar schema must not be capable of
    /// modifying one.
    /// </summary>
    public static async Task<IReadOnlySet<string>> GetAppliedVersionsAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken = default)
    {
        await using (var exists = new NpgsqlCommand("SELECT to_regclass(@table) IS NOT NULL", connection))
        {
            exists.Parameters.AddWithValue("table", TableName);
            if (await exists.ExecuteScalarAsync(cancellationToken) is not true)
            {
                return new HashSet<string>();
            }
        }

        await using var command = new NpgsqlCommand($"SELECT version FROM {TableName}", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var applied = new HashSet<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            applied.Add(reader.GetString(0));
        }
        return applied;
    }

    public static async Task<IReadOnlyList<string>> GetPendingVersionsAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken = default)
    {
        var applied = await GetAppliedVersionsAsync(connection, cancellationToken);
        return GetEmbeddedMigrationVersions().Where(version => !applied.Contains(version)).ToList();
    }

    /// <summary>Applies every pending migration, one per transaction, in order. Returns the versions applied.</summary>
    public static async Task<IReadOnlyList<string>> ApplyPendingAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken = default)
    {
        // The only caller is DarkFactory.Migrate, connected as the owner —
        // so this is the only place the tracking table gets created.
        await EnsureMigrationsTableAsync(connection, cancellationToken);

        var pending = await GetPendingVersionsAsync(connection, cancellationToken);

        foreach (var version in pending)
        {
            var sql = ReadEmbeddedMigration(version);

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using (var apply = new NpgsqlCommand(sql, connection, transaction))
            {
                await apply.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var record = new NpgsqlCommand(
                $"INSERT INTO {TableName} (version, applied_at) VALUES (@version, now())", connection, transaction))
            {
                record.Parameters.AddWithValue("version", version);
                await record.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        return pending;
    }

    private static async Task EnsureMigrationsTableAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"CREATE TABLE IF NOT EXISTS {TableName} (version text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now())",
            connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ReadEmbeddedMigration(string version)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"{assembly.GetName().Name}.Migrations.{version}.sql";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
