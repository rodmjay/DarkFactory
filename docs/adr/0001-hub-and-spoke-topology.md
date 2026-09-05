# ADR-0001: Hub-and-spoke topology

## Status
Accepted

## Context
Dark Factory coordinates work across a workspace (the customer's code and repo),
a theme (standards/stack opinions), and plugins (extra stages/capabilities). We
need to decide how these parties talk to each other.

## Decision
The factory is the **only** MCP client in the system. Workspace, theme, and
plugin servers are MCP **servers only** — they never call each other directly,
and they never act as MCP clients. Every cross-server data flow is mediated by
the factory: if a plugin needs something from the workspace, it returns that
need to the factory (via a hook result or a follow-up tool call), and the
factory fetches it from the workspace server and relays it back.

```
        Claude Code / Cursor / CI (MCP clients, "hosts")
                       |
                       v
                 Dark Factory  <-- the only MCP client among servers
                 /     |      \
                v      v       v
          Workspace  Theme   Plugin(s)
           server   server   server(s)
```

## Consequences
- Workspace/theme/plugin servers are simple to build: implement one
  convention, expose tools/resources, never worry about discovering or
  calling peers.
- The factory becomes the single place where cross-cutting concerns live:
  envelope propagation, auth, audit logging, retry policy, project isolation.
- No peer-to-peer server communication means no need for service discovery
  between spokes, and no risk of a plugin server reaching into a workspace
  server the factory hasn't authorized it to see.
- The factory is on the critical path for every interaction, so its
  availability and performance matter more than any single spoke's.
