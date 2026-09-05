# ADR-0003: Pipeline skeleton and human gates

## Status
Accepted

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
