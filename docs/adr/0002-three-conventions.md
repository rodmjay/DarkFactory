# ADR-0002: Three server conventions — Workspace, Theme, Plugin

## Status
Accepted

## Context
Third parties need to extend Dark Factory without forking it. We need a small
number of well-defined "convention" contracts that external MCP servers can
implement, so the factory can talk to any conforming server the same way.

## Decision
There are exactly three conventions, each independently versioned and
published as a document under `docs/conventions/`:

1. **Workspace** — files, exec, vcs, deploy. One per project. Gives the
   factory hands and eyes on the customer's repo. See
   [`docs/conventions/workspace.md`](../conventions/workspace.md).
2. **Theme** — standards and stack opinions, exposed as MCP *resources*
   (coding standards, test commands, architectural conventions) rather than
   tools, since a theme is consulted, not commanded. See
   [`docs/conventions/theme.md`](../conventions/theme.md).
3. **Plugin** — stages and capabilities attached to named hook points in the
   pipeline. A plugin can add pre/post behavior around any stage without
   modifying the core engine. See [`docs/conventions/plugin.md`](../conventions/plugin.md).

Core (the factory itself) is private and unversioned as a "convention" — only
the three spoke-facing contracts above are published for others to implement.

## Consequences
- Anyone can write a workspace/theme/plugin server in any language, as long
  as it speaks MCP and implements the convention's tool/resource surface.
- Convention documents evolve independently and carry their own version
  numbers (currently all v0.1); the factory can support multiple convention
  versions concurrently by content-negotiating on `workspace.describe()` /
  equivalent capability calls.
- New extension points must fit into one of these three shapes. If something
  doesn't fit, that's a signal to reconsider the abstraction rather than add
  a fourth convention ad hoc.
