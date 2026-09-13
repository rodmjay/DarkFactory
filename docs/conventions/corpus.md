# Corpus Convention — v0.1

Status: **v0.1, defined here and implemented first by `moonbeam-mcp`**
([ADR-0038](../adr/0038-corpus-servers-and-healing-connections.md)).

A Corpus server holds specifications a customer has **already written** —
prose documents that predate the factory — and serves them so the factory
can import them into its own spec graph through intake
([ADR-0037](../adr/0037-corpus-intake.md)). It is an MCP server; the factory
is its only client ([ADR-0001](../adr/0001-hub-and-spoke-topology.md)).
Every tool accepts the [call envelope](envelope.md) and returns a
`failure_class` on failure. Every tool is a read.

**Why this is not `standards` and not `knowledge`.** A standards server's
documents stay where they are and are *retrieved* for context, forever
(ADR-0036). A corpus server's documents are *imported*: read once, turned
into spec nodes the factory then owns, after which the originals are frozen
or retired and a change to one is **drift** to report, not an update to
follow. Retrieval and import are different relationships with different
failure modes, so they get different domains.

## `df.describe()`

The mandatory handshake — see [describe.md](describe.md). A corpus server
answers with `domain: "corpus"`:

```json
{
  "name": "moonbeam-specs",
  "convention_version": "0.1.0",
  "domain": "corpus",
  "capabilities": ["df.describe", "df.corpus.list", "df.corpus.get"],
  "requires": [],
  "effective_config": {
    "corpus": "moonbeam-specs",
    "root": "/home/user/dev/moonbeam/moonbeam-specs",
    "areas": "drones, smashhit, workspace",
    "document_count": "112"
  }
}
```

`effective_config` values are strings and carry no secrets.

## `df.corpus.list(area?, status?)`

Every document's metadata and no bodies:

```json
{
  "documents": [
    {
      "id": "drones/0079-the-working-swarm",
      "title": "The Working Swarm",
      "summary": "The drones you are not flying run the whole cycle themselves…",
      "area": "drones",
      "status": "draft",
      "updated": "2026-09-07",
      "sha256": "9f2c…",
      "superseded_by": null
    }
  ]
}
```

- **Complete, never paged.** It is the factory's import and drift path; a
  partial list is indistinguishable from documents having been deleted.
- `id` is stable across edits and unique in the corpus. It becomes part of
  the intake source ref (`<server name>:<id>`), so renaming a document is,
  to the factory, deleting one and adding another.
- **`sha256` is required**: the lowercase hex SHA-256 of the UTF-8 `text`
  that `get` returns for the same id. It is how the factory tells, without
  fetching every body, which documents changed since import.
- `status` is the corpus's own word. The factory gives meaning to exactly
  two values: `superseded` and `rejected` are **not imported by default**,
  because their replacement or their refusal is what is still true.
- `area` and `status` filter; absent means every document.

## `df.corpus.get(id)`

One document: `{ id, title, area, status, updated, sha256, text }`.

`text` is the document **as authored**, front matter included. The factory
does not parse it — an extraction agent reads it — so a server must not
strip or restructure it. `sha256` is computed over exactly this `text`; the
factory recomputes it and refuses a document whose hash does not match,
because an import made from a body other than the one listed cannot be
checked for drift later.

An unknown id is a `failure_class: "permanent"` error naming the id.

## Versioning

This is v0.1, reported as `convention_version`. Backwards-incompatible
changes bump the minor version while v0.x. See
[ADR-0020](../adr/0020-convention-versioning.md).
