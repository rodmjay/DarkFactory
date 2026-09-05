# ADR-0020: Convention versioning and migration

## Status
Accepted

## Context
The `df` convention ([ADR-0018](0018-df-namespace-and-describe-handshake.md))
will change over time — new capabilities, renamed fields, new domains. A
growing set of independently-authored servers (built-in, premium,
community — [ADR-0019](0019-three-plugin-tiers.md)) cannot all upgrade in
lockstep with the factory.

## Decision
The convention is a published, versioned contract. A new version ships with
a stated overlap window during which the previous version remains
supported. The factory supports at least two versions concurrently and
adapts its calls per server using the `convention_version` field from that
server's `df.describe()` response. Where a change between versions is
mechanical — a rename, an added optional field — the factory shims older
servers automatically rather than asking every author to upgrade
immediately.

A **conformance CLI**, `df-conform`, lets an author exercise their own
server against a target convention version locally, before publishing, and
reports exactly which capabilities are missing or non-conformant. The
factory tracks every connected server's version and can report adoption
back to authors (e.g. "12 of 40 registered servers are still on v1").

## Consequences
- A breaking convention change doesn't mean flag-day breakage for every
  connected server — authors get the overlap window, and the factory's
  shim layer buys time for mechanical differences.
- `df-conform` has to exist before the ecosystem is large enough that
  manual verification per server stops scaling — building it early is
  cheaper than retrofitting it once there are many non-conformant servers
  in the wild.
- The factory's own MCP client wrapper (`DarkFactory.Client`,
  [ADR-0001](0001-hub-and-spoke-topology.md)) needs a version-dispatch
  point once more than one convention version is live; that's a concrete
  design constraint on the client, not just documentation.
