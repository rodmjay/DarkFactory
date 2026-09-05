# ADR-0022: Model assignment is a policy, not code

## Status
Accepted — extends [ADR-0009](0009-factory-owns-model-access.md)

## Context
[ADR-0009](0009-factory-owns-model-access.md) established that the factory
holds its own model access. It didn't say which model handles which piece
of work, and hardcoding that choice into stage-agent code would make it
impossible to offer different cost/quality tiers or to correct a bad
default without a deploy.

## Decision
Which model handles which stage or task is configuration
(`agent_policies`), read per project and per plan, not compiled into stage
handler code. The governing criterion for assignment is **verifiability of
outcome**, not seniority of task: work whose result a test or schema can
mechanically check may run on a cheap model, because a wrong answer gets
caught regardless. Work whose subtle failure would go unnoticed — spec
amendments, architectural decisions, judging whether a conversation has
settled into a concrete proposal, judging whether a run is actually done —
goes to the strongest available model regardless of how mechanically small
the task looks.

Default policy: orchestration and spec amendment on the strongest available
model; planning on Opus-class; implementation on Sonnet-class; mechanical
tasks and routing on Haiku-class. Policies are themselves a product lever —
a cheaper tier and an enterprise tier differ by policy, not by code path.

## Consequences
- A stage handler asks "which model for this task" through policy lookup,
  never a literal model name — see `agent_policies` in the data model and
  its consumption in `DarkFactory.Engine`'s stage handlers once they call
  real models (this slice's handlers are still deterministic stubs; the
  policy lookup point is a step-3c+ concern).
- Changing a default (e.g. moving planning to a different model class) is a
  configuration change, not a redeploy of stage-handler code.
- The verifiability criterion is a concrete design rule stage-handler
  authors can apply directly: "can something downstream mechanically catch
  a wrong answer here?" — if not, don't default that task to a cheap model
  regardless of how small it looks.
