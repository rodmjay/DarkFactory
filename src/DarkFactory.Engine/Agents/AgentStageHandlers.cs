using System.Text.Json;
using DarkFactory.Contracts;
using DarkFactory.Core;
using DarkFactory.Data;
using DarkFactory.Engine.StageHandlers;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Engine.Agents;

/// <summary>
/// Shared helpers for the three real stage agents: what the run is
/// implementing, and how to talk to its workspace server.
/// </summary>
public abstract class AgentStageHandlerBase(DarkFactoryDbContext db, IServerProbe probe)
{
    /// <summary>Long enough for a test suite; a build is not a chat turn.</summary>
    protected static readonly TimeSpan WorkspaceDeadline = TimeSpan.FromMinutes(15);

    protected async Task<string> WorkspaceUrlAsync(Run run, CancellationToken cancellationToken) =>
        await db.Projects.AsNoTracking()
            .Where(p => p.Id == run.ProjectId)
            .Select(p => p.WorkspaceMcpUrl)
            .SingleAsync(cancellationToken);

    /// <summary>
    /// The spec ids this run is actually implementing — the ones its
    /// approved amendments touched, not everything in the snapshot. A
    /// project's snapshot carries its whole graph; a run implements a
    /// slice, and holding the implementer to the whole graph would be
    /// absurd.
    /// </summary>
    protected async Task<IReadOnlySet<string>> ImplementedSpecIdsAsync(
        Run run, IReadOnlySet<string> snapshotSpecIds, CancellationToken cancellationToken)
    {
        if (run.AmendmentIds is not { Length: > 0 })
        {
            return snapshotSpecIds;
        }

        var amendments = await db.Amendments.AsNoTracking()
            .Where(a => run.AmendmentIds.Contains(a.Id))
            .Select(a => a.DiffJson)
            .ToListAsync(cancellationToken);

        var touched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var json in amendments)
        {
            var diff = JsonSerializer.Deserialize<SpecDiff>(json);
            if (diff is null)
            {
                continue;
            }

            foreach (var revise in diff.Revises) touched.Add(revise.SpecId);
            // Created nodes were assigned ids at approval time and are not
            // in the diff, so they are recovered from the snapshot below.
        }

        // Everything the amendment created is in the snapshot and nowhere
        // else identifiable, so the honest reading of "what this run
        // implements" is: the snapshot, minus anything that predates the
        // amendments. Provenance gives us that.
        var createdByThisRun = await db.SpecRevisions.AsNoTracking()
            .Where(r => snapshotSpecIds.Contains(r.SpecId))
            .Join(db.Provenance.AsNoTracking(), r => r.ProvenanceId, p => p.Id, (r, p) => new { r.SpecId, p.At })
            .ToListAsync(cancellationToken);

        var amendmentTimes = await db.Amendments.AsNoTracking()
            .Where(a => run.AmendmentIds.Contains(a.Id))
            .Select(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        if (amendmentTimes.Count > 0)
        {
            var earliest = amendmentTimes.Min();
            foreach (var row in createdByThisRun.Where(r => r.At >= earliest))
            {
                touched.Add(row.SpecId);
            }
        }

        return touched.Count > 0 ? touched : snapshotSpecIds;
    }

    protected Task<ProbeResult> CallWorkspaceAsync(
        string url, string tool, Dictionary<string, object?> arguments, CancellationToken cancellationToken) =>
        probe.CallAsync(url, tool, arguments, WorkspaceDeadline, cancellationToken);
}

/// <summary>Plans the work from the run's specifications (docs/adr/0003, as amended).</summary>
public sealed class AgentPlanStageHandler(
    DarkFactoryDbContext db, IServerProbe probe, StageAgent agent)
    : AgentStageHandlerBase(db, probe), IStageHandler
{
    public StageId Stage => StageId.Plan;

    public async Task<StageOutcome> ExecuteAsync(StageContext context, CancellationToken cancellationToken)
    {
        try
        {
            var turn = await agent.RunAsync<PlanDocument>(
                context, Stage,
                AgentPrompts.PlanPrefix,
                AgentPrompts.Variable,
                AgentPrompts.PlanUserMessage,
                text => Task.FromResult(ParsePlan(text)),
                AgentPrompts.PlanTemplateVersion,
                cancellationToken);

            var plan = new Plan
            {
                ArtifactId = Ulid.NewUlid(),
                RunId = context.Run.Id,
                // docs/adr/0004 (amended): a run is built against a spec
                // snapshot, not a Spec artifact. The snapshot is on the run.
                SpecArtifactRef = context.Run.SnapshotId ?? "(no snapshot)",
                Steps = turn.Value.Steps.Select((s, i) => new PlanStep
                {
                    Order = i + 1,
                    Description = s.Description,
                    TargetPaths = s.TargetPaths.ToArray(),
                }).ToArray(),
                TestStrategy = await TestCommandAsync(context.Run, cancellationToken) ?? "(none declared)",
                CreatedAt = DateTimeOffset.UtcNow,
            };

            return new StageOutcome.Success(
                ArtifactType: "Plan",
                ArtifactContentJson: JsonSerializer.Serialize(plan, ArtifactJsonOptions.Default));
        }
        catch (StageBudgetExceededException ex)
        {
            return new StageOutcome.Failed(FailureClass.NeedsHuman, ex.Message);
        }
        catch (StageAgentException ex)
        {
            return new StageOutcome.Failed(FailureClass.Permanent, ex.Message);
        }
    }

    private async Task<string?> TestCommandAsync(Run run, CancellationToken cancellationToken) =>
        await db.Teams.AsNoTracking()
            .Where(t => t.ProjectId == run.ProjectId && t.IsActive)
            .Select(t => t.TestCommand)
            .SingleOrDefaultAsync(cancellationToken);

    private static (PlanDocument?, SchemaValidationResult) ParsePlan(string text)
    {
        var document = AgentJson.Parse<PlanDocument>(text, out var error);
        if (document is null)
        {
            return (null, Invalid(error!));
        }

        if (document.Steps.Count == 0)
        {
            return (null, Invalid("the plan has no steps; a plan with nothing in it is not a plan"));
        }

        var blank = document.Steps
            .Select((step, index) => (step, index))
            .Where(s => string.IsNullOrWhiteSpace(s.step.Description))
            .ToList();

        return blank.Count > 0
            ? (null, Invalid($"step {blank[0].index + 1} has no description"))
            : (document, SchemaValidationResult.Valid);
    }

    private static SchemaValidationResult Invalid(string message) =>
        new(false, [new SchemaValidationError("(root)", message)]);
}

/// <summary>
/// Writes the code, records it as a patch, and has the workspace server
/// apply it (3d's first constraint).
/// </summary>
public sealed class AgentImplementStageHandler(
    DarkFactoryDbContext db,
    IServerProbe probe,
    StageAgent agent,
    IArtifactStore artifacts,
    ArtifactUrlSigner? urls)
    : AgentStageHandlerBase(db, probe), IStageHandler
{
    public StageId Stage => StageId.Implement;

    public async Task<StageOutcome> ExecuteAsync(StageContext context, CancellationToken cancellationToken)
    {
        if (urls is null)
        {
            return new StageOutcome.Failed(FailureClass.NeedsHuman,
                "Artifact URLs are not configured, so the workspace server cannot fetch a patch. " +
                "Set Artifacts:PublicBaseUrl and Artifacts:SigningKey.");
        }

        var workspaceUrl = await WorkspaceUrlAsync(context.Run, cancellationToken);
        var snapshotSpecIds = await SnapshotSpecIdsAsync(context.Run, cancellationToken);
        var required = await ImplementedSpecIdsAsync(context.Run, snapshotSpecIds, cancellationToken);

        try
        {
            var turn = await agent.RunAsync<ImplementDocument>(
                context, Stage,
                AgentPrompts.ImplementPrefix,
                AgentPrompts.Variable,
                AgentPrompts.ImplementUserMessage,
                text => Task.FromResult(ParseImplement(text, required, snapshotSpecIds)),
                AgentPrompts.ImplementTemplateVersion,
                cancellationToken);

            var files = turn.Value.Files.Select(f => new FileWrite(f.Path, f.Content)).ToList();

            // Read what is there now so the patch is a real diff rather than
            // a blind overwrite — and so a file the agent thinks it is
            // creating but which already exists is handled correctly.
            var before = await ReadExistingAsync(workspaceUrl, files.Select(f => f.Path).ToList(), cancellationToken);

            var patch = UnifiedDiff.Build(
                files.Select(f => (f.Path, before.GetValueOrDefault(f.Path), f.Content)).ToList());

            if (string.IsNullOrWhiteSpace(patch))
            {
                return new StageOutcome.Failed(FailureClass.NeedsHuman,
                    "The implementer returned files identical to what is already there, so there is nothing to apply.");
            }

            var patchArtifact = await artifacts.PutAsync(
                context.Run.OrgId, context.Run.ProjectId, context.Run.Id, "Patch",
                patch, ArtifactContentTypes.Patch, cancellationToken);

            // The workspace server fetches this itself: one artifact, one
            // project, minutes to live (docs/adr/0004).
            var applied = await CallWorkspaceAsync(workspaceUrl, "df.vcs.apply_patch",
                new Dictionary<string, object?> { ["patch_ref"] = urls.Sign(patchArtifact) }, cancellationToken);

            if (!applied.Ok)
            {
                return new StageOutcome.Failed(applied.Failure ?? FailureClass.Retryable,
                    $"The workspace server could not fetch the patch: {applied.Error}");
            }

            using var response = JsonDocument.Parse(applied.Json!);
            if (!response.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                var message = response.RootElement.TryGetProperty("message", out var m)
                    ? m.GetString() : "apply_patch reported failure";
                var failureClass = response.RootElement.TryGetProperty("failure_class", out var fc)
                    ? ParseFailureClass(fc.GetString()) : FailureClass.Retryable;
                return new StageOutcome.Failed(failureClass, $"The patch did not apply: {message}");
            }

            var filesChanged = response.RootElement.TryGetProperty("files_changed", out var fcArray)
                ? fcArray.EnumerateArray().Select(e => e.GetString()!).ToArray()
                : files.Select(f => f.Path).ToArray();

            var changeSet = new ChangeSet
            {
                ArtifactId = Ulid.NewUlid(),
                RunId = context.Run.Id,
                PlanArtifactRef = await LatestRefAsync(context.Run.Id, "Plan", cancellationToken) ?? "(no plan)",
                Branch = BranchName(context.Run),
                PatchRef = ArtifactRef.Format(patchArtifact.Id),
                FilesChanged = filesChanged,
                CommitMessage = turn.Value.CommitMessage,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            return new StageOutcome.Success(
                ArtifactType: "ChangeSet",
                ArtifactContentJson: JsonSerializer.Serialize(changeSet, ArtifactJsonOptions.Default));
        }
        catch (StageBudgetExceededException ex)
        {
            return new StageOutcome.Failed(FailureClass.NeedsHuman, ex.Message);
        }
        catch (StageAgentException ex)
        {
            return new StageOutcome.Failed(FailureClass.Permanent, ex.Message);
        }
    }

    public static string BranchName(Run run) => $"dark-factory/{run.Id.ToLowerInvariant()}";

    private static FailureClass ParseFailureClass(string? value) => value switch
    {
        "permanent" => FailureClass.Permanent,
        "needs_human" => FailureClass.NeedsHuman,
        _ => FailureClass.Retryable,
    };

    /// <summary>
    /// Current content of the files about to be written. A file the
    /// workspace does not have comes back absent, which is how the diff
    /// knows to emit a creation rather than a replacement.
    /// </summary>
    private async Task<Dictionary<string, string>> ReadExistingAsync(
        string workspaceUrl, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var result = await CallWorkspaceAsync(workspaceUrl, "df.files.read_many",
            new Dictionary<string, object?> { ["paths"] = paths }, cancellationToken);

        var existing = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!result.Ok)
        {
            // Treat as all-new rather than failing: `git apply --3way` will
            // still refuse a patch that does not fit, so the cost of being
            // wrong here is a rejected patch, not a corrupted tree.
            return existing;
        }

        using var document = JsonDocument.Parse(result.Json!);
        if (!document.RootElement.TryGetProperty("files", out var files))
        {
            return existing;
        }

        foreach (var file in files.EnumerateArray())
        {
            var path = file.TryGetProperty("path", out var p) ? p.GetString() : null;
            var content = file.TryGetProperty("content", out var c) ? c.GetString() : null;
            var hasError = file.TryGetProperty("error", out _);

            if (path is not null && content is not null && !hasError && content.Length > 0)
            {
                existing[path] = content;
            }
        }

        return existing;
    }

    private async Task<IReadOnlySet<string>> SnapshotSpecIdsAsync(Run run, CancellationToken cancellationToken) =>
        run.SnapshotId is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : (await db.SnapshotMembers.AsNoTracking()
                .Where(m => m.SnapshotId == run.SnapshotId)
                .Select(m => m.SpecId)
                .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

    private async Task<string?> LatestRefAsync(string runId, string type, CancellationToken cancellationToken)
    {
        var artifact = await db.Artifacts.AsNoTracking()
            .Where(a => a.RunId == runId && a.Type == type)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return artifact is null ? null : ArtifactRef.Format(artifact.Id);
    }

    /// <summary>
    /// Validates shape <em>and</em> docs/adr/0024's reference rule, so a
    /// missing or foreign spec reference is caught before anything is
    /// written and gets the same retry treatment as a malformed response.
    /// </summary>
    private static (ImplementDocument?, SchemaValidationResult) ParseImplement(
        string text, IReadOnlySet<string> required, IReadOnlySet<string> snapshot)
    {
        var document = AgentJson.Parse<ImplementDocument>(text, out var error);
        if (document is null)
        {
            return (null, new SchemaValidationResult(false, [new SchemaValidationError("(root)", error!)]));
        }

        var problems = new List<SchemaValidationError>();

        if (document.Files.Count == 0)
        {
            problems.Add(new SchemaValidationError("/files", "no files were returned"));
        }
        if (string.IsNullOrWhiteSpace(document.CommitMessage))
        {
            problems.Add(new SchemaValidationError("/commit_message", "a commit message is required"));
        }

        foreach (var file in document.Files)
        {
            if (string.IsNullOrWhiteSpace(file.Path) || Path.IsPathRooted(file.Path) || file.Path.Contains(".."))
            {
                problems.Add(new SchemaValidationError("/files",
                    $"'{file.Path}' is not a workspace-relative path"));
            }
        }

        if (problems.Count == 0)
        {
            var references = SpecReferences.Check(
                document.Files.Select(f => new FileWrite(f.Path, f.Content)).ToList(), required, snapshot);

            problems.AddRange(references.Problems()
                .Select(p => new SchemaValidationError("/files", p)));
        }

        return problems.Count == 0
            ? (document, SchemaValidationResult.Valid)
            : (null, new SchemaValidationResult(false, problems));
    }
}

internal static class AgentJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Parses a model response into <typeparamref name="T"/>, tolerating a
    /// code fence. Never throws: an unparseable answer is a validation
    /// failure the model can be told about, not an exception.
    /// </summary>
    public static T? Parse<T>(string text, out string? error) where T : class
    {
        error = null;
        var trimmed = StripFences(text);

        try
        {
            var value = JsonSerializer.Deserialize<T>(trimmed, Options);
            if (value is null)
            {
                error = "the response was JSON null";
            }
            return value;
        }
        catch (JsonException ex)
        {
            error = $"the response was not the required JSON object: {ex.Message}";
            return null;
        }
    }

    private static string StripFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline < 0)
        {
            return trimmed;
        }

        var body = trimmed[(firstNewline + 1)..];
        var closing = body.LastIndexOf("```", StringComparison.Ordinal);
        return (closing < 0 ? body : body[..closing]).Trim();
    }
}

internal static class ArtifactJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
}
