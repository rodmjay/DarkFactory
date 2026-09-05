# ADR-0028: Each project has a standing team of agents; members are native or MCP servers

## Status
Accepted — supersedes `agent_policies` from the architecture brief's
original data model with `team_members` and `assignments`

## Context
[ADR-0022](0022-model-assignment-is-policy.md) established that model
assignment is configuration, not code, via a flat `agent_policies` table
keyed by stage-or-task. That's too thin once agents have skills,
capabilities, budgets, and fallback behavior of their own, and once some
"agents" are external MCP servers rather than in-process code — a flat
policy table has nowhere to hang any of that.

## Decision
A project has one active **team**, versioned and snapshotted the same way
the spec graph is ([ADR-0016](0016-spec-graph-content-addressed-append-only.md)):
a run records the **team snapshot** (`team_snapshot_id`) it was built with,
so "which agents, with which skills, built this" is always answerable
later even as the team evolves.

A team member has: a role name, a Foundry deployment
([ADR-0027](0027-foundry-model-gateway.md)), a set of **skills**
(versioned instruction bundles — the same shape as Claude Code skills), the
`df.*` capabilities it's allowed to call, a token budget, and a fallback
deployment for when its primary is unavailable or over budget. The team's
**assignment map** binds pipeline stages and hook points
([ADR-0003](0003-pipeline-skeleton.md)) to specific members — this is
where [ADR-0022](0022-model-assignment-is-policy.md)'s verifiability
criterion actually gets applied, per project, per stage.

Two member kinds share a seat in the assignment map:

- **Native agents** — definitions executed in-process by the engine. The
  default, and what step 3c's stub team uses.
- **Agent servers** — external MCP servers declaring `domain: agent` in
  `df.describe()` ([ADR-0018](0018-df-namespace-and-describe-handshake.md)),
  exposing `df.agent.run(task_ref, context_ref)`. They are required to
  obtain their model calls through MCP **sampling** rather than holding
  their own model access, so the factory retains model choice, policy,
  budget, and metering for every member regardless of who wrote the agent.
  Premium and community agents ship this way.

Standards servers may ship **team templates** — a starting assignment map
and skill set for a given stack. Skills themselves are a fourth free
plugin surface, alongside the three server tiers
([ADR-0019](0019-three-plugin-tiers.md)).

**Open (not resolved here):** whether a project may run multiple teams at
once — e.g. one per architectural layer. The schema is built so it could;
v1's UI and engine only ever look at one active team per project.

## Consequences
- `agent_policies` (as sketched in the original architecture brief's data
  model) is retired before it was ever built — `team_members` and
  `assignments` are its replacement, and are the tables step 3c actually
  creates.
- A run's provenance now includes not just the spec snapshot
  ([ADR-0004](0004-typed-artifacts-by-reference.md), as amended) but the
  team snapshot too — reproducing "why did the run behave this way"
  requires both.
- Requiring agent servers to use MCP sampling for model calls (rather than
  letting them hold their own API keys) is what keeps
  [ADR-0027](0027-foundry-model-gateway.md)'s cost/eval/routing guarantees
  true even for third-party agents the factory didn't write.
- This ADR's tables (`teams`, `team_revisions`, `team_members`, `skills`,
  `skill_revisions`, `assignments`) are step 3c work, not step 3a — 3a's
  migration does not create them.

## Amendment: centralized skills

Skills are held **centrally in the factory**: one library per org with
project-level overrides, each skill a stable id with immutable revisions —
the same shape as the spec graph
([ADR-0016](0016-spec-graph-content-addressed-append-only.md)), for the
same reason.

Native agents read skills from the factory at run time. Claude Code
sessions receive the project's resolved skill set **mechanically**, via
`SessionStart → df.skills.sync`
([ADR-0030](0030-claude-code-hooks-as-factory-commands.md)), which pins
them into `.claude/skills/` by revision; the factory may also expose itself
as a Claude Code plugin marketplace. Standards servers may ship skills;
community skills are free.

**A run's team snapshot records every skill revision in use**, so behavior
is always traceable to a skill version — a run that behaved oddly can be
explained by the instructions it actually had, not the ones the library
holds today.

**Data model:** `skills`, `skill_revisions`, and
`skill_assignments(scope ∈ {org, project, team_member}, skill_id,
revision_hash)`.

The consequence worth noting: skills stop being a per-repository file that
drifts between checkouts and become versioned, assignable configuration
with an owner. That is what makes them a plugin surface rather than a
convention.

## Amendment: personas (deferred)

Team members may eventually be sold as named, pictured **personas** — a
role, a deployment, skills and a price under a name and portrait, turning
the team page into a roster. A persona also declares a **speed** preset
(quick / balanced / deliberate) mapping to the gateway's per-call thinking
budget, so two personas on the same model may differ in price and depth.
Thinking tokens count against member budgets
([ADR-0032](0032-usage-fact-rows.md)).

No schema yet. `team_members` leaves room for a nullable `persona_id`, and
`model_calls` records `persona_id` and `thinking_preset` from the start so
the question "is deliberate worth it?" is answerable the day personas
arrive rather than only after a migration.
