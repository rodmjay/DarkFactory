# ADR-0031: Local-first sync with PowerSync — reads sync down to OPFS/SQLite, writes are `df.*` commands

## Status
Accepted — **scheduled with step 4** (web UI). Recorded now so the schema
and the command surface are built with it in mind rather than retrofitted.

## Context
The web UI is the primary interface, and a factory UI is mostly *reading*:
spec graphs, run timelines, batch status, conversations. Doing that over
request/response means a spinner on every navigation and polling for
liveness — and the `df-hook` CLI ([ADR-0030](0030-claude-code-hooks-as-factory-commands.md))
has a harder version of the same problem, because a hook that blocks on a
network round trip is a hook that makes someone's editor feel slow.

## Decision
The web UI and the `df-hook` CLI each hold a **local SQLite mirror kept
current by PowerSync** — OPFS VFS in the browser, the Node SDK in the CLI.

**Sync rules bucket by `org_id` and `project_id` taken from the JWT**, so
tenancy ([ADR-0010](0010-single-postgres-schema-with-org-id.md),
[ADR-0011](0011-project-isolation.md)) is enforced in the sync layer rather
than trusted to client queries.

**Synced:** projects, spec_nodes, spec_revisions, spec_edges,
spec_snapshots, snapshot_members, snapshot_edges, amendments, approvals,
conversations, turns, runs, stages, events, batches, batch_items, teams,
team_members, skills, skill_revisions, and servers (non-secret columns).

**Never synced:** artifact bodies, standards_index, audit_log, provenance
secrets, effective_config secrets.

**All client writes go through the PowerSync backend connector to a `df.*`
command.** There are no raw table writes from clients — which keeps every
invariant this system has (append-only revisions, approval gates, budget
checks) in one place instead of duplicated into a client that could skip
them.

Append-only tables mean **no conflict resolution**; write checkpoints
reconcile optimistic local rows.

## Consequences
- `migrate` owns `wal_level=logical`, the publication, and the PowerSync
  sync-rules file — the sync configuration is schema, and versioning it
  anywhere else would let it drift from the tables it describes.
- The PowerSync Open Edition service joins Compose and the self-hosted
  image ([ADR-0025](0025-identity-tenancy-deployment-modes.md)).
- Live dashboard updates come from the synced `events` table, leaving
  SignalR only for streaming agent turns during `df.work.attach`
  ([ADR-0015](0015-observable-steerable-runs.md)) — and removable entirely
  if turn streaming later moves to a table.
- The `df-hook` local cache is the synced SQLite, which is what lets a hook
  answer from local state and post asynchronously.
- Client schema is declared per table in the web app, so **adding a synced
  table is a two-schema change**. That is real friction and worth stating
  plainly: it is the cost of the client knowing its own shape.
