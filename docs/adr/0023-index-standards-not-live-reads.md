# ADR-0023: Index standards; do not read servers live per turn

## Status
Accepted

## Context
A conversation turn ([ADR-0017](0017-conversation-is-the-product.md)) needs
the project's standards as context. Fetching every relevant standards
server live, on every turn, over MCP is slow, expensive, and unnecessary —
standards content changes rarely compared to how often it's read.

## Decision
At registration and again on change notification, the factory pulls each
standards/knowledge server's content once and builds its own index
(embeddings plus structure) into `standards_index`. A conversational turn
performs exactly one retrieval that spans both the spec graph
([ADR-0016](0016-spec-graph-content-addressed-append-only.md)) and every
indexed standards source, scoped two ways: **layer routing** (which
architectural layers this turn actually touches) and **spec neighborhood**
(graph traversal outward from the nodes already in play). A small, cheap
model does the triage pass — selecting layers and candidate nodes — before
the expensive model handling the actual turn ever sees assembled context.
Standards servers are still called live, but only for actions and for
current code state, never to re-answer "what are your standards" on every
turn. Token usage per turn is metered and visible.

This slice stubs the index: `df.conversations.turn` reads one hardcoded
default standards resource directly rather than performing embedding
retrieval, so the retrieval *shape* (one call, feeding one prompt) is real
even though the index behind it isn't built yet.

## Consequences
- Standards content living in `standards_index` means a turn's latency and
  cost don't scale with how many standards servers are connected — it
  scales with one retrieval call, regardless.
- A standards server needs a way to signal "my content changed, re-index
  me" — a webhook or poll, which is real work deferred alongside the
  embeddings themselves (see "still out of scope").
- The triage-then-answer pattern (cheap model picks scope, expensive model
  uses it) is a concrete, reusable shape other retrieval-heavy paths in the
  factory can adopt later, not something specific to conversations.
