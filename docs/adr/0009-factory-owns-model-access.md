# ADR-0009: The factory owns model access

## Status
Accepted

## Context
Hosts connecting to the factory (Claude Code, Cursor, CI) are themselves
often backed by a model. It would be possible to have the factory ask the
*host* to do the model work it needs (spec-writing, planning, etc.) via
sampling/callback. Instead, we need the factory to be able to run stage
agents even when no host is attached (e.g. a scheduled run, a webhook-
triggered retry), and to have a single place to meter and audit model
usage.

## Decision
The factory holds its own `ANTHROPIC_API_KEY` (from environment) and calls
the model directly for every stage agent (`intake`, `spec`, `plan`,
`implement`, `verify`, `ship`). Hosts submit work and watch it progress —
they do not lend their own model session to the factory, and the factory
never uses MCP sampling to borrow a host's model.

## Consequences
- Runs are not coupled to a host staying connected; a host can submit work,
  disconnect, and reconnect later to check status, because the factory's
  own model access is what's driving the run forward.
- All model spend is metered and audited in one place (ADR-0012's audit
  log), rather than being invisible to the factory when a host's model
  happens to do the work.
- The factory needs its own cost/rate-limit handling for the Anthropic API,
  independent of whatever the host is doing with its own model access.
- `ANTHROPIC_API_KEY` is a required environment variable for the `factory`
  and `worker` Compose services (see `.env.example`); local dev cannot run
  real stage agents without it, though the skeleton in this first slice
  does not yet call the model.
