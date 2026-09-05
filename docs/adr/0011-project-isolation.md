# ADR-0011: Projects are isolated by `project_id` everywhere

## Status
Accepted

## Context
Dark Factory is explicitly multi-*project* from v1 (decision 13/registration
flow), even though it is single-org (ADR-0010). A plugin or stage agent
working on one project must never see or mutate state belonging to another
project, and this must be true structurally, not by convention.

## Decision
`project_id` is present on every table, every hook call (as part of the
call envelope, ADR-0005), and every state-store key. Plugin state is never
addressed directly by a plugin — it goes through a core-provided
`state.get(key) / state.set(key, value) / state.list(prefix)` API that the
factory scopes to `(project_id, plugin_id, key)` before touching storage.
A plugin cannot construct a key that reaches outside its own
`(project_id, plugin_id)` namespace.

## Consequences
- Plugin authors get a simple key-value API and cannot accidentally (or
  deliberately) read another project's or another plugin's state, because
  the scoping happens in core, not in the plugin's own code.
- Every EF Core query in `DarkFactory.Data` that touches a tenant-scoped
  table takes `project_id` as a required parameter, not an optional filter.
- The dashboard's per-project views (Projects list, Project detail) are a
  direct reflection of this boundary, not a separate access-control layer
  bolted on afterward.
