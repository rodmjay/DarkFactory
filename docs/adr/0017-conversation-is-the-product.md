# ADR-0017: The conversation is the product; the spec phase is a diff against the graph

## Status
Accepted

## Context
v1 treated intake and spec as the first two stages of a build run: submit
text, get a spec, approve it, move on. That models a one-off job, not an
ongoing relationship with a product's architecture. Real usage is a
standing conversation that occasionally settles into a concrete change.

## Decision
A `conversation` belongs to a project and persists across sessions. Each
user turn is answered by an architect agent that has retrieved the
relevant neighborhood of the spec graph ([ADR-0016](0016-spec-graph-content-addressed-append-only.md))
and the project's indexed standards ([ADR-0023](0023-index-standards-not-live-reads.md)) —
the agent is answering *from this project's actual specs*, not from
scratch each time.

When a conversation settles — the agent judges there's a concrete change to
propose — the spec phase produces a **proposed amendment**: a set of node
creations, node revisions, and edge changes, rendered as a diff a human can
read at a glance ("creates 1, amends 2, conflicts with the rule decided in
March"). Amendments are gated; who must approve one is configurable per
project (no gate, one approver, several). Only an approved amendment can
seed a run ([ADR-0003](0003-pipeline-skeleton.md), as amended). Conversations
and turns are persisted and queryable — they're part of the audit trail
alongside runs, not ephemeral chat history.

## Consequences
- There is no more "intake" or "spec" pipeline stage. A run starts at
  `plan`, seeded from one or more approved amendments and the snapshot they
  produced.
- The same conversation can span many amendments over the life of a
  project; nothing about a conversation is tied to a single run.
- Because amendments are diffs against a graph the agent already retrieved,
  "this contradicts what we decided in March" is something the agent can
  actually say, backed by a real edge (`conflicts_with`) to a real prior
  revision — not a hopeful guess from context window recall.
- Per-project configurable approval means a solo developer's project can
  have zero friction (auto-approve) while a team's project can require
  sign-off, without different code paths — just different `approvals`
  configuration.
