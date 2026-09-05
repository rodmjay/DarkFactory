# ADR-0004: Typed artifacts, passed by reference

## Status
Accepted, amended by the [architecture brief v2](../architecture-brief.md)

## Amendment (v2)
`Spec` is no longer a run artifact. Specs are durable project state living
in the spec graph ([ADR-0016](0016-spec-graph-content-addressed-append-only.md)),
not something a stage produces and the engine validates per run. `Plan`,
`ChangeSet`, and `TestReport` remain exactly as below. A run additionally
records the **spec snapshot** (`snapshot_id`) it was built against —
the immutable set of spec revisions in effect when the run's seeding
amendment(s) were approved — so "what was the run actually building
towards" is always answerable later even as the graph keeps evolving.

## Original decision (v1)

## Context
Stages produce and consume documents — a spec, a plan, a diff, a test
report. These can be large, need to be stored durably for audit/resume, and
need a stable shape so downstream stages and the dashboard can rely on their
structure.

## Decision
Four artifact types are defined as JSON Schemas under
`contracts/schemas/`: `Spec`, `Plan`, `ChangeSet`, `TestReport`. Every
artifact produced by a stage is validated against its schema before the
engine persists it and advances the run.

(Per the amendment above, `Spec` is retired as a *run* artifact in v2 — its
schema stays in `contracts/schemas/` as the shape a spec node's canonical
content takes in the graph, not as something `df.work.*` produces per run.)

Artifacts are passed between calls **by reference** (`artifact_ref`, an
opaque id resolvable through the factory's artifact store), not inline.
Callers may additionally pass small inline content (e.g. a short summary or
the first N bytes) when it helps a receiver avoid a round trip, but the
`artifact_ref` is always authoritative and always present.

## Consequences
- Large payloads (patches, logs, test output) never bloat the MCP call
  envelope or clog context windows; only refs move around, and the artifact
  store is the source of truth.
- Every artifact is independently fetchable and inspectable via the
  dashboard's JSON viewer and via `factory://runs/{run_id}` resources,
  which is essential for the "inspect what happened" requirement.
- Schema validation at the stage boundary catches malformed agent output
  before it propagates, and gives a clear `permanent` failure to react to
  (see ADR-0007).
- New artifact types require a new schema file and a version bump to the
  conventions that reference them; existing consumers are unaffected until
  they opt into the new type.
