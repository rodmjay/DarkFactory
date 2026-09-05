# ADR-0012: Audit log from day one

## Status
Accepted

## Context
The factory acts on the customer's behalf against real repos and (later)
real deployments. "What did the factory actually do, and why" must be
answerable after the fact, not just observable live on the dashboard.

## Decision
Every outbound tool call the factory makes to a workspace/theme/plugin
server is recorded in an `audit_entries` table, capturing:

- the full call envelope (`run_id`, `stage_id`, `project_id`,
  `idempotency_key`, `trace_id`, `deadline`),
- the target server (which workspace/theme/plugin, and its URL/identity),
- inputs, **by reference** (an `artifact_ref` or a hash, not necessarily the
  raw payload) — consistent with ADR-0004's by-reference passing,
- the result's failure class if it failed (`retryable` / `permanent` /
  `needs_human`) or success otherwise,
- duration, and
- cost (model tokens/dollars where applicable, e.g. stage agent calls).

Audit entries are append-only and are never mutated after being written.

## Consequences
- `factory://runs/{run_id}` can be backed directly by audit entries plus
  events, giving hosts and the dashboard a complete "what happened" view
  without a separate logging pipeline.
- Because inputs are stored by reference, the audit log itself doesn't
  become a second, uncontrolled copy of potentially large or sensitive
  payloads — it points at the same artifact store that ADR-0004 already
  defines.
- Cost tracking here is the seed of future billing/metering (explicitly out
  of scope otherwise for this slice), so the column exists now even though
  nothing bills against it yet.
