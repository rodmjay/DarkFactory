using DarkFactory.Client;
using DarkFactory.Core;
using DarkFactory.Mcp.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DarkFactory.Data.Tests;

/// <summary>
/// The first time a spoke pulls from the hub (docs/adr/0001): a real
/// workspace server, over real MCP, fetching a real patch from a real HTTP
/// endpoint and applying it to a real git tree.
///
/// Everything here is deliberately end-to-end because every seam in it is
/// new — the signed URL, the HTTP endpoint, the fetch, `git apply`. A fake
/// at any one of them would leave the interesting failure untested.
/// </summary>
[Collection("SpecGraph")]
public sealed class PatchDeliveryTests(SpecGraphTestFixture fixture)
{
    private const string Patch =
        """
        diff --git a/hello.txt b/hello.txt
        index 3b18e51..5dd01c1 100644
        --- a/hello.txt
        +++ b/hello.txt
        @@ -1 +1 @@
        -hello world
        +hello factory

        """;

    [Fact]
    public async Task TheWorkspaceServerFetchesAPatchFromTheFactoryAndAppliesIt()
    {
        using var workspace = GitWorkspace.Create(("hello.txt", "hello world\n"));
        await using var server = await ReferenceWorkspaceServer.StartAsync(workspace.Root);

        var (projectId, orgId, _) = await fixture.SeedProjectAsync();
        var run = await SeedRunAsync(projectId, orgId);

        Artifact artifact;
        await using (var db = fixture.NewDb())
        {
            artifact = await new PostgresArtifactStore(db).PutAsync(
                orgId, projectId, run.Id, "ChangeSet", Patch, ArtifactContentTypes.Patch);
        }

        await using var factory = await ArtifactHost.StartAsync(fixture.OwnerConnectionString);
        var url = factory.Signer.Sign(artifact);

        var result = await new McpServerProbe().CallAsync(
            server.Url, "df.vcs.apply_patch",
            new Dictionary<string, object?> { ["patch_ref"] = url },
            TimeSpan.FromSeconds(30));

        Assert.True(result.Ok, result.Error);

        using var document = System.Text.Json.JsonDocument.Parse(result.Json!);
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean(),
            document.RootElement.TryGetProperty("message", out var m) ? m.GetString() : "apply_patch reported failure");

        var filesChanged = document.RootElement.GetProperty("files_changed")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        // Exactly the path, and nothing else: `git apply --summary` also
        // emits lines like "create mode 100644 x", which would be reported
        // as if they were files.
        Assert.Equal(["hello.txt"], filesChanged);

        // The patch actually landed on disk. A green result with an
        // unchanged file would be the worst possible outcome, because the
        // next stage would build on it.
        Assert.Equal("hello factory\n", await File.ReadAllTextAsync(Path.Combine(workspace.Root, "hello.txt")));
    }

    [Fact]
    public async Task ANewFilePatchReportsThePathAndNotGitsSummaryLines()
    {
        // The case that caught the parser: `git apply --summary` emits
        // "create mode 100644 DEMO.md" for a new file, which has no tabs
        // and would otherwise be reported as a second changed file.
        const string newFilePatch =
            """
            diff --git a/DEMO.md b/DEMO.md
            new file mode 100644
            index 0000000..a1b2c3d
            --- /dev/null
            +++ b/DEMO.md
            @@ -0,0 +1 @@
            +Written by the factory.

            """;

        using var workspace = GitWorkspace.Create(("hello.txt", "hello world\n"));
        await using var server = await ReferenceWorkspaceServer.StartAsync(workspace.Root);

        var (projectId, orgId, _) = await fixture.SeedProjectAsync();
        var run = await SeedRunAsync(projectId, orgId);

        Artifact artifact;
        await using (var db = fixture.NewDb())
        {
            artifact = await new PostgresArtifactStore(db).PutAsync(
                orgId, projectId, run.Id, "ChangeSet", newFilePatch, ArtifactContentTypes.Patch);
        }

        await using var factory = await ArtifactHost.StartAsync(fixture.OwnerConnectionString);

        var result = await new McpServerProbe().CallAsync(
            server.Url, "df.vcs.apply_patch",
            new Dictionary<string, object?> { ["patch_ref"] = factory.Signer.Sign(artifact) },
            TimeSpan.FromSeconds(30));

        using var document = System.Text.Json.JsonDocument.Parse(result.Json!);
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean(),
            document.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null);

        Assert.Equal(["DEMO.md"], document.RootElement.GetProperty("files_changed")
            .EnumerateArray().Select(e => e.GetString()).ToList());

        Assert.Equal("Written by the factory.\n",
            await File.ReadAllTextAsync(Path.Combine(workspace.Root, "DEMO.md")));
    }

    [Fact]
    public async Task AnArtifactFromAnotherProjectIsNotServed()
    {
        using var workspace = GitWorkspace.Create(("hello.txt", "hello world\n"));
        await using var server = await ReferenceWorkspaceServer.StartAsync(workspace.Root);

        var (projectA, orgId, _) = await fixture.SeedProjectAsync();
        var (projectB, _, _) = await fixture.SeedProjectAsync();
        var runA = await SeedRunAsync(projectA, orgId);

        Artifact artifact;
        await using (var db = fixture.NewDb())
        {
            artifact = await new PostgresArtifactStore(db).PutAsync(
                orgId, projectA, runA.Id, "ChangeSet", Patch, ArtifactContentTypes.Patch);
        }

        await using var factory = await ArtifactHost.StartAsync(fixture.OwnerConnectionString);

        // A URL signed for project B naming project A's artifact. The
        // signature is genuine — we mint it here with the real key — so the
        // only thing that can reject it is the scope check.
        var forged = factory.Signer.Sign(new Artifact
        {
            Id = artifact.Id,
            OrgId = orgId,
            ProjectId = projectB,
            RunId = runA.Id,
            Type = "ChangeSet",
            ContentJson = Patch,
            ContentType = ArtifactContentTypes.Patch,
            Sha256 = artifact.Sha256,
            CreatedAt = artifact.CreatedAt,
        });

        var result = await new McpServerProbe().CallAsync(
            server.Url, "df.vcs.apply_patch",
            new Dictionary<string, object?> { ["patch_ref"] = forged },
            TimeSpan.FromSeconds(30));

        using var document = System.Text.Json.JsonDocument.Parse(result.Json!);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("retryable", document.RootElement.GetProperty("failure_class").GetString());

        // Nothing was touched.
        Assert.Equal("hello world\n", await File.ReadAllTextAsync(Path.Combine(workspace.Root, "hello.txt")));
    }

    [Fact]
    public async Task APatchThatDoesNotApplyChangesNothingAndIsPermanent()
    {
        // The file the patch expects is not what is on disk.
        using var workspace = GitWorkspace.Create(("hello.txt", "something else entirely\n"));
        await using var server = await ReferenceWorkspaceServer.StartAsync(workspace.Root);

        var (projectId, orgId, _) = await fixture.SeedProjectAsync();
        var run = await SeedRunAsync(projectId, orgId);

        Artifact artifact;
        await using (var db = fixture.NewDb())
        {
            artifact = await new PostgresArtifactStore(db).PutAsync(
                orgId, projectId, run.Id, "ChangeSet", Patch, ArtifactContentTypes.Patch);
        }

        await using var factory = await ArtifactHost.StartAsync(fixture.OwnerConnectionString);

        var result = await new McpServerProbe().CallAsync(
            server.Url, "df.vcs.apply_patch",
            new Dictionary<string, object?> { ["patch_ref"] = factory.Signer.Sign(artifact) },
            TimeSpan.FromSeconds(30));

        using var document = System.Text.Json.JsonDocument.Parse(result.Json!);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());

        // Permanent, not retryable: the same patch against the same tree
        // will fail the same way forever (docs/adr/0007).
        Assert.Equal("permanent", document.RootElement.GetProperty("failure_class").GetString());

        // And crucially: a half-applied patch is worse than a rejected one,
        // because the next stage builds on top of it.
        Assert.Equal("something else entirely\n",
            await File.ReadAllTextAsync(Path.Combine(workspace.Root, "hello.txt")));
    }

    private async Task<Run> SeedRunAsync(string projectId, string orgId)
    {
        var suffix = Guid.NewGuid().ToString("n");
        var now = DateTimeOffset.UtcNow;

        await using var db = fixture.NewDb();
        var workItem = new WorkItem
        {
            Id = $"wi_{suffix}", OrgId = orgId, ProjectId = projectId, Input = "patch test", CreatedAt = now,
        };
        db.WorkItems.Add(workItem);

        var run = new Run
        {
            Id = $"run_{suffix}", OrgId = orgId, ProjectId = projectId, WorkItemId = workItem.Id,
            CurrentStage = StageId.Implement, Status = RunStatus.Running, CreatedAt = now,
        };
        db.Runs.Add(run);
        await db.SaveChangesAsync();
        return run;
    }

    // ---- harness ----------------------------------------------------------

    /// <summary>The factory's artifact endpoint, on a real socket a real workspace server can reach.</summary>
    private sealed class ArtifactHost(WebApplication app, ArtifactUrlSigner signer) : IAsyncDisposable
    {
        public ArtifactUrlSigner Signer { get; } = signer;

        public static async Task<ArtifactHost> StartAsync(string connectionString)
        {
            var options = new ArtifactUrlOptions
            {
                PublicBaseUrl = $"http://127.0.0.1:{ReferenceWorkspaceServer.FreePort()}",
                SigningKey = "patch-delivery-test-key",
            };

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls(options.PublicBaseUrl);
            builder.Logging.ClearProviders();

            builder.Services.AddDbContext<DarkFactoryDbContext>(o => o
                .UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention());
            builder.Services.AddScoped<IArtifactStore, PostgresArtifactStore>();
            builder.Services.AddSingleton(new ArtifactUrlSigner(options));

            var app = builder.Build();
            app.MapArtifactEndpoints();
            await app.StartAsync();

            return new ArtifactHost(app, new ArtifactUrlSigner(options));
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
