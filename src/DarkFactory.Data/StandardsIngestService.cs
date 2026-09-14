using System.Text.Json;
using System.Text.RegularExpressions;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data;

public sealed record StandardsIngest(string ServerId, string ServerName, int Ingested, int Removed, DateTimeOffset At);

/// <summary>
/// Copies a standards server's documents into the factory's own index
/// (docs/adr/0023, as amended by docs/adr/0036): <c>list</c> for what exists,
/// <c>get</c> for each text. The factory then reads standards from here —
/// one lookup, no live call per turn, and a server that is down does not
/// take its rules with it.
///
/// Ingest replaces rather than merges. A corpus of fifty documents
/// re-reads in well under a second, and a standard deleted at the source
/// must stop being served; a merge would keep it forever.
/// </summary>
public sealed partial class StandardsIngestService(DarkFactoryDbContext db, IServerProbe probe)
{
    public const string Domain = "standards";
    public const string ListTool = "df.standards.list";
    public const string GetTool = "df.standards.get";

    public static readonly TimeSpan CallDeadline = TimeSpan.FromSeconds(30);

    public async Task<StandardsIngest> IngestAsync(string serverId, CancellationToken cancellationToken = default)
    {
        var server = await db.Servers.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == serverId && s.RemovedAt == null, cancellationToken)
            ?? throw new InvalidOperationException($"No server '{serverId}'.");
        if (!string.Equals(server.Domain, Domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"'{server.Name}' is a '{server.Domain}' server, not a standards server.");
        }
        if (server.Status == ServerStatus.Unreachable)
        {
            throw new InvalidOperationException(
                $"'{server.Name}' has been unreachable since {server.UnreachableSince:u}; its last ingested standards are still served.");
        }

        using var listed = JsonDocument.Parse(await CallAsync(server, ListTool, new Dictionary<string, object?>(), cancellationToken));
        if (!listed.RootElement.TryGetProperty("standards", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"'{server.Name}' answered {ListTool} without a 'standards' array.");
        }

        var now = DateTimeOffset.UtcNow;
        var rows = new List<StandardsIndexEntry>();
        foreach (var entry in array.EnumerateArray())
        {
            var id = Str(entry, "id");
            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidOperationException($"'{server.Name}' listed a standard without an id.");
            }

            using var got = JsonDocument.Parse(
                await CallAsync(server, GetTool, new Dictionary<string, object?> { ["id"] = id }, cancellationToken));
            var text = Str(got.RootElement, "text");
            if (string.IsNullOrEmpty(text))
            {
                throw new InvalidOperationException($"'{server.Name}' answered {GetTool}('{id}') without its text.");
            }

            var layers = entry.TryGetProperty("layers", out var l) && l.ValueKind == JsonValueKind.Array
                ? l.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
                : [];

            rows.Add(new StandardsIndexEntry
            {
                Id = Ulid.NewUlid(),
                ServerId = server.Id,
                ProjectId = server.ProjectId,
                ChunkRef = id,
                Layer = layers.Count == 0 ? "all" : string.Join(",", layers),
                Title = Str(entry, "title") ?? Str(got.RootElement, "title") ?? id,
                Updated = Str(entry, "updated"),
                Text = text,
                SourceRef = $"{server.Name}:{id}",
                IngestedAt = now,
            });
        }

        // Everything fetched before anything is replaced: a pull that fails
        // halfway leaves the previous ingest serving, not half of this one.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var removed = await db.StandardsIndex.Where(s => s.ServerId == server.Id).ExecuteDeleteAsync(cancellationToken);
        db.StandardsIndex.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var kept = rows.Select(r => r.ChunkRef).ToHashSet(StringComparer.Ordinal);
        return new StandardsIngest(server.Id, server.Name, rows.Count, Math.Max(0, removed - kept.Count), now);
    }

    /// <summary>
    /// The standard ids a document names in its front matter, e.g.
    /// <c>standards: [web-game-structure, performance-budget]</c>. Order kept;
    /// empty when there is no such line.
    /// </summary>
    public static IReadOnlyList<string> NamedIn(string content)
    {
        var frontMatter = FrontMatter().Match(content);
        if (!frontMatter.Success)
        {
            return [];
        }

        var line = StandardsLine().Match(frontMatter.Groups[1].Value);
        return line.Success
            ? line.Groups[1].Value.Split(',').Select(s => s.Trim().Trim('"', '\'')).Where(s => s != "").Distinct().ToList()
            : [];
    }

    [GeneratedRegex(@"\A---\r?\n(.*?)\r?\n---", RegexOptions.Singleline)]
    private static partial Regex FrontMatter();

    [GeneratedRegex(@"^standards:\s*\[([^\]]*)\]", RegexOptions.Multiline)]
    private static partial Regex StandardsLine();

    private async Task<string> CallAsync(
        Server server, string tool, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var result = await probe.CallAsync(server.Url, tool, arguments, CallDeadline, cancellationToken);
        if (!result.Ok && result.Failure == FailureClass.Retryable)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            result = await probe.CallAsync(server.Url, tool, arguments, CallDeadline, cancellationToken);
        }

        return result.Ok
            ? result.Json!
            : throw new InvalidOperationException($"'{server.Name}' could not answer {tool}: {result.Error}");
    }

    private static string? Str(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
