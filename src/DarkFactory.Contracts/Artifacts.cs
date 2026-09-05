using System.Text.Json.Serialization;

namespace DarkFactory.Contracts;

// Mirrors contracts/schemas/{spec,plan,changeset,testreport}.schema.json.
// See docs/adr/0004-typed-artifacts-by-reference.md.

public sealed record Spec
{
    [JsonPropertyName("artifact_id")] public required string ArtifactId { get; init; }
    [JsonPropertyName("run_id")] public required string RunId { get; init; }
    [JsonPropertyName("title")] public required string Title { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
    [JsonPropertyName("requirements")] public required IReadOnlyList<string> Requirements { get; init; }
    [JsonPropertyName("out_of_scope")] public IReadOnlyList<string> OutOfScope { get; init; } = [];
    [JsonPropertyName("open_questions")] public IReadOnlyList<string> OpenQuestions { get; init; } = [];
    [JsonPropertyName("acceptance_criteria")] public required IReadOnlyList<string> AcceptanceCriteria { get; init; }
    [JsonPropertyName("created_at")] public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record PlanStep
{
    [JsonPropertyName("order")] public required int Order { get; init; }
    [JsonPropertyName("description")] public required string Description { get; init; }
    [JsonPropertyName("target_paths")] public IReadOnlyList<string> TargetPaths { get; init; } = [];
}

public sealed record Plan
{
    [JsonPropertyName("artifact_id")] public required string ArtifactId { get; init; }
    [JsonPropertyName("run_id")] public required string RunId { get; init; }
    [JsonPropertyName("spec_artifact_ref")] public required string SpecArtifactRef { get; init; }
    [JsonPropertyName("steps")] public required IReadOnlyList<PlanStep> Steps { get; init; }
    [JsonPropertyName("test_strategy")] public required string TestStrategy { get; init; }
    [JsonPropertyName("risks")] public IReadOnlyList<string> Risks { get; init; } = [];
    [JsonPropertyName("created_at")] public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record ChangeSet
{
    [JsonPropertyName("artifact_id")] public required string ArtifactId { get; init; }
    [JsonPropertyName("run_id")] public required string RunId { get; init; }
    [JsonPropertyName("plan_artifact_ref")] public required string PlanArtifactRef { get; init; }
    [JsonPropertyName("branch")] public required string Branch { get; init; }
    [JsonPropertyName("patch_ref")] public required string PatchRef { get; init; }
    [JsonPropertyName("files_changed")] public required IReadOnlyList<string> FilesChanged { get; init; }
    [JsonPropertyName("commit_sha")] public string? CommitSha { get; init; }
    [JsonPropertyName("commit_message")] public required string CommitMessage { get; init; }
    [JsonPropertyName("created_at")] public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record TestReport
{
    [JsonPropertyName("artifact_id")] public required string ArtifactId { get; init; }
    [JsonPropertyName("run_id")] public required string RunId { get; init; }
    [JsonPropertyName("changeset_artifact_ref")] public required string ChangesetArtifactRef { get; init; }
    [JsonPropertyName("command")] public required string Command { get; init; }
    [JsonPropertyName("exit_code")] public required int ExitCode { get; init; }
    [JsonPropertyName("passed")] public required int Passed { get; init; }
    [JsonPropertyName("failed")] public required int Failed { get; init; }
    [JsonPropertyName("skipped")] public required int Skipped { get; init; }
    [JsonPropertyName("stdout_ref")] public string? StdoutRef { get; init; }
    [JsonPropertyName("stderr_ref")] public string? StderrRef { get; init; }
    [JsonPropertyName("success")] public required bool Success { get; init; }
    [JsonPropertyName("created_at")] public required DateTimeOffset CreatedAt { get; init; }
}
