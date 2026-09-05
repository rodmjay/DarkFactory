# ADR-0029: Approved amendments execute in ordered batches; deploy is a batch-level action

## Status
Accepted — refines [ADR-0003](0003-pipeline-skeleton.md) (as amended) on
what seeds a run, and makes `ship` a batch action rather than a run action

## Context
[ADR-0017](0017-conversation-is-the-product.md) makes the approved amendment
the unit of intent, and the amended [ADR-0003](0003-pipeline-skeleton.md)
makes it the thing that seeds a run. A single conversation routinely settles
into several amendments at once, and those amendments are rarely
independent: a new rule may depend on a node another amendment creates in
the same sitting.

Running them as unordered concurrent runs is wrong twice over. Each run
would build against a snapshot taken before its siblings landed, so a later
amendment could not see an earlier one's specs; and each run would
independently reach `ship`, producing one deploy per amendment. That makes
release notes meaningless and rollback nearly impossible — a user who wants
to undo "the thing we did this afternoon" would have to identify and revert
five separate deploys, in the right order.

## Decision
Approved amendments enter a **project backlog**. A **batch** is an ordered
set of amendments drawn from that backlog.

**Ordering.** The factory proposes the order by traversing spec-graph edges
([ADR-0016](0016-spec-graph-content-addressed-append-only.md)) —
`depends_on` first — and the user may reorder before executing. The proposal
is a suggestion, not a constraint; the user's order wins.

**Execution.** A batch executes as a *sequence* of runs, not a fan-out. Each
run builds against the snapshot produced by the previous run in the batch,
so later items genuinely build on earlier ones. The first run builds against
a snapshot taken when the batch starts.

**Gates.** `verify` must pass for every run in the batch before the batch is
deployable. **Deploy is invoked once per batch, not once per run**, and the
batch is therefore the unit of release notes and rollback.

**Data model:** `batches` (with `status` and `deployed_at`) and
`batch_items(batch_id, amendment_id, seq, run_id?)`.

**Front surface:** `df.batches.create`, `df.batches.order`,
`df.batches.execute`, `df.batches.status`, `df.batches.deploy`.

## Consequences
- `ship` stops being the terminal stage of an individual run and becomes a
  batch-level action. A run that verifies clean is *deployable*, not
  *deployed*. The `ship` stage in
  [PipelineStages](../../src/DarkFactory.Core/PipelineStages.cs) stays where
  it is for now; what changes is who invokes it and how often.
- Sequential execution costs wall-clock time versus fanning out. That is the
  intended trade: correctness of the "later builds on earlier" guarantee is
  worth more than parallelism at this scale, and a batch is typically a
  handful of items, not hundreds.
- A failed run mid-batch halts the batch. The already-verified runs before
  it are not deployed, because deploy is batch-level — so a partial batch
  never reaches production. Whether the user may then re-cut a shorter batch
  from the successful prefix is a UI question, not a schema one; the schema
  allows it.
- Rollback has a real unit for the first time. `batches.deployed_at` plus
  the batch's ordered runs is enough to describe and reverse a release.
- These tables (`batches`, `batch_items`) are **not** step 3a work. 3a
  creates the spec graph only; batches land with `df.work.create` and the
  execution path.
