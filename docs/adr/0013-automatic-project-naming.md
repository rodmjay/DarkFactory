# ADR-0013: Automatic, collision-safe project naming

## Status
Accepted

## Context
`projects.register` connects to a workspace server and should generally
"just work" without asking the caller to invent a unique project name up
front, while still letting a caller pin a specific name (e.g. to match an
existing dashboard bookmark or CI config).

## Decision
When `projects.register(workspace_mcp_url, name?)` is called without an
explicit `name`, the factory derives one from the workspace's directory or
repo name (as reported by that server's `workspace.describe()`), slugified
(lowercase, non-alphanumeric runs collapsed to `-`, trimmed). If the
slugified name collides with an existing project, the factory appends
`-2`, `-3`, etc. until it finds a free name. Callers may always override
this by passing an explicit `name`, which is used as-is (still subject to
the same collision suffixing).

## Consequences
- Registering a repo has zero required naming decisions in the common case
  (`projects.register("http://host.docker.internal:5200/mcp")` just works),
  matching the "one round trip" feel the rest of the surface aims for.
- Project names are stable identifiers once assigned; re-registering the
  same workspace URL does not rename an existing project (registration is
  idempotent per workspace URL within a project's lifetime — re-running it
  against an already-registered workspace returns the existing project
  rather than minting `-2`).
- The slugification and collision rule live in one place
  (`DarkFactory.Core`) so `projects.register` and any future bulk-import
  path share identical naming behavior.
