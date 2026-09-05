using System.Text.Json;
using DarkFactory.Core;

namespace DarkFactory.Data.Tests;

/// <summary>
/// A scriptable stand-in for a remote MCP server. Records every call the
/// registry makes, which is how the "conformance never runs the server's
/// suggestions" test can assert on what the factory actually sent rather
/// than trusting that it sent the right thing.
/// </summary>
internal sealed class FakeServerProbe : IServerProbe
{
    public sealed record Call(string Url, string Tool, IReadOnlyDictionary<string, object?> Arguments, TimeSpan Deadline);

    public List<Call> Calls { get; } = [];

    /// <summary>Tool name → the response to give. Missing means "this server does not implement that tool".</summary>
    public Dictionary<string, Func<IReadOnlyDictionary<string, object?>, ProbeResult>> Handlers { get; } = new(StringComparer.Ordinal);

    public IReadOnlyList<Call> CallsTo(string tool) => Calls.Where(c => c.Tool == tool).ToList();

    public Task<ProbeResult> CallAsync(
        string serverUrl,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        TimeSpan deadline,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(new Call(serverUrl, toolName, arguments, deadline));

        return Task.FromResult(Handlers.TryGetValue(toolName, out var handler)
            ? handler(arguments)
            : ProbeResult.Failed($"no such tool '{toolName}'", FailureClass.Permanent, 1));
    }

    // ---- fluent setup -----------------------------------------------------

    public FakeServerProbe Answers(string tool, object payload)
    {
        Handlers[tool] = _ => ProbeResult.Success(JsonSerializer.Serialize(payload), 1);
        return this;
    }

    public FakeServerProbe AnswersRaw(string tool, string json)
    {
        Handlers[tool] = _ => ProbeResult.Success(json, 1);
        return this;
    }

    public FakeServerProbe Fails(string tool, string error, FailureClass failure = FailureClass.Retryable)
    {
        Handlers[tool] = _ => ProbeResult.Failed(error, failure, 1);
        return this;
    }

    /// <summary>
    /// A workspace server that behaves: it accepts the write, lists back
    /// whatever was written, and runs the command it was given.
    /// </summary>
    public static FakeServerProbe HealthyWorkspace(
        string name = "fake-workspace",
        IEnumerable<string>? capabilities = null,
        string conventionVersion = "0.2.0")
    {
        var probe = new FakeServerProbe();
        var written = new List<string>();

        probe.Answers("df.describe", Describe(name, capabilities, conventionVersion));

        probe.Handlers["df.files.write_many"] = args =>
        {
            var paths = ReadFilePaths(args);
            written.AddRange(paths);
            return ProbeResult.Success(JsonSerializer.Serialize(new { written = paths }), 1);
        };

        probe.Handlers["df.files.list"] = _ =>
            ProbeResult.Success(JsonSerializer.Serialize(new { paths = written }), 1);

        probe.Handlers["df.exec.run"] = args =>
        {
            var command = args.TryGetValue("command", out var c) ? c as string : null;
            // Echo semantics, so a probe asserting on stdout is asserting on
            // something the "server" genuinely derived from the command.
            var stdout = command?.StartsWith("echo ", StringComparison.Ordinal) == true ? command[5..] : "";
            return ProbeResult.Success(
                JsonSerializer.Serialize(new { exit_code = 0, stdout_ref = stdout, stderr_ref = "" }), 1);
        };

        return probe;
    }

    public static object Describe(
        string name = "fake-workspace",
        IEnumerable<string>? capabilities = null,
        string conventionVersion = "0.2.0",
        string domain = "workspace",
        IEnumerable<string>? requires = null) => new
        {
            name,
            convention_version = conventionVersion,
            domain,
            capabilities = capabilities?.ToArray() ?? DefaultCapabilities,
            requires = requires?.ToArray() ?? [],
            effective_config = new { root = "/tmp/fake" },
        };

    public static readonly string[] DefaultCapabilities =
    [
        "df.describe",
        "df.files.list",
        "df.files.read_many",
        "df.files.write_many",
        "df.exec.run",
        "df.vcs.open_pr",
    ];

    private static List<string> ReadFilePaths(IReadOnlyDictionary<string, object?> args)
    {
        if (args.TryGetValue("files", out var files) &&
            files is IEnumerable<Dictionary<string, object?>> entries)
        {
            return entries
                .Select(e => e.TryGetValue("path", out var p) ? p as string : null)
                .Where(p => p is not null)
                .Select(p => p!)
                .ToList();
        }
        return [];
    }
}
