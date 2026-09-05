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
