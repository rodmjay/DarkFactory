# ADR-0005: Call envelope on every outbound call

## Status
Accepted

## Context
The factory makes many outbound MCP calls (to workspace/theme/plugin
servers) on behalf of a run. Each call needs enough context for the callee
to correlate it with the run, deduplicate retries, propagate tracing, and
respect a deadline — without every tool's input schema reinventing these
fields.

## Decision
Every outbound tool call from the factory carries a call envelope:

| Field             | Meaning                                                        |
|-------------------|------------------------------------------------------------------|
| `run_id`          | The run this call is part of.                                    |
| `stage_id`        | The stage currently executing (e.g. `implement`).                 |
| `project_id`      | The project the run belongs to.                                   |
| `idempotency_key` | Stable per logical attempt; lets a callee dedupe retried calls.   |
| `trace_id`        | Propagated for distributed tracing (OpenTelemetry).               |
| `deadline`        | ISO-8601 timestamp after which the caller will treat the call as timed out. |

Conforming servers must echo the envelope back on the result (unchanged,
alongside their own response payload) so the factory can verify it heard
back from the call it thinks it made. The full shape is defined in
[`docs/conventions/envelope.md`](../conventions/envelope.md) and
[`contracts/schemas/envelope.schema.json`](../../contracts/schemas/envelope.schema.json).

## Consequences
- Every hook/tool implementation across every convention has one shared,
  small piece of boilerplate to parse and echo, rather than each convention
  inventing its own correlation fields.
- Idempotency keys make the retry policy in ADR-0007 safe to apply blindly:
  a `retryable` failure can be retried without the callee double-applying
  side effects, as long as the callee honors the key.
- Deadlines let the factory time out a call deterministically rather than
  guessing, and let a slow callee choose to return early with a partial
  result instead of being killed mid-work.
- `org_id` is deliberately *not* in the envelope in v1 (see ADR-0010) — it
  is a column on the factory's own tables, not something spoke servers need
  to see yet.
