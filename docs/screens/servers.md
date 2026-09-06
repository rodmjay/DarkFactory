# Screen: Servers and connectors
Route: /servers
Reads (local SQLite): servers (non-secret columns), capabilities, effective_config
Dispatches: df.servers.authorize, df.servers.recheck
Renders:
  server                 → ServerCard (states: conformant, degraded, unreachable, built-in not authorized)
  domain group           → section per df.* domain, including groups with no server
States covered: connected roster, project connected to nothing, degraded with a named failing capability, unreachable, connector awaiting authorization, empty capability domain
Proposed additions: none — `ServerCard`'s not-authorized rendering was corrected in packages/ui and the manifest in this commit.

## Notes

Grouped by the `df.*` domain each server serves rather than by tier,
because the question is always "what can the factory do here". A group with
no server is the most informative row on the screen: it says which
capability the factory is currently doing without.

A built-in connector that has not been authorized now shows `not
authorized` rather than its health. It previously rendered `conformant`,
which asserts a conformance check that never ran — the one claim this card
must not make.

The empty state says what still works, because that is the surprising half:
the spec graph, conversations, amendments and approvals all run on the
factory's own database. Only runs need somewhere to read and write code.

`effective_config` is rendered on the card, which is why the schema forbids
secrets in it.
