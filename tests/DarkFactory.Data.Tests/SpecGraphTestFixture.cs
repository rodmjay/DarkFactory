using DarkFactory.Core;
using DarkFactory.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DarkFactory.Data.Tests;

/// <summary>
/// One Postgres container, migrated with the real MigrationRunner, exposing
/// two connection strings: the owner's (what `migrate` uses) and the
/// restricted application role's (what `factory` and `worker` use).
///
/// Having both is the point. The append-only guarantee in
/// Migrations/0002_spec_graph.sql is a REVOKE, and a Postgres table owner
/// bypasses its own REVOKEs — so a test that tried to prove immutability
/// over the owner connection would pass no matter what the migration said.
/// </summary>
public sealed class SpecGraphTestFixture : IAsyncLifetime
{
    private const string AppPassword = "app_role_test_password";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16")
        .WithDatabase("darkfactory_test")
        .WithUsername("darkfactory")
        .WithPassword("darkfactory")
        .Build();

    /// <summary>Owns the tables. Used for seeding and for the control half of the privilege tests.</summary>
    public string OwnerConnectionString => _container.GetConnectionString();

    /// <summary>The restricted role the running application actually connects as.</summary>
    public string AppConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var connection = new NpgsqlConnection(OwnerConnectionString);
        await connection.OpenAsync();
        await MigrationRunner.ApplyPendingAsync(connection);
        await AppRole.EnsureLoginAsync(connection, AppPassword);

        AppConnectionString = new NpgsqlConnectionStringBuilder(OwnerConnectionString)
        {
            Username = AppRole.Name,
            Password = AppPassword,
        }.ConnectionString;
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public DarkFactoryDbContext NewDb() => NewDb(OwnerConnectionString);

    public DarkFactoryDbContext NewAppDb() => NewDb(AppConnectionString);

    private static DarkFactoryDbContext NewDb(string connectionString) =>
        new(new DbContextOptionsBuilder<DarkFactoryDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options);

    /// <summary>A project + conversation to hang spec nodes off, unique per call so tests never collide.</summary>
    public async Task<(string ProjectId, string OrgId, string ConversationId)> SeedProjectAsync()
    {
        var suffix = Guid.NewGuid().ToString("n");
        var now = DateTimeOffset.UtcNow;

        await using var db = NewDb();

        var project = new Project
        {
            Id = $"proj_{suffix}",
            OrgId = "org_test",
            Name = $"spec-test-{suffix}",
            WorkspaceMcpUrl = $"http://example.invalid/{suffix}",
            CreatedAt = now,
        };
        db.Projects.Add(project);

        var conversation = new Conversation
        {
            Id = Ulid.NewUlid(),
            ProjectId = project.Id,
            OrgId = project.OrgId,
            Title = "seed",
            CreatedBy = "tester",
            CreatedAt = now,
            Status = ConversationStatus.Active,
        };
        db.Conversations.Add(conversation);

        await db.SaveChangesAsync();
        return (project.Id, project.OrgId, conversation.Id);
    }
}

[CollectionDefinition("SpecGraph")]
public sealed class SpecGraphCollection : ICollectionFixture<SpecGraphTestFixture>;
