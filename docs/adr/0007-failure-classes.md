# ADR-0007: Three failure classes drive retry policy

## Status
Accepted

## Context
Stages and hooks can fail in ways that call for different responses:
transient network blips should be retried automatically; a malformed
request should not be retried at all; some failures need a human to look at
them. If every hook implementation (and every agent prompt) improvises its
own judgment about which is which, behavior becomes inconsistent and hard
to reason about.

## Decision
Every hook/tool result that represents a failure must classify itself as
exactly one of:

- `retryable` — transient; safe to retry with backoff (e.g. network
  timeout, rate limit, momentary lock contention).
- `permanent` — will not succeed on retry (e.g. schema validation failure,
  invalid input, a compile error the agent cannot fix by re-running).
- `needs_human` — the engine cannot decide how to proceed (e.g. ambiguous
  spec, a merge conflict the agent isn't confident resolving, an explicit
  gate).

The engine — not the calling agent — owns the reaction to each class (see
ADR-0008's retry policy). Agents and hook implementations only classify;
they do not implement their own retry loops, backoff, or "try again"
behavior.

## Consequences
- Retry/backoff logic lives in exactly one place (the engine), so its
  behavior (ADR-0008: exponential backoff, max 5, for `retryable`) is
  consistent across every stage and every spoke server.
- `permanent` failures fail fast, surfacing a clear `stage.failed` event
  instead of masking a real bug behind silent retries.
- `needs_human` failures reuse the same gate machinery as planned human
  gates (ADR-0003), so the dashboard has one UI pattern ("this run is
  waiting on you") for both planned approvals and unplanned escalations.
- Every convention document must specify, for each tool, what failure
  classes it can realistically return and under what conditions.
