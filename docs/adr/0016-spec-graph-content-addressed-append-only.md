# ADR-0016: The spec graph lives in the factory database, content-addressed and append-only

## Status
Accepted

## Context
Specs are now durable project state (see the amendment to
[ADR-0004](0004-typed-artifacts-by-reference.md) in the architecture
brief), not a per-run artifact. A durable spec needs the same properties
git gives source code — stable identity, an immutable history, diffable
points in time — without asking every customer to run a second version
control system just for specifications.

## Decision
The spec graph is "the git model without git," built from a small number
of append-only tables:

- **Nodes** (`spec_nodes`) have a stable identity — `spec_id`, a ULID —
  that never changes across the node's life.
- **Revisions** (`spec_revisions`) are immutable and content-addressed: the
  primary key is the SHA-256 hash of the revision's canonical content.
  Identical content always yields the identical hash; a revision is never
  updated in place, only superseded by a new revision row pointing at the
  same `spec_id`.
- **Edges** (`spec_edges`) are typed (`depends_on`, `conflicts_with`,
  `supersedes`, `implements`, `constrains`, plus kinds a plugin declares)
  and point at node identities, not specific revisions, so an edge survives
  its endpoints being revised.
- **Snapshots** (`spec_snapshots` + `snapshot_members`) are named,
  immutable sets of `(spec_id, revision_hash)` pairs — a snapshot pins
  exactly which revision of every node was current at that moment. Runs
  record the snapshot they were built against (extending
  [ADR-0004](0004-typed-artifacts-by-reference.md)); snapshots are
  diffable against each other.
- **Provenance** (`provenance`) is required on every amendment and revision:
  conversation id, turn id, proposer (human or agent), approver, timestamp.
  Provenance rows are never updated.

Nothing in this graph is ever updated or deleted in place. A node's
lifecycle end is recorded as `retired_at` transitioning once from null to a
timestamp — never any other field, and never on a revision row.

A **markdown export** (`df.specs.export`) materializes the graph into the
repo on demand as a read-only projection. The database, not the exported
file, remains the source of truth; the export is regenerated, never
hand-edited back into meaning.

## Consequences
- Content addressing makes "did this spec actually change" a hash
  comparison, not a diff — and makes accidental duplicate proposals
  self-deduplicating (proposing identical content twice yields the same
  revision row).
- Diffing two snapshots is a set comparison over `snapshot_members` (which
  nodes were created, revised, or retired between them) plus a
  timestamp-scoped comparison over `spec_edges` (which edges were active at
  each snapshot's creation time) — no bespoke diff algorithm against free
  text.
- Because revisions are genuinely immutable, the database can enforce it at
  the privilege level, not just in application code: the application's own
  database role has no `UPDATE`/`DELETE` grant on `spec_revisions` or
  `provenance` (see the migration in `src/DarkFactory.Data/Migrations/`).
  A bug that tries to "fix" a revision in place fails at the database, not
  silently.
- This is deliberately a handful of tables, not a generic version-control
  engine — the model only needs to support nodes, typed edges, revisions,
  and named snapshots, not merges, branches, or conflict resolution beyond
  what `conflicts_with` edges make visible to a human.

## Amendment: an amendment records who, when, and *why* — per change

The original decision said "every amendment carries provenance:
conversation id, turn id, proposer, approver, timestamp." That is who and
when. It omitted why, and the omission was inherited by the code: the wire
contract asked for a rationale on every element, the model supplied one,
and nothing carried it to storage in a form anything could read.

**The rationale is part of an amendment's provenance and is recorded per
element, not per amendment.** One approval routinely creates a node for one
reason and retires another for a different one; a single rationale on the
amendment would have to pick between them, which is how a record stops
being a record.

It is deliberately **not** part of the canonical text a revision is hashed
from. Two people can agree on a rule and disagree about the reason for it,
and that must not produce two revisions of the same rule.

Each revision therefore carries the reason it was made for, and
`df.specs.get` returns a node's revision history rather than only its
current text — because "why does this say what it says" is rarely answered
by the latest revision, and usually answered by the one that changed it.

### What was actually wrong, since the two halves failed differently

- **Creates and revises** were *buried*. The translator serialized the
  rationale into each element's `ContentJson`, so it did reach the database
  and did reach the revision — but no reader ever looked there, and the
  rendered amendment showed `null`.
- **Retires and both edge kinds** were *dropped*. Those internal records had
  no field for a rationale at all, so the value was discarded when the wire
  document was translated, before the amendment was ever written.

Both surfaced identically as `rationale: null` on an approval card, which
is the part worth remembering: **"nobody gave a reason" and "we lost the
reason" rendered the same**, so the loss was invisible from the outside for
as long as it existed. Migration `0009` recovers the first from the
amendment itself and the second from `turns.payloads`, and never overwrites
a rationale that is already stored — what a human approved outranks what a
migration can reconstruct.

## Consequences of this amendment
- `SpecDiff`'s elements each carry a `Rationale`, and it round-trips from
  the wire to the rendered amendment.
- `df.specs.get` returns `revisions[]`, each with its text, its reason, and
  the provenance of the approval that made it.
- A null rationale now means what it says: nobody gave one.
