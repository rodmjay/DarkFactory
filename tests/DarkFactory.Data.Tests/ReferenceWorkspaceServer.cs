using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace DarkFactory.Data.Tests;

/// <summary>
/// The reference workspace server, launched as a real node process over
/// Streamable HTTP. Shared by every test that needs a genuine spoke rather
/// than a stand-in — registration, conformance, and patch delivery.
/// </summary>
internal sealed class ReferenceWorkspaceServer : IAsyncDisposable
{
    private readonly Process _process;

    private ReferenceWorkspaceServer(Process process, string url)
    {
        _process = process;
        Url = url;
    }

    public string Url { get; }

    public static async Task<ReferenceWorkspaceServer> StartAsync(
        string workspaceRoot, [CallerFilePath] string here = "")
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

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("could not start node");

        await WaitForListeningAsync(port, process);
        return new ReferenceWorkspaceServer(process, $"http://127.0.0.1:{port}/mcp");
    }

    /// <summary>
    /// Polls the real condition — the port accepting a connection — rather
    /// than sleeping a hopeful interval, so a server that comes up fast
    /// costs nothing and one that never comes up fails with a clear message.
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

    public static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
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

/// <summary>A real git repository with real content, so `git apply` has something to apply to.</summary>
internal sealed class GitWorkspace : IDisposable
{
    public required string Root { get; init; }

    public static GitWorkspace Create(params (string Path, string Content)[] files)
    {
        var root = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "df-workspace-" + Guid.NewGuid().ToString("n"))).FullName;

        foreach (var (path, content) in files)
        {
            var full = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        Git(root, "init", "-q", "-b", "main");
        Git(root, "config", "user.email", "test@darkfactory.invalid");
        Git(root, "config", "user.name", "Test");
        Git(root, "add", "-A");
        // --allow-empty: a workspace with no files yet is a legitimate
        // starting state, and the registration tests use exactly that.
        Git(root, "commit", "-q", "--allow-empty", "-m", "seed");

        return new GitWorkspace { Root = root };
    }

    private static void Git(string root, params string[] args)
    {
        var startInfo = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardError = true };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)!;
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
    }
}
