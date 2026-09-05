namespace DarkFactory.Core;

// Domain entities for the factory's own state (never customer code). Every
// tenant-scoped record carries OrgId (docs/adr/0010) and ProjectId
// (docs/adr/0011). Persistence mapping lives in DarkFactory.Data (step 2);
// these are plain records so Core has no dependency on EF Core.

public sealed class Project
{
    public required string Id { get; init; }
    public required string OrgId { get; init; }
    public required string Name { get; set; }
    public required string WorkspaceMcpUrl { get; set; }
    public string? WorkspaceName { get; set; }
    public string? WorkspaceRoot { get; set; }
    public string[] StackHints { get; set; } = [];
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed class WorkItem
{
    public required string Id { get; init; }
    public required string OrgId { get; init; }
    public required string ProjectId { get; init; }
    public required string Input { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed class Run
{
    public required string Id { get; init; }
    public required string OrgId { get; init; }
    public required string ProjectId { get; init; }
    public required string WorkItemId { get; init; }
    public required StageId CurrentStage { get; set; }
    public required RunStatus Status { get; set; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>A persisted checkpoint: the durability unit for resume-after-crash. See docs/adr/0008.</summary>
public sealed class StageCheckpoint
{
    public required string Id { get; init; }
    public required string RunId { get; init; }
    public required StageId Stage { get; init; }
    public required RunStatus Status { get; init; }
    public string? ArtifactRef { get; set; }
    public int Attempt { get; set; } = 1;
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed class Artifact
{
    public required string Id { get; init; }
    public required string OrgId { get; init; }
    public required string ProjectId { get; init; }
    public required string RunId { get; init; }
    public required string Type { get; init; } // "Spec" | "Plan" | "ChangeSet" | "TestReport"
    public required string ContentJson { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed class Gate
{
    public required string Id { get; init; }
    public required string RunId { get; init; }
    public required GateKind Kind { get; init; }
    public required GateStatus Status { get; set; }
    public string? Reason { get; set; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

public sealed class Event
{
    public required string Id { get; init; }
    public required string RunId { get; init; }
    public required string Type { get; init; } // e.g. "stage.completed", "gate.waiting"
    public string? DataJson { get; set; }
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>An outbound call record. See docs/adr/0012-audit-log.md.</summary>
public sealed class AuditEntry
{
    public required string Id { get; init; }
    public required string OrgId { get; init; }
    public required string ProjectId { get; init; }
    public required string RunId { get; init; }
    public required string StageId { get; init; }
    public required string TargetServer { get; init; }
    public required string ToolName { get; init; }
    public string? InputRef { get; init; }
    public bool Success { get; init; }
    public FailureClass? FailureClass { get; init; }
    public required long DurationMs { get; init; }
    public decimal? CostUsd { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
