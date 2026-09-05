using Npgsql;

namespace DarkFactory.Data;

/// <summary>
/// The restricted, non-owning database role that `factory` and `worker`
/// connect as. Migrations/0002_spec_graph.sql creates it NOLOGIN and sets
/// its grants (including the REVOKEs that make the append-only tables
/// genuinely append-only); this class does the one thing a versioned .sql
/// file must not, which is put a password in the database.
///
/// The separation matters. A Postgres table owner bypasses its own
/// GRANT/REVOKE, so "the application cannot UPDATE spec_revisions" is only
/// true while the application connects as something other than the role
/// that created the table. `migrate` keeps the owner credentials; nothing
/// else gets them.
/// </summary>
public static class AppRole
{
    public const string Name = "darkfactory_app";

    /// <summary>Configuration key (or <c>DARKFACTORY_APP_PASSWORD</c> env var) holding the role's password.</summary>
    public const string PasswordConfigurationKey = "DARKFACTORY_APP_PASSWORD";

    /// <summary>
    /// Grants LOGIN and sets the password. Idempotent, and safe to run on
    /// every migrate: re-running it simply re-asserts the password, which
    /// is also how the password gets rotated.
    /// </summary>
    public static async Task EnsureLoginAsync(
        NpgsqlConnection connection, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Password must not be empty.", nameof(password));
        }

        // ALTER ROLE takes no parameters, and string-concatenating a
        // password into DDL is exactly the mistake this whole class exists
        // to avoid. Let Postgres do the quoting: format(%I, %L) produces a
        // correctly escaped statement, which we then execute.
        await using var build = new NpgsqlCommand(
            "SELECT format('ALTER ROLE %I WITH LOGIN PASSWORD %L', @role, @password)", connection);
        build.Parameters.AddWithValue("role", Name);
        build.Parameters.AddWithValue("password", password);

        var statement = (string)(await build.ExecuteScalarAsync(cancellationToken))!;

        await using var alter = new NpgsqlCommand(statement, connection);
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }
}
