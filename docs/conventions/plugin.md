# Plugin Convention — v0.1 (draft)

Status: **Draft. Mostly TODO.** No real plugin server exists in this slice;
`plugins.list` returns a stubbed, hardcoded response and plugin
installation returns `not_implemented`. This document sketches the intended
shape.

A Plugin server attaches stages and/or capabilities to named hook points in
the pipeline (see [ADR-0003](../adr/0003-pipeline-skeleton.md):
`before:<stage>` / `after:<stage>` for each of `intake`, `spec`, `plan`,
`implement`, `verify`, `ship`). Like Workspace and Theme servers, a plugin
is an MCP server only — it never calls another spoke server directly (see
[ADR-0001](../adr/0001-hub-and-spoke-topology.md)); anything it needs from
the workspace or theme comes back through the factory.

## Intended shape

- `plugin.describe()` → declares which hook points this plugin attaches to,
  and its `plugin_id` (used for state scoping — see
  [ADR-0011](../adr/0011-project-isolation.md)).
- `plugin.invoke(hook_point, context)` → runs the plugin's logic for that
  hook point, returns a [hook result](envelope.md) (`ok`, or a
  `failure_class`).
- State access: a plugin never touches its own storage directly. It calls
  back into a core-provided `state.get(key)` / `state.set(key, value)` /
  `state.list(prefix)` surface, scoped by the factory to
  `(project_id, plugin_id, key)`.

## TODO before v1.0

- [ ] Decide the exact `plugin.invoke` context shape per hook point (what a
      `before:implement` plugin can see vs. an `after:verify` plugin).
- [ ] Decide whether a plugin can request a gate (extending
      [ADR-0003](../adr/0003-pipeline-skeleton.md)'s two hardcoded v1
      gates) or whether gates stay engine-only through v1.
- [ ] Design the plugin registry and install flow (explicitly out of scope
      for this slice — `plugins.list`'s installation path returns
      `not_implemented`).
- [ ] Decide capability declarations beyond hook attachment (e.g. can a
      plugin add a brand-new stage, or only hook the fixed six?).

## v1 behavior

`plugins.list(project_id)` returns the resolved default theme (see
[theme.md](theme.md)) plus an empty plugin list, read directly from
`DarkFactory.Core`'s hardcoded defaults — no MCP connection to any real
plugin server happens in this slice.
