# Call Envelope & Failure Classes — v0.1

Status: **Stable for v1.** See [ADR-0005](../adr/0005-call-envelope.md) and
[ADR-0007](../adr/0007-failure-classes.md).

This document defines the envelope every Dark Factory outbound call carries,
and the failure-class vocabulary every hook/tool result uses. Both
Workspace, Theme, and Plugin convention servers must support this document;
it is shared infrastructure, not owned by any one convention.

JSON Schema: [`contracts/schemas/envelope.schema.json`](../../contracts/schemas/envelope.schema.json),
[`contracts/schemas/hookresult.schema.json`](../../contracts/schemas/hookresult.schema.json).

## Envelope shape

Every tool call the factory makes to a spoke server includes an `envelope`
object alongside the tool's own arguments:

```json
{
  "envelope": {
    "run_id": "run_01hz...",
    "stage_id": "implement",
    "project_id": "proj_01hz...",
    "idempotency_key": "run_01hz...:implement:attempt-1",
    "trace_id": "00-4bf92f...-1",
    "deadline": "2026-09-04T21:15:00Z"
  },
  "...tool-specific arguments": "..."
}
```

| Field             | Type              | Notes |
|-------------------|-------------------|-------|
| `run_id`          | string            | Stable for the life of the run. |
| `stage_id`        | string            | One of the six pipeline stages currently executing. |
| `project_id`      | string            | The project the run belongs to. |
| `idempotency_key` | string            | Stable per logical attempt. Servers that perform side effects (e.g. `vcs.commit`) must treat repeated calls with the same key as a no-op replay of the first result, not a new action. |
| `trace_id`        | string            | W3C trace-context compatible. Propagate onto any spans the server creates. |
| `deadline`        | string (ISO-8601) | The caller intends to treat the call as timed out after this instant. Servers should return a partial/`retryable` result rather than being killed mid-write when possible. |

Conforming servers **must echo the envelope back unchanged** on their
response, alongside their own result payload, so the factory can confirm
which call a given response belongs to.

## Failure classes

Any tool result that represents a failure must include a `failure_class`:

```json
{
  "envelope": { "...": "echoed back" },
  "ok": false,
  "failure_class": "retryable",
  "message": "upstream git remote timed out after 30s"
}
```

- **`retryable`** — transient; the engine will retry with exponential
  backoff, up to 5 attempts (see [ADR-0007](../adr/0007-failure-classes.md),
  [ADR-0008](../adr/0008-durable-orchestration.md)). Examples: network
  timeout, rate limiting, lock contention.
- **`permanent`** — will not succeed on retry. The engine fails the stage
  immediately and emits `stage.failed`. Examples: schema validation
  failure, invalid arguments, a compile error the agent cannot resolve by
  re-running the same call.
- **`needs_human`** — the engine cannot decide how to proceed on its own.
  The run parks as a gate (`gate.waiting`) until a human resolves it via
  `work.approve` / `work.reject`. Examples: ambiguous spec, an unresolved
  merge conflict, an explicit approval gate.

Servers must not implement their own retry loops for `retryable` failures —
classify and return; the engine owns the retry.

## Async jobs

For calls too long to complete inline, return:

```json
{ "envelope": { "...": "echoed back" }, "job_id": "job_01hz..." }
```

immediately, then `POST` completion to
`{factory_base_url}/webhooks/jobs/{job_id}` with the signed token supplied
in the original call's envelope (as `envelope.callback_token`, not shown
above since it is populated by the factory only on calls it expects may go
async). See [ADR-0006](../adr/0006-four-channels.md).
