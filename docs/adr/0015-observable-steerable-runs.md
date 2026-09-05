# ADR-0015: Runs are observable and steerable by any authorized client

## Status
Accepted

## Context
Once the factory can run for minutes to hours against a real spec, "submit
and wait" is not enough. A user watching a run needs to see what it's doing
as it happens, and needs a way to correct course without blowing away
progress already made.

## Decision
Two new front-surface primitives make a run observable and steerable
regardless of which client is watching:

- `df.work.attach(run_id)` streams the run's activity: current stage, the
  agent's current turn, every outbound tool call (per
  [ADR-0012](0012-audit-log.md)'s audit entries), and artifacts as they're
  produced. The dashboard (step 4) and any external client (a host's own
  UI, a CLI) consume the same stream — there is no dashboard-only channel.
- `df.work.steer(run_id, message)` injects guidance into the *next* agent
  turn of the current stage without cancelling the run. It is recorded as
  an event, the same way a gate approval is, so the audit trail shows
  exactly what was said and when.

Review is promoted to a first-class role rather than being only a human
gate: an `after:stage` hook (see [ADR-0003](0003-pipeline-skeleton.md)) may
be a human, a plugin, or a model, and whichever it is produces a typed
`Review` artifact that either approves or steers — the same shape either
way.

## Consequences
- The engine's event log (already the transactional outbox from
  [ADR-0008](0008-durable-orchestration.md)) is the single source both
  `attach` and the dashboard read from; nothing about steerability requires
  a second notification path.
- A steering message doesn't restart or reclaim the run — it's read by the
  in-progress stage handler on its next turn, so the lease/checkpoint
  mechanics are unaffected.
- Because `Review` is a typed artifact regardless of who produced it, a
  human reviewer and a QA agent plugin are interchangeable at the
  `after:stage` hook point without the engine caring which one it got.
