# ADR-0008: Durable orchestration via Postgres-backed state machine

## Status
Accepted

## Context
A run can span minutes to hours across six stages, calling out to external
MCP servers that may themselves be slow or momentarily unavailable. The
engine process must be able to crash, restart, or be redeployed without
losing track of runs, and without re-doing work that already completed.

## Decision
Every stage transition is a **checkpoint**, persisted to Postgres before the
next stage begins. A run resumes from its last checkpoint after any crash —
never from `intake`. v1 implements this as a Postgres-backed state machine
(`runs`, `stages`, `checkpoints` tables — see `DarkFactory.Data`) driven by a
background worker (`DarkFactory.Engine`) that polls for runnable work using
`SELECT ... FOR UPDATE SKIP LOCKED`.

**Candidate replacement:** Temporal (.NET SDK) is the leading candidate to
replace this hand-rolled state machine once any of the following becomes
true:
- We need workflow logic more complex than "six linear stages plus hooks"
  (parallel branches, sagas with compensation, sub-workflows).
- Polling `FOR UPDATE SKIP LOCKED` starts showing contention or latency
  problems at the run volumes we're actually seeing.
- We need cross-process/cross-language workflow visibility or replay
  debugging that a hand-rolled table doesn't give us for free.
- We're willing to take on running (or paying for) a Temporal cluster as
  new operational surface area, which is not justified for a single-tenant
  local dev loop today.

Until one of those triggers, the Postgres state machine is simpler to run
(it's "just another table" in the same database we already have) and
sufficient for the fixed six-stage pipeline in ADR-0003.

## Consequences
- Every stage's checkpoint write and the decision to advance must be in the
  same transaction (or made idempotent under retry) so a crash between
  "checkpoint written" and "advance" can't duplicate or skip a stage.
- The worker must be safe to run with multiple replicas from day one (via
  `SKIP LOCKED`), even though v1 Compose runs a single `worker` instance,
  so that scaling out later is a deployment change, not a code change.
- A test must prove resume-from-checkpoint: kill the worker mid-`implement`
  and assert the run continues from `implement`, not `intake`, on restart
  (see `tests/DarkFactory.Engine.Tests`).
- Migrating to Temporal later means replacing the state machine's internals
  behind the same `run`/`stage`/checkpoint concepts the dashboard and front
  MCP surface already depend on — not a rewrite of those consumers.
