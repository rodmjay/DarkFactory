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

## Note (added during step 3d): Anthropic-direct is the development provider

`IModelGateway` now has two implementations.

**Microsoft Foundry remains the production target**, and everything above
about it stands: Entra managed identity, no vendor key in production,
role-named deployments, Foundry metering as the source of truth for cost,
and the bring-your-own-Foundry story that lets an org keep prompts in its
own tenant and tokens on its own bill.

**`DarkFactory.Anthropic` calls the Anthropic Messages API directly**, and
exists so the factory can be built, tested and demonstrated before a
Foundry resource is provisioned. It is selected by
`ModelGateway:Provider` (`Anthropic` | `Foundry`); with neither set, the
provider is inferred from whichever is configured, and having *both*
configured is an error rather than a silent choice, because picking one on
a customer's behalf is how a run gets billed to the wrong account.

Role names are the interface on both. `architect`, `planner`,
`implementer`, `reviewer` and `router` map to Foundry deployment names on
one and to model ids on the other, in configuration — so
[ADR-0022](0022-model-assignment-is-policy.md)'s policy is expressed the
same way regardless of who serves it, and nothing above the gateway changes
when the provider does.

Both report token usage identically, which is what
[ADR-0028](0028-project-agent-teams.md)'s budgets count. A budget that only
worked on one provider would not be a budget.

### Consequence worth stating

A second implementation is the first real test of whether this ADR's
abstraction was ever true. An interface with one implementation is a claim;
two is a seam. Provider containment is therefore a rule with teeth: only
`DarkFactory.Hosting` — the composition helper both `factory` and `worker`
call — may reference either provider project, and a test asserts nothing
else does.

## Amendment: the output ceiling belongs to the gateway

`max_tokens` was a hardcoded 8192 on every call. That is a fact about a
model, and the layer that held it — `ModelRequest`'s default, and later a
constant in `TeamService` — is deliberately kept ignorant of models: callers
name a role, and only the gateway knows what that role resolves to. The
consequence was not theoretical. The 3d acceptance demo truncated the
implementer's first attempt mid-JSON, and the failure presented as "the
response was not the required JSON object", which sent both the model and
the reader after a formatting mistake that was not there.

**A null output limit means the model's own maximum, resolved by the
gateway.** The Anthropic gateway holds a table of ceilings taken from
`GET /v1/models/{id}` (`max_tokens`) and pinned at authoring time rather
than fetched per call — a lookup that can fail would make truncation come
back intermittently, dependent on someone else's uptime, which is a worse
failure than a stale constant. A model the build does not recognise falls
back to 8192 **and logs a warning**, because silence there is exactly how an
implementer on a newly-configured model would quietly start truncating
again. Foundry sends no ceiling at all unless one is configured: a
deployment name says nothing about the model behind it, so the factory would
be guessing, and the provider's own default is the honest answer.

`team_members.max_output_tokens` survives as an **override**, not as the
source of the number. Non-null still wins, and on Foundry it is the only
lever there is — without it a Foundry-served implementer would be stuck at
the fallback with no configuration escape, leaving the production target
worse off than the development one.

### Consequence: the per-call ceiling is no longer a spend bound

It never was one deliberately, but it was acting as one. Budgets are checked
*after* a call by design ([ADR-0032](0032-usage-fact-rows.md)) — the cost of
a call is not knowable until it returns — so the 8192 ceiling was, by
accident, the only thing bounding a single runaway response. It is now the
model's maximum, sixteen times larger on the current models, and every
member seeded from the built-in template has a null `token_budget`.

The exposure is every member, on every call, including the conversation —
not just the implementer that exposed the bug. The architect is the most
frequently called of the lot, since it runs on every conversational turn,
and it now resolves to the same ceiling with the same null budget.

So one looping agent can now emit substantially more before anything
notices. In normal operation this is a tail risk rather than a cost — a
ceiling is not a target, and the acceptance re-run in `docs/evidence/3d/`
emitted 15,208 tokens against a 128,000 ceiling — but it is sixteen times
fatter than yesterday with
nothing behind it. This is recorded rather than solved: capping a call at
`min(model ceiling, remaining budget)` is a real design question — it needs
a token estimate the gateway does not have, and a partial answer is worth
less than a whole one — not something to settle inside a limit change. Until
then, a member's `token_budget` is the only other bound, and the built-in
template should probably stop leaving it null.

### Streaming is the real fix at 128k, and is out of scope

The Anthropic gateway is one non-streaming JSON POST. At a 128,000-token
ceiling a single response can outlive any HTTP timeout worth setting, and
raising the client timeout to 15 minutes is an honest statement of what this
client can wait for rather than a claim that it is enough. Streaming the
Messages API — and surfacing partial output to `df.work.attach`
([ADR-0015](0015-observable-steerable-runs.md)) rather than only the
finished artifact — is the actual answer, and it is deliberately not
attempted here: it changes the gateway's shape, the recording of usage rows,
and the run event stream at once.
