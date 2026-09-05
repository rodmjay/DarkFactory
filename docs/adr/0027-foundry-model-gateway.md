# ADR-0027: Microsoft Foundry powers the intelligence layer

## Status
Accepted — amends [ADR-0009](0009-factory-owns-model-access.md)

## Context
[ADR-0009](0009-factory-owns-model-access.md) settled that the factory
holds its own model access rather than borrowing a host's. It left open
*how* — a raw vendor API key is a real operational and security liability
to hold in a multi-tenant hosted product, and gives no first-party story
for routing, metering, or evaluation across many projects and orgs.

## Decision
All model inference, routing, and evaluation runs through Microsoft
Foundry on Azure. The factory never holds a model vendor's API key in
production; it authenticates to Foundry with an Entra managed identity.
Deployments are named by role — `architect`, `planner`, `implementer`,
`reviewer`, `router` — and referenced by name from team definitions
([ADR-0028](0028-project-agent-teams.md)), so swapping the model behind a
role is a Foundry configuration change, not a code change.

Foundry's model router performs the small-model triage pass
[ADR-0023](0023-index-standards-not-live-reads.md) already calls for.
Foundry usage metering is the source of truth for token cost per project,
per run, and per agent — not a parallel accounting system the factory
maintains itself. Foundry Control Plane evaluations and tracing score
agent outputs, and the factory records evaluation results as run metadata
alongside the audit log ([ADR-0012](0012-audit-log.md)).

**Bring-your-own Foundry:** an org may point its projects at its own
Foundry resource, so its tokens bill to its own Azure account and its
prompts stay in its own tenant (with zero-data-retention configuration
where required). Hosted customers who don't bring their own use ours.

Foundry Agent Service is *not* used for the core pipeline — the factory's
own durable engine ([ADR-0008](0008-durable-orchestration.md)) remains the
orchestrator, not Foundry's — though Foundry Agent Service may host
external agent servers later ([ADR-0028](0028-project-agent-teams.md)).

Implementation: an `IModelGateway` interface in `DarkFactory.Core`, with a
Foundry-backed implementation for production. Local development uses the
same gateway interface against a plain API key — nothing above the gateway
abstraction knows which provider is actually behind it.

## Consequences
- No component above `IModelGateway` ever sees a model name, an API key, or
  a provider SDK type — those all live behind the gateway, which is what
  makes bring-your-own-Foundry and role-based deployment swapping both
  possible without touching stage-handler or agent code.
- Cost and evaluation data the factory would otherwise have to build itself
  (per-project token accounting, output scoring) comes from Foundry
  instead — a deliberate build-vs-integrate choice in favor of integrate.
- Local dev and self-hosted deployments ([ADR-0025](0025-identity-tenancy-deployment-modes.md))
  need *some* gateway implementation that doesn't require an Azure tenant —
  the plain-API-key gateway is that fallback, not a second-class path.
- This ADR is implemented starting step 3c
  (`docs/architecture-brief.md`'s "Step 3 amendment"); the deterministic
  stub stage handlers written in step 2 predate `IModelGateway` entirely
  and don't yet call it.
