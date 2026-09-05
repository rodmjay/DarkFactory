# ADR-0006: Four communication channels

## Status
Accepted

## Context
Different stage work has different latency/duration profiles: a quick
lookup, a long-running test suite, a multi-minute build. One channel shape
(request/response) does not fit all of them, but we don't want an unbounded
number of transport patterns either.

## Decision
Exactly four channels exist:

1. **Synchronous tool call** (default). Request, response, done. Used for
   anything that reliably completes within the call's deadline.
2. **MCP progress notifications** (in-session). For a call that's
   synchronous but long enough to want incremental progress reported back
   to the same MCP session while it runs.
3. **Async job with callback to the factory's webhook ingress.** For calls
   that may exceed any reasonable inline deadline (e.g. a large test run).
   The callee returns `{ job_id }` immediately; when done, it `POST`s
   completion to `/webhooks/jobs/{job_id}` on the factory, using the signed
   token the factory supplied in the envelope for that call.
4. **Internal durable event bus.** Not exposed to spoke servers at all —
   this is how the engine, dashboard (via SignalR), and audit log observe
   state changes (`stage.completed`, `gate.waiting`, etc.) inside the
   factory's own process boundary.

Webhooks flow **only** into and out of the factory — never directly between
two spoke servers, consistent with the hub-and-spoke topology (ADR-0001).

## Consequences
- Spoke server authors only need to reason about three channels (sync,
  progress, async+webhook); the event bus is a core implementation detail.
- The async job pattern requires the factory to expose a stable, signed
  webhook ingress endpoint per job, which the `DarkFactory.Mcp` host owns.
- Because webhooks never go spoke-to-spoke, a compromised or buggy spoke
  server cannot use webhooks to reach another spoke directly; every
  notification is re-validated and re-dispatched by the factory.
