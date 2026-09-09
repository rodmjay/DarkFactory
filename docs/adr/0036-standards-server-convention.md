# ADR-0036: The `df.standards.*` convention, and staleness as a reported fact

## Status
Accepted

## Context
`standards` has been a legal `domain` in the describe handshake since
[ADR-0018](0018-df-namespace-and-describe-handshake.md), and the dashboard's
Servers screen has drawn standards servers — degraded ones included — since
step 4. But no tool surface was ever defined. `df.standards.query` and
`df.standards.index` existed as strings in a fixture and nowhere else.

That gap became load-bearing the moment a real corpus turned up. The
Moonbeam games workspace keeps 47 written standards with front matter, a
`standards-first` rule that says the standard is written *before* the
pattern it governs, and specs and architecture documents alongside. It is
exactly the thing the `standards` domain was reserved for, and there was no
contract to implement.

Defining it from a real corpus rather than in the abstract is the point:
the shape below is what one honest implementation needed, not what seemed
tidy.

## Decision

**Four tools, all reads except the index rebuild:** `df.standards.query`,
`df.standards.get`, `df.standards.list`, `df.standards.index`. The full
contract is [docs/conventions/standards.md](../conventions/standards.md).

**Retrieval is the primary call, not enumeration.** A stage agent never
wants the corpus; it wants the handful of rules that bear on the file it is
about to write. `query` is scoped by layer and ranked; `list` exists for
the dashboard and cache warming and is allowed to be the slow one.

**`index_age_seconds` is required on every `query` response.** This is the
part worth recording. A standards server's index goes stale silently —
somebody edits a rule in a repository the factory does not watch — and the
failure mode we care about is not a missing answer but a *confident wrong
one*. Returning the age on every retrieval, rather than exposing a separate
health call nobody remembers to make, means the factory cannot render
retrieved standards without also knowing how old they are. The run records
the age in its provenance, so "why did it generate that" still has an
answer months later.

**A server must not reindex during `query` to avoid reporting an age.** The
caller is a stage agent under a deadline. A slow correct answer is a worse
failure than an honest stale one.

**`index` is implemented even by a server that keeps no index**, returning
the document count and `took_ms: 0`. "Nothing to do" and "not implemented"
must not look the same to the factory — that distinction is the difference
between a server that is fine and one that was mis-deployed.

## Consequences

**A standards server can be `degraded` and still useful**, and the existing
health vocabulary already says so: `index` failing while `query` succeeds
is the canonical degraded case, and turns read the last good index. This is
the state the dashboard has been drawing against a fixture; it now has a
real server that can produce it.

**Standards are retrieved, never authored, through this surface.** There is
no `df.standards.write`. The spec graph is what this project decided and is
amended through conversation and approval; standards are what the
organisation already decided, and the factory does not own them. Anyone
wanting the factory to edit a corpus should register it as a *workspace*
server as well — which is a different domain, a different trust decision,
and a deliberate second step rather than a hidden capability of this one.

**Conformance does not probe these capabilities yet.** `ConformanceChecker`
knows how to exercise `df.files.write_many`, `df.files.list` and
`df.exec.run`; everything else is recorded `NotProbed`. A standards server
therefore registers with its four capabilities unprobed, which is honest
but weak. Probing `df.standards.index` and asserting a non-negative count
is the obvious next step and is deliberately not in this ADR's slice.
