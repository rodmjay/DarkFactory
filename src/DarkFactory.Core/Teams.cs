namespace DarkFactory.Core;

// A project's standing team of agents (docs/adr/0028). Minimal in this
// slice: `teams`, `team_members` and `assignments` only. Skills and team
// snapshotting (team_revisions, runs.team_snapshot_id) are step 3d.
//
// This replaces docs/adr/0022's flat `agent_policies`, which was retired
// before it was ever built.

/// <summary>The roles a built-in team template fills. Deployment names in Foundry follow these (docs/adr/0027).</summary>
public static class AgentRoles
{
    public const string Architect = "architect";
    public const string Planner = "planner";
    public const string Implementer = "implementer";
    public const string Reviewer = "reviewer";
    public const string Router = "router";
}

/// <summary>
/// The points a team member can be bound to. Pipeline stages
/// (docs/adr/0003) plus the non-stage work the factory does — the
/// conversation is not a stage any more (ADR-0003 as amended), but it is
/// still work that needs an agent assigned to it.
/// </summary>
public static class AssignmentPoints
{
    public const string Conversation = "conversation";
    public const string Triage = "triage";
    public const string Plan = "plan";
    public const string Implement = "implement";
    public const string Verify = "verify";
    public const string Ship = "ship";
}

public sealed class Team
{
    public required string Id { get; init; }
    public required string ProjectId { get; init; }
    public required string OrgId { get; init; }
    public required string Name { get; set; }

    /// <summary>
    /// docs/adr/0028 leaves multiple concurrent teams open. The schema
    /// allows it; v1 only ever looks at the one active team per project,
    /// which is what this flag makes unambiguous.
    /// </summary>
    public required bool IsActive { get; set; }

    /// <summary>
    /// What `verify` runs. Team-level rather than project-level because it
    /// is a statement about how this team judges its own work
    /// (docs/adr/0022's verifiability criterion), and because a team
    /// template can ship one.
    ///
    /// Null means the team has not declared one, and `verify` fails rather
    /// than inventing a command — a stage that reports success because it
    /// found nothing to run is worse than one that admits it is not
    /// configured.
    /// </summary>
    public string? TestCommand { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed class TeamMember
{
    public required string Id { get; init; }
    public required string TeamId { get; init; }

    /// <summary>A role name, e.g. <see cref="AgentRoles.Architect"/>.</summary>
    public required string Role { get; init; }

    /// <summary>
    /// A Foundry deployment name (docs/adr/0027) — never a vendor model id.
    /// Swapping the model behind a role is a Foundry change, so nothing in
    /// this row needs to know what model it resolves to.
    /// </summary>
    public required string Deployment { get; set; }

    /// <summary>Used when the primary is unavailable or over budget (docs/adr/0028).</summary>
    public string? FallbackDeployment { get; set; }

    /// <summary>Null means no explicit cap; the org's default applies.</summary>
    public int? TokenBudget { get; set; }

    /// <summary>The <c>df.*</c> capabilities this member is allowed to call, as a JSON array.</summary>
    public required string CapabilitiesJson { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Binds an assignment point to a member. This is where
/// docs/adr/0022's verifiability criterion is actually applied — per
/// project, per stage — rather than being a principle written down nowhere
/// the code can see.
/// </summary>
public sealed class Assignment
{
    public required string Id { get; init; }
    public required string TeamId { get; init; }
    public required string Point { get; init; }
    public required string TeamMemberId { get; set; }
}

/// <summary>
/// One row per gateway call (docs/adr/0032). Append-only, written by the
/// gateway and never by an agent.
///
/// Cached and thinking counts are <em>breakdowns</em>: cached input is part
/// of <see cref="InputTokensUncached"/> + <see cref="InputTokensCached"/>
/// making up the input, and thinking is part of the output. They are held
/// apart because they are billed apart — a cost model built on totals is
/// wrong in the direction that flatters us.
/// </summary>
public sealed class ModelCall
{
    public required string Id { get; init; }

    // ---- identity ----
    public required string OrgId { get; init; }
    public string? ProjectId { get; init; }
    public string? RunId { get; init; }
    public string? BatchId { get; init; }

    /// <summary>A pipeline stage, or a non-stage point such as "conversation".</summary>
    public string? StageId { get; init; }

    public string? TaskId { get; init; }
    public required int Attempt { get; init; }
    public string? TeamMemberId { get; init; }
    public string? PersonaId { get; init; }

    /// <summary>The role the caller asked for; the model behind it is <see cref="ModelFamily"/>.</summary>
    public required string Deployment { get; init; }

    public required string Provider { get; init; }
    public required string ModelFamily { get; init; }

    // ---- inputs ----
    public required int InputTokensUncached { get; init; }
    public required int InputTokensCached { get; init; }
    public required int CacheWriteTokens { get; init; }
    public string? ContextPackRef { get; init; }
    public string[] SkillRevisions { get; init; } = [];
    public string? PromptTemplateVersion { get; init; }
    public string? ThinkingPreset { get; init; }

    // ---- outputs ----
    public required int OutputTokens { get; init; }
    public required int ThinkingTokens { get; init; }
    public required long LatencyMs { get; init; }
    public decimal? Cost { get; init; }

    // ---- outcome, completed by the caller once it knows one ----
    public bool? ArtifactValidFirstTry { get; set; }
    public bool? Retried { get; set; }
    public bool? Steered { get; set; }
    public string? StageResult { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>What a budget counts: billable volume, not a sum of every column.</summary>
    public int TotalTokens => InputTokensUncached + InputTokensCached + OutputTokens;
}
