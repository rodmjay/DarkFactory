using System.Text.Json;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data;

/// <summary>A document as <c>df.corpus.list</c> describes it (docs/conventions/corpus.md).</summary>
public sealed record CorpusDocument(
    string Id, string Title, string? Area, string? Status, string? Updated, string Sha256, string? SupersededBy);

public sealed record CorpusDriftEntry(string OriginId, string SourceId, string ImportedSha256, string CurrentSha256);

/// <summary>What changed at the source since an intake was pulled from it.</summary>
public sealed record CorpusDrift(
    string IntakeId,
    string ServerName,
    IReadOnlyList<CorpusDriftEntry> Changed,
    IReadOnlyList<CorpusDocument> Added,
    IReadOnlyList<string> Removed);

/// <summary>
/// Pulls a corpus server's documents into an intake (docs/adr/0038,
/// docs/conventions/corpus.md) — extraction from MCP, rather than someone
/// pasting documents into a tool call.
///
/// What it guarantees: every document imported is the one the server
/// listed. <c>get</c>'s text is hashed and must match <c>list</c>'s hash,
/// because that hash is what drift is measured against afterwards, and an
/// import made from some other body could never be checked.
/// </summary>
public sealed class CorpusImporter(DarkFactoryDbContext db, IServerProbe probe, IntakeService intake)
{
    public const string Domain = "corpus";
    public const string ListTool = "df.corpus.list";
    public const string GetTool = "df.corpus.get";

    /// <summary>A corpus of a hundred documents lists in well under a second; thirty is generous.</summary>
    public static readonly TimeSpan CallDeadline = TimeSpan.FromSeconds(30);

    /// <summary>Their replacement, or their refusal, is what is still true (docs/conventions/corpus.md).</summary>
    public static IReadOnlySet<string> RetiredStatuses { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "superseded", "rejected" };

    public async Task<IntakeStarted> PullAsync(
        string projectId,
        string serverId,
        string? name,
        string? area,
        bool includeRetired,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        var server = await CorpusServerAsync(serverId, cancellationToken);
        var documents = await ListAsync(server, area, cancellationToken);

        var wanted = documents
            .Where(d => includeRetired || d.Status is null || !RetiredStatuses.Contains(d.Status))
            .ToList();
        if (wanted.Count == 0)
        {
            throw new InvalidOperationException(
                $"'{server.Name}' lists nothing to import{(area is null ? "" : $" in area '{area}'")}.");
        }

        var sources = new List<IntakeSourceInput>(wanted.Count);
        foreach (var document in wanted)
        {
            var (title, text) = await GetAsync(server, document, cancellationToken);
            sources.Add(new IntakeSourceInput($"{server.Name}:{document.Id}", title, text)
            {
                OriginId = document.Id,
                OriginSha256 = document.Sha256,
                OriginUpdated = document.Updated,
            });
        }

        return await intake.StartAsync(
            projectId, name ?? server.Name, sources, createdBy, cancellationToken, sourceServerId: server.Id);
    }

    /// <summary>
    /// Changed, added and removed at the source since the pull. A report,
    /// never an update: after intake the factory is authoritative (docs/adr/0037),
    /// so a changed original is a question for a person, not an instruction.
    /// </summary>
    public async Task<CorpusDrift> DriftAsync(string intakeId, CancellationToken cancellationToken = default)
    {
        var pulled = await db.Intakes.AsNoTracking().SingleOrDefaultAsync(i => i.Id == intakeId, cancellationToken)
            ?? throw new InvalidOperationException($"No intake '{intakeId}'.");
        if (pulled.SourceServerId is null)
        {
            throw new InvalidOperationException(
                $"Intake '{intakeId}' was submitted inline, not pulled from a corpus server, so there is no source to compare with.");
        }

        var server = await CorpusServerAsync(pulled.SourceServerId, cancellationToken);
        var current = (await ListAsync(server, area: null, cancellationToken)).ToDictionary(d => d.Id, StringComparer.Ordinal);

        var sources = await db.IntakeSources.AsNoTracking()
            .Where(s => s.IntakeId == intakeId && s.OriginId != null)
            .OrderBy(s => s.Seq)
            .ToListAsync(cancellationToken);
        var imported = sources.Select(s => s.OriginId!).ToHashSet(StringComparer.Ordinal);

        var changed = sources
            .Where(s => current.TryGetValue(s.OriginId!, out var now) && now.Sha256 != s.OriginSha256)
            .Select(s => new CorpusDriftEntry(s.OriginId!, s.Id, s.OriginSha256 ?? "", current[s.OriginId!].Sha256))
            .ToList();

        var removed = sources.Where(s => !current.ContainsKey(s.OriginId!)).Select(s => s.OriginId!).ToList();

        var added = current.Values
            .Where(d => !imported.Contains(d.Id) && (d.Status is null || !RetiredStatuses.Contains(d.Status)))
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .ToList();

        return new CorpusDrift(intakeId, server.Name, changed, added, removed);
    }

    private async Task<Server> CorpusServerAsync(string serverId, CancellationToken cancellationToken)
    {
        var server = await db.Servers.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == serverId && s.RemovedAt == null, cancellationToken)
            ?? throw new InvalidOperationException($"No server '{serverId}'.");

        if (!string.Equals(server.Domain, Domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{server.Name}' is a '{server.Domain}' server, not a corpus server; it has no documents to import.");
        }

        if (server.Status == ServerStatus.Unreachable)
        {
            throw new InvalidOperationException(
                $"'{server.Name}' has been unreachable since {server.UnreachableSince:u}" +
                $"{(server.LastError is null ? "" : $" ({server.LastError})")}. " +
                "The factory keeps checking it and re-verifies it the moment it answers; pull again then.");
        }

        return server;
    }

    private async Task<IReadOnlyList<CorpusDocument>> ListAsync(Server server, string? area, CancellationToken cancellationToken)
    {
        var arguments = new Dictionary<string, object?>();
        if (area is not null)
        {
            arguments["area"] = area;
        }

        var json = await CallAsync(server, ListTool, arguments, cancellationToken);

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("documents", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"'{server.Name}' answered {ListTool} without a 'documents' array.");
        }

        var documents = new List<CorpusDocument>();
        foreach (var element in array.EnumerateArray())
        {
            string? Str(string property) =>
                element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            var id = Str("id");
            var sha = Str("sha256");
            if (string.IsNullOrEmpty(id) || sha is null || !IsSha256(sha))
            {
                throw new InvalidOperationException(
                    $"'{server.Name}' listed a document without an id and a lowercase hex sha256 ({Truncate(element.GetRawText())}).");
            }

            documents.Add(new CorpusDocument(
                id, Str("title") ?? id, Str("area"), Str("status"), Str("updated"), sha, Str("superseded_by")));
        }

        var duplicate = documents.GroupBy(d => d.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"'{server.Name}' listed '{duplicate.Key}' more than once.");
        }

        return documents;
    }

    private async Task<(string Title, string Text)> GetAsync(
        Server server, CorpusDocument listed, CancellationToken cancellationToken)
    {
        var json = await CallAsync(server, GetTool, new Dictionary<string, object?> { ["id"] = listed.Id }, cancellationToken);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var text = root.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        if (string.IsNullOrEmpty(text))
        {
            throw new InvalidOperationException($"'{server.Name}' answered {GetTool}('{listed.Id}') without its text.");
        }

        var computed = SpecGraphService.ComputeHash(text);
        if (!string.Equals(computed, listed.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{listed.Id}' from '{server.Name}': the text {GetTool} served hashes to {computed}, but {ListTool} " +
                $"listed {listed.Sha256}. An import made from it could not be checked for drift later, so nothing was imported.");
        }

        var title = root.TryGetProperty("title", out var tt) && tt.ValueKind == JsonValueKind.String ? tt.GetString()! : listed.Title;
        return (title, text);
    }

    /// <summary>
    /// One call, with one retry on a failure the server may not repeat — a
    /// restart mid-pull should not throw away the forty documents already
    /// fetched. A second failure is reported, not retried further: the
    /// health monitor owns "is this server down", not the importer.
    /// </summary>
    private async Task<string> CallAsync(
        Server server, string tool, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var result = await probe.CallAsync(server.Url, tool, arguments, CallDeadline, cancellationToken);
        if (!result.Ok && result.Failure == FailureClass.Retryable)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            result = await probe.CallAsync(server.Url, tool, arguments, CallDeadline, cancellationToken);
        }

        if (!result.Ok)
        {
            throw new InvalidOperationException($"'{server.Name}' could not answer {tool}: {result.Error}");
        }

        return result.Json!;
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Truncate(string value) => value.Length <= 200 ? value : value[..200] + "…";
}
