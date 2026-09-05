# ADR-0018: `df` namespace and `df.describe` handshake

## Status
Accepted

## Context
v1's conventions used bare tool names (`workspace.describe`, `files.list`,
`exec.run`). That's fine when the factory is the only caller, but it
collides with a server's own tools the moment that server is meant to be
useful standalone too — and there was no single, mandatory way for the
factory to ask "what are you, and what can you do" before trusting a
server with anything.

## Decision
Every convention tool lives under the dotted root `df.` —
`df.files.read_many`, `df.vcs.open_pr`, `df.specs.query`. Anything a server
exposes outside `df.` is its own business; a server can be `df`-compliant
and still be a perfectly good standalone tool for its own users.

`df.describe()` is mandatory for every compliant server and returns:

- `convention_version` — which version of the convention this server speaks
  (see [ADR-0020](0020-convention-versioning.md)).
- `capabilities[]` — the `df.*` tools it actually implements.
- `domain` — one or more of `vcs`, `deploy`, `standards`, `qa`, `knowledge`,
  `observability`, `ticketing`, …
- `effective_config` — what this instance is actually pointed at (repo,
  branch, environment, subscription) so the factory isn't guessing.
- `requires[]` — capabilities this server needs from elsewhere, so the
  factory can check the graph of connected servers is actually complete.

The factory calls `df.describe()` first and refuses to register a server
that cannot answer it. The registry (`servers` table) holds a **static
manifest** the author published; registration compares that manifest
against the **live describe** response and flags any disagreement rather
than silently trusting either one. Registration also runs a **conformance
check**: each capability the manifest claims is exercised once against a
scratch project before the server is trusted with a real one.

## Consequences
- A server author gets a single, mandatory extension point to implement
  before anything else works, and a single place to bump when they add
  capabilities.
- Manifest/live-describe disagreement is a signal worth surfacing (a stale
  listing, a misconfigured deployment) rather than a silent trust gap.
- The namespace convention means the reference workspace server's tools
  (`docs/conventions/workspace.md`) get renamed under `df.` as part of
  implementing this ADR (step 3b) — a mechanical but real breaking change
  from v1's bare names.
