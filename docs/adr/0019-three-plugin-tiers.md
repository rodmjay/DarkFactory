# ADR-0019: Three plugin tiers; internal components are not forced through MCP

## Status
Accepted

## Context
Not every server in the ecosystem is the same kind of thing. A GitHub
connector the factory itself operates on a customer's behalf is a different
trust and business relationship than a community-published linter
standards server. Conflating them into one undifferentiated "plugin"
concept would force either unsafe trust of community code or unnecessary
friction for our own first-party connectors.

## Decision
Three tiers:

1. **Built-in connectors** (GitHub, Azure, Vercel) ship with the product,
   are authorized via OAuth in the web UI, and hold customer credentials in
   the factory's own secret store. They implement `df.vcs.*` / `df.deploy.*`
   internally — but a customer may substitute their own server for any of
   them, because a built-in connector is *just* a `df`-compliant server the
   factory happens to operate. They are also the quality floor: if a
   built-in connector ever needs to cheat around the published convention
   to do its job, that's a signal the convention itself is incomplete, not
   a reason to special-case the built-in.
2. **Premium first-party servers** (per-layer standards, QA) are
   subscription line items we author and support.
3. **Community servers** are free, publish with no review queue beyond the
   [ADR-0018](0018-df-namespace-and-describe-handshake.md) conformance
   check, and are listed by their manifest.

Our own orchestrator, deployer, and QA agent are in-process components of
the factory. They expose `df.*` surfaces at the points a third party might
want to replace them, but they are not required to call *themselves* over
MCP — there's no rule that internal-to-internal calls must round-trip
through the protocol just for consistency.

## Consequences
- "Can a customer replace this with their own server" is answered the same
  way for every tier: yes, because even a built-in connector is only ever
  a `df`-compliant server underneath.
- The registry (`servers` table, [ADR-0018](0018-df-namespace-and-describe-handshake.md))
  carries a `tier` column from day one, even though billing/entitlement
  logic for premium and marketplace tiers is out of scope for this slice.
- Internal components stay simple and fast (plain method calls, not MCP
  round trips) without that simplicity leaking into what customers can
  swap out.
