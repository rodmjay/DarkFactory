using System.Text.Json;
using DarkFactory.Core;

namespace DarkFactory.Data;

/// <summary>
/// Exercises a registered server's declared capabilities against a scratch
/// directory the factory names and owns.
///
/// Every call here is fixed: fixed tool, fixed arguments, fixed command,
/// fixed path, short deadline. The factory never runs a command the server
/// proposes and never uses a path the server suggests. A registration probe
/// is the last place to hand an unverified third party the ability to
/// choose what executes — the whole point of the check is that we do not
/// trust this server yet.
/// </summary>
public sealed class ConformanceChecker(IServerProbe probe)
{
    /// <summary>The one command ever executed by a conformance probe.</summary>
    public const string ProbeCommand = "echo df-conformance";

    /// <summary>Written into, and expected back from, the scratch directory.</summary>
    public const string ProbeMarker = "df-conformance";

    /// <summary>
    /// Short by design. A server that cannot answer a trivial call promptly
    /// is not one the factory should be waiting on during registration, and
    /// a slow probe would let an unregistered server hold a request open.
    /// </summary>
    public static readonly TimeSpan ProbeDeadline = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The one directory a workspace probe writes into, inside the
    /// customer's working tree. Fixed rather than per pass: conformance now
    /// re-runs every time a server heals (docs/adr/0038), and a directory
    /// per pass left a new folder in someone's repository after every
    /// restart — the workspace convention has no delete to clean up with.
    /// Each pass overwrites the same marker instead.
    /// </summary>
    public const string ScratchDirectory = ".df-conformance/probe";

    private const string WriteMany = "df.files.write_many";
    private const string List = "df.files.list";
    private const string ExecRun = "df.exec.run";
    private const string CorpusList = "df.corpus.list";
    private const string CorpusGet = "df.corpus.get";

    /// <summary>Capabilities this slice knows how to check. Everything else is recorded as NotProbed.</summary>
    public static IReadOnlySet<string> ProbedCapabilities { get; } =
        new HashSet<string>(StringComparer.Ordinal) { WriteMany, List, ExecRun, CorpusList, CorpusGet };

    public async Task<IReadOnlyList<ConformanceResult>> RunAsync(
        string serverId,
        string serverUrl,
        string conformanceRunId,
        IReadOnlyList<string> declaredCapabilities,
        CancellationToken cancellationToken = default)
    {
        // The factory chooses this path. Nothing the server said influences
        // it. The pass is identified by conformanceRunId on its results, not
        // by a directory in the customer's tree.
        var scratchDirectory = ScratchDirectory;
        var markerPath = $"{scratchDirectory}/probe.txt";

        var results = new List<ConformanceResult>();
        var declared = declaredCapabilities.ToHashSet(StringComparer.Ordinal);

        var wrote = false;
        if (declared.Contains(WriteMany))
        {
            var outcome = await ProbeWriteManyAsync(serverUrl, markerPath, cancellationToken);
            wrote = outcome.Status == ConformanceStatus.Passed;
            results.Add(Row(serverId, conformanceRunId, WriteMany, outcome));
        }

        if (declared.Contains(List))
        {
            var outcome = wrote
                ? await ProbeListAsync(serverUrl, scratchDirectory, markerPath, cancellationToken)
                : new Outcome(ConformanceStatus.Failed,
                    $"skipped: the scratch directory could not be created ({WriteMany} did not pass), so there was nothing to list", 0);
            results.Add(Row(serverId, conformanceRunId, List, outcome));
        }

        if (declared.Contains(ExecRun))
        {
            // cwd falls back to the server root when the scratch directory
            // does not exist; the command and its expected output are what
            // the probe actually asserts on, so it is still meaningful.
            var outcome = await ProbeExecAsync(serverUrl, wrote ? scratchDirectory : null, cancellationToken);
            results.Add(Row(serverId, conformanceRunId, ExecRun, outcome));
        }

        // A corpus server (docs/conventions/corpus.md): list it, then fetch
        // the first document and check its text hashes to what list said.
        // That hash is the one promise an import depends on.
        if (declared.Contains(CorpusList))
        {
            var (outcome, first) = await ProbeCorpusListAsync(serverUrl, cancellationToken);
            results.Add(Row(serverId, conformanceRunId, CorpusList, outcome));

            if (declared.Contains(CorpusGet))
            {
                var get = first is null
                    ? new Outcome(outcome.Status == ConformanceStatus.Passed ? ConformanceStatus.NotProbed : ConformanceStatus.Failed,
                        outcome.Status == ConformanceStatus.Passed
                            ? "the corpus is empty, so there was no document to fetch"
                            : $"skipped: {CorpusList} did not pass, so there was no document to fetch", 0)
                    : await ProbeCorpusGetAsync(serverUrl, first.Value.Id, first.Value.Sha256, cancellationToken);
                results.Add(Row(serverId, conformanceRunId, CorpusGet, get));
            }
        }
        else if (declared.Contains(CorpusGet))
        {
            results.Add(Row(serverId, conformanceRunId, CorpusGet,
                new Outcome(ConformanceStatus.Failed, $"declared without {CorpusList}, so there is no id to fetch", 0)));
        }

        // Everything the server declared that we have no probe for. Recorded
        // explicitly rather than omitted: "we did not check this" and "this
        // is fine" must not look the same in the registry.
        foreach (var capability in declaredCapabilities.Where(c => !ProbedCapabilities.Contains(c)))
        {
            results.Add(Row(serverId, conformanceRunId, capability,
                new Outcome(ConformanceStatus.NotProbed, "no probe for this capability in this convention version", 0)));
        }

        return results;
    }

    private sealed record Outcome(ConformanceStatus Status, string? Detail, long DurationMs);

    private async Task<Outcome> ProbeWriteManyAsync(string serverUrl, string markerPath, CancellationToken cancellationToken)
    {
        var result = await probe.CallAsync(serverUrl, WriteMany, new Dictionary<string, object?>
        {
            ["files"] = new[] { new Dictionary<string, object?> { ["path"] = markerPath, ["content"] = ProbeMarker } },
        }, ProbeDeadline, cancellationToken);

        if (!result.Ok)
        {
            return new Outcome(ConformanceStatus.Failed, result.Error, result.DurationMs);
        }

        return TryReadArray(result.Json!, "written", out var written, out var error) && written.Contains(markerPath)
            ? new Outcome(ConformanceStatus.Passed, null, result.DurationMs)
            : new Outcome(ConformanceStatus.Failed,
                error ?? $"did not report writing '{markerPath}' (got: {Truncate(result.Json!)})", result.DurationMs);
    }

    private async Task<Outcome> ProbeListAsync(
        string serverUrl, string scratchDirectory, string markerPath, CancellationToken cancellationToken)
    {
        var result = await probe.CallAsync(serverUrl, List, new Dictionary<string, object?>
        {
            ["globs"] = new[] { $"{scratchDirectory}/**" },
        }, ProbeDeadline, cancellationToken);

        if (!result.Ok)
        {
            return new Outcome(ConformanceStatus.Failed, result.Error, result.DurationMs);
        }

        if (!TryReadArray(result.Json!, "paths", out var paths, out var error))
        {
            return new Outcome(ConformanceStatus.Failed, error, result.DurationMs);
        }

        // A server may legitimately return the path absolute or normalized,
        // so match on the marker file rather than on string equality with
        // exactly what we sent.
        return paths.Any(p => p.Replace('\\', '/').EndsWith(markerPath, StringComparison.Ordinal))
            ? new Outcome(ConformanceStatus.Passed, null, result.DurationMs)
            : new Outcome(ConformanceStatus.Failed,
                $"listed the scratch directory without finding '{markerPath}' (got: {Truncate(result.Json!)})",
                result.DurationMs);
    }

    private async Task<Outcome> ProbeExecAsync(string serverUrl, string? cwd, CancellationToken cancellationToken)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["command"] = ProbeCommand,
            ["timeout"] = (int)ProbeDeadline.TotalMilliseconds,
        };
        if (cwd is not null)
        {
            arguments["cwd"] = cwd;
        }

        var result = await probe.CallAsync(serverUrl, ExecRun, arguments, ProbeDeadline, cancellationToken);
        if (!result.Ok)
        {
            return new Outcome(ConformanceStatus.Failed, result.Error, result.DurationMs);
        }

        using var document = JsonDocument.Parse(result.Json!);
        var root = document.RootElement;

        if (!root.TryGetProperty("exit_code", out var exitCode) || exitCode.ValueKind != JsonValueKind.Number)
        {
            return new Outcome(ConformanceStatus.Failed,
                $"response had no numeric exit_code (got: {Truncate(result.Json!)})", result.DurationMs);
        }

        if (exitCode.GetInt32() != 0)
        {
            return new Outcome(ConformanceStatus.Failed,
                $"'{ProbeCommand}' exited {exitCode.GetInt32()}", result.DurationMs);
        }

        var stdout = root.TryGetProperty("stdout_ref", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        return stdout is not null && stdout.Contains(ProbeMarker, StringComparison.Ordinal)
            ? new Outcome(ConformanceStatus.Passed, null, result.DurationMs)
            : new Outcome(ConformanceStatus.Failed,
                $"'{ProbeCommand}' exited 0 but stdout did not contain '{ProbeMarker}' (got: {Truncate(result.Json!)})",
                result.DurationMs);
    }

    private async Task<(Outcome Outcome, (string Id, string Sha256)? First)> ProbeCorpusListAsync(
        string serverUrl, CancellationToken cancellationToken)
    {
        var result = await probe.CallAsync(serverUrl, CorpusList, new Dictionary<string, object?>(), ProbeDeadline, cancellationToken);
        if (!result.Ok)
        {
            return (new Outcome(ConformanceStatus.Failed, result.Error, result.DurationMs), null);
        }

        try
        {
            using var document = JsonDocument.Parse(result.Json!);
            if (!document.RootElement.TryGetProperty("documents", out var array) || array.ValueKind != JsonValueKind.Array)
            {
                return (new Outcome(ConformanceStatus.Failed,
                    $"response had no 'documents' array (got: {Truncate(result.Json!)})", result.DurationMs), null);
            }

            (string, string)? first = null;
            foreach (var entry in array.EnumerateArray())
            {
                var id = entry.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
                var sha = entry.TryGetProperty("sha256", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                if (string.IsNullOrEmpty(id) || sha is null || sha.Length != 64)
                {
                    return (new Outcome(ConformanceStatus.Failed,
                        $"a listed document has no id or no 64-character sha256 (got: {Truncate(entry.GetRawText())})",
                        result.DurationMs), null);
                }
                first ??= (id, sha);
            }

            return (new Outcome(ConformanceStatus.Passed, null, result.DurationMs), first);
        }
        catch (JsonException ex)
        {
            return (new Outcome(ConformanceStatus.Failed, $"response was not valid JSON: {ex.Message}", result.DurationMs), null);
        }
    }

    private async Task<Outcome> ProbeCorpusGetAsync(
        string serverUrl, string id, string listedSha256, CancellationToken cancellationToken)
    {
        var result = await probe.CallAsync(serverUrl, CorpusGet, new Dictionary<string, object?> { ["id"] = id },
            ProbeDeadline, cancellationToken);
        if (!result.Ok)
        {
            return new Outcome(ConformanceStatus.Failed, result.Error, result.DurationMs);
        }

        using var document = JsonDocument.Parse(result.Json!);
        var text = document.RootElement.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()
            : null;
        if (text is null)
        {
            return new Outcome(ConformanceStatus.Failed,
                $"response for '{id}' had no text (got: {Truncate(result.Json!)})", result.DurationMs);
        }

        var computed = SpecGraphService.ComputeHash(text);
        return string.Equals(computed, listedSha256, StringComparison.Ordinal)
            ? new Outcome(ConformanceStatus.Passed, null, result.DurationMs)
            : new Outcome(ConformanceStatus.Failed,
                $"the text of '{id}' hashes to {computed}, but {CorpusList} listed {listedSha256}", result.DurationMs);
    }

    private static bool TryReadArray(string json, string property, out List<string> values, out string? error)
    {
        values = [];
        error = null;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            {
                error = $"response had no '{property}' array (got: {Truncate(json)})";
                return false;
            }

            values = array.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToList();
            return true;
        }
        catch (JsonException ex)
        {
            error = $"response was not valid JSON: {ex.Message}";
            return false;
        }
    }

    private static string Truncate(string value) => value.Length <= 200 ? value : value[..200] + "…";

    private static ConformanceResult Row(string serverId, string conformanceRunId, string capability, Outcome outcome) =>
        new()
        {
            Id = Ulid.NewUlid(),
            ServerId = serverId,
            ConformanceRunId = conformanceRunId,
            Capability = capability,
            Status = outcome.Status,
            Detail = outcome.Detail,
            DurationMs = outcome.DurationMs,
            CheckedAt = DateTimeOffset.UtcNow,
        };
}
