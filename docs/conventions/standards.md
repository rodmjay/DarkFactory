# Standards Convention — v0.1

Status: **v0.1, defined here and implemented by
[`moonbeam-mcp`](https://github.com/rodmjay/moonbeam-mcp)** — the first
standards server, and the reason this document exists
([ADR-0036](../adr/0036-standards-server-convention.md)).

A Standards server holds the written rules a customer's code is supposed to
obey — naming, structure, review gates, house style — and lets the factory
retrieve the ones that bear on the work in hand. It is an MCP server; the
factory is its only client ([ADR-0001](../adr/0001-hub-and-spoke-topology.md)).
Every tool below accepts the [call envelope](envelope.md) and returns a
`failure_class` on failure.

**Why this is not the spec graph.** The spec graph is what *this project*
has decided; standards are what the *organisation* already decided, usually
before the project existed and often across many projects. The graph is
amended through conversation and approval; standards arrive from a server
the factory does not own and cannot edit. Retrieval, not authorship, is the
whole relationship — which is why every tool here is a read.

Design principle: **the factory indexes; the server serves documents.**
Per [ADR-0023](../adr/0023-index-standards-not-live-reads.md), the factory
pulls a standards server's content with `list` and `get`, builds its own
index, and retrieves from that — one retrieval per turn across every
connected source, instead of one live call per server. So `list` and `get`
are the calls a server must get right first. `query` is for everything that
does *not* have an index of its own: a person in Claude Code, a script, a
client that has not ingested. (Amended 2026-09-13; see ADR-0036.)

## `df.describe()`

The mandatory handshake — see [describe.md](describe.md). A standards
server answers with `domain: "standards"` and describes its corpus in
`effective_config`:

```json
{
  "name": "moonbeam-standards",
  "convention_version": "0.1.0",
  "domain": "standards",
  "capabilities": [
    "df.describe",
    "df.standards.list",
    "df.standards.get",
    "df.standards.query",
    "df.standards.index"
  ],
  "requires": [],
  "effective_config": {
    "corpus": "moonbeam",
    "root": "/home/user/dev/moonbeam",
    "layers": ["all", "godot", "gdscript", "mcp", "infrastructure"],
    "document_count": "47"
  }
}
```

`effective_config` is rendered in the dashboard, so it carries no secrets —
a subscription identifier is fine, the token that buys it is not.

## Retrieval

### `df.standards.query(query, layers?, limit?)`

The call generation actually makes.

```json
{
  "matches": [
    {
      "id": "gdscript",
      "title": "GDScript",
      "layers": ["godot"],
      "excerpt": "Static types on every function signature…",
      "score": 0.82
    }
  ],
  "index_age_seconds": 41
}
```

- `layers` filters to documents whose own scope intersects the caller's.
  Absent means every layer.
- `limit` defaults to 10. A server may return fewer; it must not return
  more.
- `score` is optional and comparable only within one response. A server
  with no ranking may omit it, and the order of `matches` is then its
  answer.
- **`index_age_seconds` is required**, and it is the point of the whole
  response. A server whose index is stale still answers, because the last
  good index beats no standards at all — but the factory has to be able to
  say "these rules are two days old" rather than presenting them as
  current. See *Staleness* below.

### `df.standards.get(id)`

One document in full: `{ id, title, text, layers, status, updated, review_by? }`.

`text` is the document as authored. The factory does not parse it — it goes
into a context pack for an agent to read — so a server must not strip its
own structure to make it machine-friendly.

### `df.standards.list(layers?)`

Every document's metadata and no bodies:
`{ standards: [{ id, title, layers, status, updated }] }`.

**This is the factory's ingest path.** It must be complete — every
document, not a page — and every entry must carry `updated`, because that
is how the factory tells which documents changed since it last ingested. A
server may make it slower than `query`; it may not make it partial.

## Indexing

### `df.standards.index()`

Rebuild whatever backs `query`, returning
`{ indexed: number, took_ms: number, at: string }`.

Called on registration and on demand. It is the one **write-shaped** tool
in this convention, and it writes only the server's own index — never the
corpus. A server that keeps no index still implements it and returns the
document count with `took_ms: 0`, because "nothing to do" and "not
implemented" must not look the same to the factory.

**This is the capability most likely to fail conformance**, and that is
working as intended: indexing is where a standards server does real work,
so it is where a server that has been mis-deployed says so. A server whose
`index` fails but whose `query` succeeds is `degraded`, not `unreachable` —
it stays in use and turns read the last good index.

## Staleness

Standards change without telling the factory. A server therefore reports
`index_age_seconds` on every `query`, and the factory decides what to do
with it rather than the server deciding for it:

- The dashboard shows the age next to the server on the Servers screen.
- A run records the age it retrieved at, in its provenance, so "why did it
  generate that" has an answer months later.

A server must **not** silently reindex during a `query` to avoid reporting
an age. A slow correct answer is a worse failure than an honest stale one:
the caller is a stage agent under a deadline.

## Versioning

This is v0.1, reported by `df.describe()` as `convention_version`.
Backwards-incompatible changes bump the minor version while v0.x. See
[ADR-0020](../adr/0020-convention-versioning.md).
