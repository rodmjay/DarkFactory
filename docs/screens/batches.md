# Screen: Batches
Route: /batches
Reads (local SQLite): batches, batch_items, runs, stages, events, amendments (approved backlog)
Dispatches: df.batches.compose, df.batches.reorder, df.batches.deploy, df.runs.steer, df.runs.answer
Renders:
  backlog amendment      → draggable row, LayerBadge, blocked-by note
  batch                  → header with status, spend against budget, deploy control
  batch_item             → StatusChip + summary + run attachment
  run                    → StageTimeline (plan, implement, verify, ship; attempts; over-budget)
  events                 → live activity list
States covered: running, blocked by verify, deployable, deployed, parked item awaiting an answer, over-budget attempt followed by a retry
Proposed additions: none

## Notes

Deploy is a batch action and never a run action (ADR-0029), so the only
deploy control on this screen sits on a batch header. A disabled deploy
says *why* it is disabled — "Run 2 has not passed verify" — because a batch
that cannot ship and will not say why is the thing people file bugs about.

A batch status is not a stage status: `deployable` has no run analogue. It
gets its own small mapping rather than being forced through `StatusChip`.

Steering a run is not editing its plan. It is recorded as an event with the
actor's name on it, and lands in the next agent turn.
