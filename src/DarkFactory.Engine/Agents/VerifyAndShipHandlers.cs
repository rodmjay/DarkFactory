using System.Text;
using System.Text.Json;
using DarkFactory.Contracts;
using DarkFactory.Core;
using DarkFactory.Data;
using DarkFactory.Engine.StageHandlers;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Engine.Agents;

/// <summary>
/// Runs the team's declared test command and records what happened
/// (3d's fourth constraint).
///
/// No model call: the answer is an exit code, and asking a model to
/// interpret one would add a way to be wrong about something that is not in
/// doubt. Counts are parsed for the report a human reads; success is the
/// exit code and nothing else.
/// </summary>
public sealed class AgentVerifyStageHandler(
    DarkFactoryDbContext db, IServerProbe probe, IArtifactStore artifacts)
    : AgentStageHandlerBase(db, probe), IStageHandler
{
    public StageId Stage => StageId.Verify;

    public async Task<StageOutcome> ExecuteAsync(StageContext context, CancellationToken cancellationToken)
    {
        var team = await Db.Teams.AsNoTracking()
            .SingleOrDefaultAsync(t => t.ProjectId == context.Run.ProjectId && t.IsActive, cancellationToken);

        if (string.IsNullOrWhiteSpace(team?.TestCommand))
        {
            // A verify stage that passes because it found nothing to run is
            // worse than one that admits it is not configured.
            return new StageOutcome.Failed(FailureClass.NeedsHuman,
                "This project's team has not declared a test command, so verify has nothing to run. " +
                "Set the team's test command.");
        }

        var workspaceUrl = await WorkspaceUrlAsync(context.Run, cancellationToken);

        var result = await CallWorkspaceAsync(workspaceUrl, "df.exec.run", new Dictionary<string, object?>
        {
            ["command"] = team.TestCommand,
            ["timeout"] = (int)WorkspaceDeadline.TotalMilliseconds,
        }, cancellationToken);

        if (!result.Ok)
        {
            return new StageOutcome.Failed(result.Failure ?? FailureClass.Retryable,
                $"Could not run '{team.TestCommand}': {result.Error}");
        }

        using var response = JsonDocument.Parse(result.Json!);
        var root = response.RootElement;

        if (!root.TryGetProperty("exit_code", out var exitCodeElement)
            || exitCodeElement.ValueKind != JsonValueKind.Number)
        {
            return new StageOutcome.Failed(FailureClass.Permanent,
                "The workspace server returned no exit code for the test command.");
        }

        var exitCode = exitCodeElement.GetInt32();
        var stdout = Text(root, "stdout_ref");
        var stderr = Text(root, "stderr_ref");
        var parsed = TestOutput.Parse(stdout + "\n" + stderr);

        // Output is stored by reference (docs/adr/0004): a failing suite's
        // output is exactly what someone needs to read, and exactly what
        // must not be inlined into an artifact.
        var stdoutArtifact = await artifacts.PutAsync(
            context.Run.OrgId, context.Run.ProjectId, context.Run.Id, "TestOutput",
            stdout, ArtifactContentTypes.Markdown, cancellationToken);

        var report = new TestReport
        {
            ArtifactId = Ulid.NewUlid(),
            RunId = context.Run.Id,
            ChangesetArtifactRef = await LatestRefAsync(context.Run.Id, "ChangeSet", cancellationToken)
                ?? "(no changeset)",
            Command = team.TestCommand,
            ExitCode = exitCode,
            Passed = parsed.Passed,
            Failed = parsed.Failed,
            Skipped = parsed.Skipped,
            StdoutRef = ArtifactRef.Format(stdoutArtifact.Id),
            Success = exitCode == 0,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var reportJson = JsonSerializer.Serialize(report, ArtifactJsonOptions.Default);

        if (exitCode != 0)
        {
            // The report is still stored: a red suite is a result, and
            // losing it because the stage failed would throw away the one
            // thing a human needs to decide what to do next.
            await artifacts.PutAsync(
                context.Run.OrgId, context.Run.ProjectId, context.Run.Id, "TestReport",
                reportJson, ArtifactContentTypes.Json, cancellationToken);

            var counts = parsed.CountsFound
                ? $"{parsed.Failed} failed, {parsed.Passed} passed, {parsed.Skipped} skipped"
                : "counts not recognised in the output";

            return new StageOutcome.Failed(
                TestOutput.Classify(exitCode, parsed),
                $"'{team.TestCommand}' exited {exitCode} ({counts}). Output: {ArtifactRef.Format(stdoutArtifact.Id)}");
        }

        return new StageOutcome.Success(ArtifactType: "TestReport", ArtifactContentJson: reportJson);
    }

    private static string Text(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    private async Task<string?> LatestRefAsync(string runId, string type, CancellationToken cancellationToken)
    {
        var artifact = await Db.Artifacts.AsNoTracking()
            .Where(a => a.RunId == runId && a.Type == type)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return artifact is null ? null : ArtifactRef.Format(artifact.Id);
    }
}

/// <summary>
/// Branches, commits and opens the PR — and persists the PR body as an
/// artifact (3d's third constraint).
///
/// The body is stored whether or not a PR was actually opened. The demo
/// workspace has no remote, so `df.vcs.open_pr` returns a stub URL; if the
/// body only existed inside that call, the one thing worth asserting on
/// would be untestable exactly where it matters most.
/// </summary>
public sealed class AgentShipStageHandler(
    DarkFactoryDbContext db, IServerProbe probe, IArtifactStore artifacts)
    : AgentStageHandlerBase(db, probe), IStageHandler
{
    public StageId Stage => StageId.Ship;

    public const string PrBodyArtifactType = "PrBody";

    public async Task<StageOutcome> ExecuteAsync(StageContext context, CancellationToken cancellationToken)
    {
        var workspaceUrl = await WorkspaceUrlAsync(context.Run, cancellationToken);
        var branch = AgentImplementStageHandler.BranchName(context.Run);

        var changeSet = await LatestAsync<ChangeSet>(context.Run.Id, "ChangeSet", cancellationToken);
        var report = await LatestAsync<TestReport>(context.Run.Id, "TestReport", cancellationToken);

        var specIds = await SnapshotSpecIdsAsync(context.Run, cancellationToken);
        var body = await BuildPrBodyAsync(context.Run, changeSet, report, specIds, cancellationToken);

        // Persisted before the PR is opened, so it exists even if opening
        // fails — and so the acceptance check has something to read.
        var bodyArtifact = await artifacts.PutAsync(
            context.Run.OrgId, context.Run.ProjectId, context.Run.Id, PrBodyArtifactType,
            body, ArtifactContentTypes.Markdown, cancellationToken);

        var branched = await CallWorkspaceAsync(workspaceUrl, "df.vcs.branch",
            new Dictionary<string, object?> { ["name"] = branch }, cancellationToken);
        if (!branched.Ok)
        {
            return new StageOutcome.Failed(branched.Failure ?? FailureClass.Retryable,
                $"Could not create branch '{branch}': {branched.Error}");
        }

        var message = changeSet?.CommitMessage ?? $"Dark Factory run {context.Run.Id}";
        var committed = await CallWorkspaceAsync(workspaceUrl, "df.vcs.commit",
            new Dictionary<string, object?> { ["message"] = message }, cancellationToken);
        if (!committed.Ok)
        {
            return new StageOutcome.Failed(committed.Failure ?? FailureClass.Retryable,
                $"Could not commit: {committed.Error}");
        }

        var title = message.Split('\n')[0];
        var opened = await CallWorkspaceAsync(workspaceUrl, "df.vcs.open_pr",
            new Dictionary<string, object?> { ["title"] = title, ["body"] = body }, cancellationToken);
        if (!opened.Ok)
        {
            return new StageOutcome.Failed(opened.Failure ?? FailureClass.Retryable,
                $"Could not open a pull request: {opened.Error}");
        }

        using var response = JsonDocument.Parse(opened.Json!);
        var url = response.RootElement.TryGetProperty("url", out var u) ? u.GetString() : null;

        var shipped = new
        {
            run_id = context.Run.Id,
            branch,
            pr_url = url,
            pr_body_ref = ArtifactRef.Format(bodyArtifact.Id),
            snapshot_id = context.Run.SnapshotId,
            spec_ids = specIds.OrderBy(s => s, StringComparer.Ordinal).ToArray(),
            created_at = DateTimeOffset.UtcNow,
        };

        return new StageOutcome.Success(
            ArtifactType: "Shipped",
            ArtifactContentJson: JsonSerializer.Serialize(shipped));
    }

    /// <summary>
    /// The PR body. Its contract is 3d's acceptance condition: every spec
    /// id the run implemented, and the snapshot id it was built against.
    /// Those two facts are what make a merged PR traceable back to the
    /// decisions that caused it (docs/adr/0024).
    /// </summary>
    private async Task<string> BuildPrBodyAsync(
        Run run, ChangeSet? changeSet, TestReport? report,
        IReadOnlySet<string> specIds, CancellationToken cancellationToken)
    {
        var body = new StringBuilder();

        body.AppendLine("## What this implements");
        body.AppendLine();

        var texts = await SpecTextsAsync(run, specIds, cancellationToken);
        if (texts.Count == 0)
        {
            body.AppendLine("_No specifications were recorded for this run._");
        }
        else
        {
            foreach (var (specId, text) in texts.OrderBy(t => t.Key, StringComparer.Ordinal))
            {
                body.AppendLine($"- `{specId}` — {text}");
            }
        }
        body.AppendLine();

        body.AppendLine("## Provenance");
        body.AppendLine();
        body.AppendLine($"- Snapshot: `{run.SnapshotId ?? "(none)"}`");
        body.AppendLine($"- Run: `{run.Id}`");
        if (run.AmendmentIds is { Length: > 0 })
        {
            body.AppendLine($"- Amendments: {string.Join(", ", run.AmendmentIds.Select(a => $"`{a}`"))}");
        }
        body.AppendLine();

        if (changeSet is not null)
        {
            body.AppendLine("## Files changed");
            body.AppendLine();
            foreach (var file in changeSet.FilesChanged)
            {
                body.AppendLine($"- `{file}`");
            }
            body.AppendLine();
        }

        if (report is not null)
        {
            body.AppendLine("## Verification");
            body.AppendLine();
            body.AppendLine($"`{report.Command}` exited {report.ExitCode} — " +
                            $"{report.Passed} passed, {report.Failed} failed, {report.Skipped} skipped.");
            body.AppendLine();
        }

        body.AppendLine("---");
        body.AppendLine();
        body.AppendLine("Generated by Dark Factory. Every specification above is referenced from the code that");
        body.AppendLine("implements it, so `df.specs.reconcile` can tell later whether the two still agree.");

        return body.ToString();
    }

    private async Task<IReadOnlySet<string>> SnapshotSpecIdsAsync(Run run, CancellationToken cancellationToken) =>
        run.SnapshotId is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : (await Db.SnapshotMembers.AsNoTracking()
                .Where(m => m.SnapshotId == run.SnapshotId)
                .Select(m => m.SpecId)
                .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

    private async Task<Dictionary<string, string>> SpecTextsAsync(
        Run run, IReadOnlySet<string> specIds, CancellationToken cancellationToken)
    {
        if (run.SnapshotId is null || specIds.Count == 0)
        {
            return [];
        }

        var members = await Db.SnapshotMembers.AsNoTracking()
            .Where(m => m.SnapshotId == run.SnapshotId)
            .ToListAsync(cancellationToken);

        var hashes = members.Select(m => m.RevisionHash).ToList();
        var ids = members.Select(m => m.SpecId).ToList();

        var revisions = await Db.SpecRevisions.AsNoTracking()
            .Where(r => ids.Contains(r.SpecId) && hashes.Contains(r.Hash))
            .ToListAsync(cancellationToken);

        // The revision the snapshot pinned, not the latest — the PR
        // describes what was built, not what the graph says now.
        var byPair = revisions.ToDictionary(r => (r.SpecId, r.Hash), r => r.CanonicalText);

        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            if (byPair.TryGetValue((member.SpecId, member.RevisionHash), out var text))
            {
                texts[member.SpecId] = text;
            }
        }
        return texts;
    }

    private async Task<T?> LatestAsync<T>(string runId, string type, CancellationToken cancellationToken)
        where T : class
    {
        var artifact = await Db.Artifacts.AsNoTracking()
            .Where(a => a.RunId == runId && a.Type == type)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return artifact is null ? null : JsonSerializer.Deserialize<T>(artifact.ContentJson, AgentJson.Options);
    }
}
