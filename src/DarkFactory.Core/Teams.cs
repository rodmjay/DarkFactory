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
