using DarkFactory.Client;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data.Tests;

/// <summary>
/// The real thing: the reference workspace server, launched as an actual
/// node process over Streamable HTTP, registered through the real MCP
/// client. Everything else in this suite runs against a fake probe, which
/// proves the registry's rules; this proves the wire.
///
/// It is also the only test that can catch a whole class of problem the
/// fake cannot — a tool name that does not match, a response shape the
/// client cannot read, a describe document that fails our own published
/// schema. Those are exactly the failures a server author would hit first.
/// </summary>
[Collection("SpecGraph")]
public sealed class ReferenceServerRegistrationTests(SpecGraphTestFixture fixture)
{
    private const string Org = "org_test";

    [Fact]
    public async Task TheReferenceServerRegistersAsConformant()
    {
        using var workspace = GitWorkspace.Create();
        await using var server = await ReferenceWorkspaceServer.StartAsync(workspace.Root);

        ServerRegistration registration;
        await using (var db = fixture.NewDb())
        {
            registration = await new ServerRegistry(db, new McpServerProbe())
                .RegisterAsync(Org, server.Url);
        }

        Assert.Equal(ServerStatus.Conformant, registration.Server.Status);
        Assert.Equal("workspace", registration.Server.Domain);
        Assert.Equal("0.2.0", registration.Server.ConventionVersion);
        Assert.Equal("dark-factory-workspace-mcp", registration.Server.Name);

        // Its describe answered our own published schema — if it had not,
        // RegisterAsync would have thrown rather than reaching here.
        Assert.Contains("df.describe", registration.Live.Capabilities);
        Assert.Contains("df.exec.run", registration.Live.Capabilities);
        Assert.All(registration.Live.Capabilities,
            c => Assert.StartsWith("df.", c, StringComparison.Ordinal));

        // First registration with no manifest: live becomes the manifest,
        // so there is nothing to disagree about.
        Assert.True(registration.Diff.IsEmpty, registration.Diff.Summarize());

        var byCapability = registration.Conformance.ToDictionary(c => c.Capability);
        Assert.Equal(ConformanceStatus.Passed, byCapability["df.files.write_many"].Status);
        Assert.Equal(ConformanceStatus.Passed, byCapability["df.files.list"].Status);
        Assert.Equal(ConformanceStatus.Passed, byCapability["df.exec.run"].Status);
        Assert.Equal(ConformanceStatus.NotProbed, byCapability["df.vcs.open_pr"].Status);
    }

    [Fact]
    public async Task TheConformanceProbeReallyTouchedTheWorkspace()
    {
        using var workspace = GitWorkspace.Create();
        await using var server = await ReferenceWorkspaceServer.StartAsync(workspace.Root);

        await using (var db = fixture.NewDb())
        {
            await new ServerRegistry(db, new McpServerProbe()).RegisterAsync(Org, server.Url);
        }

        // A green conformance row is only worth something if the call
        // behind it did what it claimed. The marker file is on disk, inside
        // the scratch directory the factory chose, in the workspace root
        // the server was pointed at — and nowhere else.
        var scratchRoot = Path.Combine(workspace.Root, ".df-conformance");
        Assert.True(Directory.Exists(scratchRoot), $"{scratchRoot} was never created");

        var markers = Directory.GetFiles(scratchRoot, "probe.txt", SearchOption.AllDirectories);
        var marker = Assert.Single(markers);
        Assert.Equal(ConformanceChecker.ProbeMarker, (await File.ReadAllTextAsync(marker)).Trim());
    }

    [Fact]
    public async Task SomethingThatIsNotAnMcpServerFailsRegistrationWithAClearError()
    {
        // The health endpoint of the reference server's own host: reachable,
        // speaks HTTP, is not MCP.
        using var workspace = GitWorkspace.Create();
        await using var server = await ReferenceWorkspaceServer.StartAsync(workspace.Root);
        var notMcp = server.Url.Replace("/mcp", "/not-mcp", StringComparison.Ordinal);

        await using var db = fixture.NewDb();
        var ex = await Assert.ThrowsAsync<ServerRegistrationException>(
            () => new ServerRegistry(db, new McpServerProbe()).RegisterAsync(Org, notMcp));

        Assert.Contains("df.describe", ex.Message, StringComparison.Ordinal);

        await using var verify = fixture.NewDb();
        Assert.Null(await verify.Servers.SingleOrDefaultAsync(s => s.Url == notMcp));
    }

}
