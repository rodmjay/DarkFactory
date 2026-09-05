using System.Diagnostics;
using System.Runtime.CompilerServices;
using DarkFactory.Data;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DarkFactory.Engine.Tests;

/// <summary>
/// One Postgres container, migrated once via the real MigrationRunner (so
/// these tests also double as coverage that the plain .sql migrations
/// actually apply cleanly), plus one Release publish of DarkFactory.Engine
/// so the crash/resume test can launch it as a real OS process. Shared
/// across the whole "Engine" collection so the container and the publish
/// only happen once per test run.
/// </summary>
public sealed class EngineTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16")
        .WithDatabase("darkfactory_test")
        .WithUsername("darkfactory")
        .WithPassword("darkfactory")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public string EnginePublishDir { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await MigrationRunner.ApplyPendingAsync(connection);

        EnginePublishDir = await PublishEngineAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
        try { Directory.Delete(EnginePublishDir, recursive: true); } catch { /* best effort */ }
    }

    private static async Task<string> PublishEngineAsync([CallerFilePath] string here = "")
    {
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
        var csproj = Path.Combine(repoRoot, "src", "DarkFactory.Engine", "DarkFactory.Engine.csproj");
        var outDir = Path.Combine(Path.GetTempPath(), "darkfactory-engine-test-publish-" + Guid.NewGuid().ToString("n"));

        // /nodeReuse:false + UseSharedCompilation=false + MSBUILDDISABLENODEREUSE
        // avoid a ~15-minute hang: a dotnet build/publish spawned as a child
        // of a `dotnet test` process otherwise tries to hand its build off
        // to (or reuse) an MSBuild worker node the parent's own build
        // already has pinned, and the handshake stalls until some deep
        // internal timeout.
        var publishStartInfo = new ProcessStartInfo("dotnet",
            $"publish \"{csproj}\" -c Release -o \"{outDir}\" --nologo -v quiet /nodeReuse:false -p:UseSharedCompilation=false")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        publishStartInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        var publish = Process.Start(publishStartInfo)!;

        var stdout = await publish.StandardOutput.ReadToEndAsync();
        var stderr = await publish.StandardError.ReadToEndAsync();
        await publish.WaitForExitAsync();

        if (publish.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet publish failed (exit {publish.ExitCode}):\n{stdout}\n{stderr}");
        }

        return outDir;
    }
}

[CollectionDefinition("Engine")]
public sealed class EngineCollection : ICollectionFixture<EngineTestFixture>;
