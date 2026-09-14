# Screen: Amendments
Route: /amendments
Reads (local SQLite): amendments, approvals, sync
Dispatches: df.amendments.approve, df.amendments.reject
Renders:
  amendment (queue row)  → LayerBadge, SpecId, conflict count, change summary (+n ~n −n)
  amendment.payload      → SpecDiff (creates, revises, retires, edges, conflicts)
  amendment.approval     → ApprovalCard (states: awaiting, approved, rejected)
States covered: queue, nothing awaiting, awaiting-you vs awaiting-others, approved, rejected with reason
Proposed additions: none

## Notes

The detail pane leads with the conflict rather than the diff. A change that
contradicts a decision someone made in March is a different decision from
one that does not, and burying that under a list of edits is how it gets
approved by accident.

Rejection requires a reason and approval does not. The diff already says
what was agreed to; a rejection that says only "no" sends the proposer back
to guess.

"Nothing is waiting on you" means nothing *proposed*. The approved and
rejected amendments are still facts about the project, and the backlog
count the empty state quotes is computed from them rather than written into
the copy.
