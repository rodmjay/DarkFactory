# 0004 — `df.standards.*`, and the Moonbeam games workspace wired to the factory

## Instruction (verbatim)

> we ned to look at the moonbeam games its a sibling workspace, update the MCP so that it uses the pattern we define, and we want to wire up the games workspace to use dark factory

Scoped by two answers during the work: **one pilot game first, then expand**,
and **yes — also expose the standards corpus**.

## Commit hash

`7134aa2` (dark-factory) · `7414531` (moonbeam-mcp) · `43493a7`
(moonbeam-standards).

## What changed

**The pattern did not exist yet.** `standards` has been a legal `domain` in
the describe handshake since ADR-0018, and the Servers screen has drawn
standards servers — degraded ones included — since step 4. But no tool
surface was ever defined: `df.standards.query` and `df.standards.index` were
strings in a dashboard fixture and nowhere else. "Update the MCP to use the
pattern we define" turned out to require defining it.

So it is defined from a real corpus rather than in the abstract —
[docs/conventions/standards.md](../conventions/standards.md) and
[ADR-0036](../adr/0036-standards-server-convention.md). Four tools, all
reads but the index rebuild. Retrieval is primary and enumeration is not: a
stage agent never wants the corpus, it wants the handful of rules bearing on
the file it is about to write.

**`index_age_seconds` is required on every `query` response.** A standards
index goes stale silently — someone edits a rule in a repository the factory
does not watch — and the failure that matters is not a missing answer but a
*confident wrong one*. Requiring the age on every retrieval means the
factory cannot render standards without also knowing how old they are, and a
server may not reindex inside a query to make the number look better.

**Moonbeam speaks it, as a second surface.** `moonbeam-mcp/df/` is a new
package; the existing stdio server's 17 tools (`hello`, `get_standard`,
`scaffold_game`…) are untouched. The convention reserves the `df.` root
exactly so a server can answer to the factory and stay a good standalone
tool for a person in Claude Code. It is a separate package because the
existing one is deliberately dependency-free hand-rolled JSON-RPC while the
factory connects with the official MCP SDK over streamable HTTP — keeping
its own `package.json` leaves the stdio server's dependency count at zero.

**The pilot game is registered.** `drones` (20 files, TypeScript/Vite) is a
Dark Factory project, its workspace served by the factory's own reference
server pointed at that repo, with the Moonbeam corpus registered as a
`standards` server scoped to it.

## Verified how

| Command | Result |
|---|---|
| `pnpm check` | exit 0 — 76 files no raw colours; 162 token pairs pass contrast; export bundle current; lint and typecheck clean |
| `pnpm test:visual` | exit 0 — 14 passed |
| `dotnet test DarkFactory.sln` | exit 0 — 194 passed, 2 skipped |
| `./scripts/check-stack.sh` | exit 0 — six services healthy from an empty volume |
| `node --test df/test/describe.test.js` (moonbeam) | exit 0 — 6 passed |

**Live, against the running factory** — not asserted:

- `df.describe` validates against `contracts/schemas/describe.schema.json`.
- `df.servers.register` → `moonbeam-standards`, domain `standards`,
  convention `0.1.0`, **Conformant**, "manifest and live describe agree".
- `df.projects.register` → `drones`, stack hints `node, typescript` derived
  by the factory, five-member team seeded.
- The dashboard's Projects screen renders it live.

**The Moonbeam tests were shown to fail against planted breakage.** Adding a
single extra top-level field to `df.describe` — precisely what the closed
published schema rejects at registration — takes the suite from 6 passed to
1 failed. Restored, 6 passed.

## Decisions I made that weren't specified

- **A game registers per repo, never as the whole workspace.** A workspace
  server is defined as hands and eyes on exactly one working tree; the
  Moonbeam root is deliberately not a repo and holds eleven of them.
  Registering the root would have the factory seeing eleven histories as one
  tree, and commits, branches and PRs stop meaning anything.
- **The workspace surface was not copied into Moonbeam.** It is generic and
  the factory already ships a conformant implementation taking `--root`.
  Duplicating 589 lines to be able to say Moonbeam owns it would buy nothing
  and cost a second thing to keep in step with the convention. Only the
  *standards* surface — the part only Moonbeam can serve — was written there.
- **`applies_to` stays `applies_to`.** The convention calls the same idea a
  layer; the adapter translates. A corpus that renames its own vocabulary to
  suit a consumer has exactly one consumer.
- **Servers run as containers on the factory's network**, not on the host.
  `host.docker.internal` resolves here but this machine's firewall blocks
  container→host traffic on both gateway addresses, so `df.describe` timed
  out while the same call from the host took 41ms. Same shape as
  `workspace-demo`. The corpus mounts read-only — there is no
  `df.standards.write` to justify anything else.
- **A Moonbeam standard was written for the integration** before calling it
  done, because that workspace's `standards-first` rule says the standard
  precedes the pattern. It is their rule and it applies to work done in
  their repo.

## Things I was wrong about

- **My `git add -A` yesterday swallowed another session's work.** Commit
  `5e244b1` contains five files belonging to a parallel session, which
  discovered it while writing its own report — its note is the unstaged edit
  to handoff 0002 that this commit deliberately leaves alone. Nothing was
  lost, the content was byte-identical, but the attribution and ordering are
  wrong and it is exactly the failure a worktree per workstream prevents.
  Everything here was staged file by file.
- **I typed the factory's project shape as camelCase** when writing the
  dashboard client, matching whatever that build was serializing. The wire is
  snake_case throughout. When the factory returned to the documented shape
  the fields read `undefined`, so the roster still rendered and quietly lost
  its stack hints — a screen losing a column without erroring is the worst
  way to find out a serializer moved. It now accepts either spelling.

## What I did not do and why

- **Conformance does not probe `df.standards.*`.** `ConformanceChecker`
  exercises `df.files.write_many`, `df.files.list` and `df.exec.run`;
  everything else is recorded `NotProbed`. The Moonbeam server therefore
  registers Conformant with all five capabilities unprobed — honest, but
  weak. Probing `df.standards.index` and asserting a non-negative count is
  the obvious next step; recorded in ADR-0036 as deliberately out of slice.
- **Nothing consumes the standards yet.** No stage agent calls
  `df.standards.query`, so registering the corpus buys retrieval that is
  available rather than used. Wiring it into context packs is the point of
  having done this, and it is the next real piece of work.
- **Only `drones` is registered.** Ten more repos are eligible; the second
  one is the same three commands.
- **The other ten Moonbeam repos' in-progress work was left alone.** Both
  `moonbeam-mcp` and `moonbeam-standards` have substantial uncommitted
  changes that are not mine. I committed only the files I wrote.
- **`Dark Factory.html` is still untracked** in the dark-factory root, as in
  0002.

## Next

1. Probe `df.standards.index` in `ConformanceChecker`, so a standards server
   can actually fail conformance.
2. Put `df.standards.query` into the context pack a stage agent receives, and
   record `index_age_seconds` in run provenance.
3. Register the remaining Moonbeam game repos.
