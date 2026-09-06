# ADR-0035: Approval is hierarchical along `depends_on`; rejection blocks dependents rather than rejecting them

## Status
Accepted. Amends the approval flow in
[ADR-0017](0017-conversation-is-the-product.md) and constrains batch
composition in
[ADR-0029](0029-ordered-batches-and-batch-level-deploy.md).

## Context
[ADR-0017](0017-conversation-is-the-product.md) gates amendments: only an
approved amendment can seed a run. It treats approval as a property of the
amendment — one decision, taken whole.

That works while an amendment is one idea. It stops working as soon as the
spec graph has depth, because
[ADR-0016](0016-spec-graph-content-addressed-append-only.md) makes
`depends_on` a real edge and the small-grain recommendation in the brief
means a single feature request produces many nodes with real dependencies
between them. Two failures follow:

- **Approving in the wrong order approves something meaningless.** A node
  whose parent is still proposed has been approved against a premise nobody
  has agreed to. It is not wrong yet; it is unfounded, and nothing in the
  system says so.
- **Rejecting one node silently invalidates others.** The dependents are
  still marked approved, still eligible for a batch, and still describe
  behaviour that rests on a rule that was just refused. Rejecting them too
  is worse: they were never objected to, and a revised parent should bring
  them back without anyone re-arguing them.

## Decision

**A spec node cannot be approved while any node it transitively depends on
is unapproved or rejected.** Approval proceeds in dependency order.

**Rejection blocks; it does not reject.** Rejecting a node sets every
transitive dependent to `blocked_by` — still *proposed*, not *rejected*. A
later approval of a revised parent unblocks them. The distinction is the
whole point: *rejected* is a judgment about that node, and *blocked_by* is a
statement that the question cannot be answered yet.

**Blast radius is shown before the destructive action, not after.** Before a
reject or a retire, the approval surface reports the count of nodes that
would be blocked and the amendments affected. Retiring an approved node is
subject to the same dependent check as rejecting one.

**Blocked nodes cannot enter a batch.** The batch order proposer in
[ADR-0029](0029-ordered-batches-and-batch-level-deploy.md) already respects
`depends_on`, so this adds an eligibility filter rather than new ordering
logic.

**Approval state belongs to the node, not the amendment.** An amendment is
approved when all of its nodes are.

**Data model:** `spec_nodes.approval_state ∈ {proposed, approved, rejected,
blocked_by, retired}`, plus `blocked_by_spec_id`.

**Front surface:** `df.specs.approve` and `df.specs.reject` gain
`cascade_preview: true`, which returns the blast radius without acting.

**UI:** `ApprovalCard` shows dependency order and blast radius; `SpecDiff`
marks blocked elements.

## Consequences

- Moving approval state onto the node is the substantive change to
  [ADR-0017](0017-conversation-is-the-product.md). An amendment is a
  proposal grouping, not the unit of decision — which is what makes partial
  approval of a large amendment expressible at all.
- `blocked_by` is a derived state that must be recomputed whenever a parent's
  state changes, in both directions. The single-parent `blocked_by_spec_id`
  records *a* cause; a node blocked by several rejections is unblocked only
  when the last of them clears, so the field is a pointer for explaining the
  block, not the authority on whether it still holds.
- Append-only ([ADR-0016](0016-spec-graph-content-addressed-append-only.md))
  covers revisions, not this. Approval state is mutable per node and needs
  its own transition history to stay auditable — otherwise "who unblocked
  this, and when" has no answer.
- `cascade_preview` makes the preview and the action two calls over a graph
  that can change between them. The preview is advisory; the check at the
  moment of action is the one that decides.
- Deep dependency chains make approval a sequence rather than a single
  decision, which is more work for the approver. That is the cost of the
  small-grain recommendation, and it is why dependency order is shown rather
  than left to be inferred.
- A cycle in `depends_on` would make approval unreachable for every node in
  it. Nothing currently prevents one being proposed.

## Open
Cycle detection on `depends_on` — whether it is rejected at propose time or
surfaced as a diagnostic on the approval surface — is not settled here.
