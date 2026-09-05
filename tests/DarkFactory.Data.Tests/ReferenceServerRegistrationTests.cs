using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
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
        using var workspace = new TemporaryWorkspace();
        await using var server = await ReferenceServer.StartAsync(workspace.Root);

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
        using var workspace = new TemporaryWorkspace();
        await using var server = await ReferenceServer.StartAsync(workspace.Root);

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
        using var workspace = new TemporaryWorkspace();
        await using var server = await ReferenceServer.StartAsync(workspace.Root);
        var notMcp = server.Url.Replace("/mcp", "/not-mcp", StringComparison.Ordinal);

        await using var db = fixture.NewDb();
        var ex = await Assert.ThrowsAsync<ServerRegistrationException>(
            () => new ServerRegistry(db, new McpServerProbe()).RegisterAsync(Org, notMcp));

        Assert.Contains("df.describe", ex.Message, StringComparison.Ordinal);

        await using var verify = fixture.NewDb();
        Assert.Null(await verify.Servers.SingleOrDefaultAsync(s => s.Url == notMcp));
    }

    // ---- harness ----------------------------------------------------------

    private sealed class TemporaryWorkspace : IDisposable
    {
        public string Root { get; } =
            Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "df-workspace-" + Guid.NewGuid().ToString("n"))).FullName;

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class ReferenceServer : IAsyncDisposable
    {
        private readonly Process _process;

        private ReferenceServer(Process process, string url)
        {
            _process = process;
            Url = url;
        }

        public string Url { get; }

        public static async Task<ReferenceServer> StartAsync(string workspaceRoot, [CallerFilePath] string here = "")
        {
            var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
            var entrypoint = Path.Combine(repoRoot, "reference", "workspace-mcp", "dist", "index.js");

            if (!File.Exists(entrypoint))
            {
                throw new InvalidOperationException(
                    $"{entrypoint} not found. Run `npm install && npm run build` in reference/workspace-mcp first.");
            }

            var port = FreePort();
            var startInfo = new ProcessStartInfo("node", $"\"{entrypoint}\" --http {port} --root \"{workspaceRoot}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("could not start node");

            var url = $"http://127.0.0.1:{port}/mcp";
            await WaitForListeningAsync(port, process);
            return new ReferenceServer(process, url);
        }

        /// <summary>
        /// Polls the real condition — the port accepting a connection —
        /// rather than sleeping a hopeful interval, so a server that comes
        /// up fast costs nothing and one that never comes up fails fast
        /// with a clear message.
        /// </summary>
        private static async Task WaitForListeningAsync(int port, Process process)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException(
                        $"the reference server exited with {process.ExitCode} before listening:\n" +
                        await process.StandardError.ReadToEndAsync());
                }

                try
                {
                    using var probe = new TcpClient();
                    await probe.ConnectAsync("127.0.0.1", port);
                    return;
                }
                catch (SocketException)
                {
                    await Task.Delay(50);
                }
            }

            throw new TimeoutException($"the reference server never listened on port {port}");
        }

        private static int FreePort()
        {
            using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch { /* best effort */ }
            _process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
