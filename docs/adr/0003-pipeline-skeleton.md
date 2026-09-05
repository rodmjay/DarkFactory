# ADR-0003: Pipeline skeleton and human gates

## Status
Accepted, amended by the [architecture brief v2](../architecture-brief.md)

## Amendment (v2)
`intake` and `spec` are removed from the run pipeline. They are the
conversation ([ADR-0017](0017-conversation-is-the-product.md)) — an ongoing
exchange with an architect agent that produces, when it settles, a proposed
amendment against the spec graph ([ADR-0016](0016-spec-graph-content-addressed-append-only.md)).
A run is created only from one or more *approved* amendments, never from
raw text. The pipeline a run actually executes is now:

```
conversation -> spec amendment (gate) -> plan -> implement -> verify -> ship
```

where "conversation" and "spec amendment (gate)" happen before a run
exists at all — the run itself starts at `plan`. The PR-approval gate
before `ship` is unchanged. Everything below this point is the original
(v1) decision, kept for its rationale on hook points and gate mechanics,
which still apply to `plan -> implement -> verify -> ship`.

## Original decision (v1, six-stage run)

## Context
Every unit of work the factory carries out needs a predictable shape so the
engine, dashboard, and plugins can all reason about "where a run is" the same
way.

## Decision
The pipeline has exactly six stages, always in this order:

```
intake -> spec -> plan -> implement -> verify -> ship
```

Every stage has a hook point immediately before it and immediately after it
(`before:<stage>`, `after:<stage>`), so plugins can attach behavior without
the engine knowing about them by name.

v1 defines exactly two human gates:
- **Approve the spec** before the run is allowed to proceed from `spec` into
  `plan`.
- **Approve the PR** before the run is allowed to proceed from `verify` into
  `ship` (i.e. before the PR opened in `ship` is treated as the final
  output — practically, `ship` prepares the PR and the gate confirms it
  should be opened/merged).

A gated stage parks the run with status `awaiting_approval` and emits a
`gate.waiting` event; `work.approve` / `work.reject` (ADR-0003 front surface)
resume or fail it.

## Consequences
- The six-stage skeleton is fixed in v1; stages are not user-configurable,
  though hook points make their behavior extensible.
- Gates are engine-level concepts, not plugin concepts — a plugin can *add*
  a gate at a hook point in later versions, but the two v1 gates are
  hardcoded into the state machine.
- Because hook points exist before and after every stage, adding a third
  gate later (e.g. before `ship` proper, or before `implement`) does not
  require changing the pipeline shape, only registering a plugin/hook that
  requests a gate.
