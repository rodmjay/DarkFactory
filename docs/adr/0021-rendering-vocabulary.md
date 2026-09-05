# ADR-0021: Rich responses bind to a factory-owned rendering vocabulary

## Status
Accepted

## Context
Once the primary interface is a conversation and a dashboard
([ADR-0017](0017-conversation-is-the-product.md), step 4), responses need
to carry more than prose: a spec diff, a dependency graph, an approval
button. If every plugin invented its own shape for that, the web UI would
need bespoke rendering per plugin, and the MCP front surface would have no
consistent structured content to return either.

## Decision
Conversational and run responses are typed payloads, not prose alone. The
factory owns a versioned vocabulary of renderable component types —
`spec_diff`, `dependency_graph`, `stage_timeline`, `approval_card`, `table`,
`code_diff`, `form`, `metric`, `markdown` — each backed by a JSON Schema
under `contracts/schemas/`. Plugins and stage agents emit payloads that
bind to these existing types; they never ship their own UI. If a plugin
needs to represent something the vocabulary doesn't cover, it either maps
the concept onto an existing component or proposes a new component type
through the convention process ([ADR-0020](0020-convention-versioning.md))
— it doesn't unilaterally invent a rendering.

The web UI (step 4) renders the vocabulary directly. The MCP front surface
returns the identical payloads as structured tool-call content, so a host
without a web UI still gets the same information, just not the same pixels.

## Consequences
- The dashboard never needs plugin-specific rendering code — it has a fixed
  set of component renderers to build once, and every new plugin capability
  either fits an existing one or triggers a deliberate vocabulary addition.
- A payload is meaningful outside the web UI too: any MCP host can inspect
  a `spec_diff` payload's structured fields instead of parsing prose.
- Growing the vocabulary is a versioned, deliberate act (new JSON Schema,
  new component contract) — not an emergent pile of ad hoc shapes different
  plugins happened to invent.
