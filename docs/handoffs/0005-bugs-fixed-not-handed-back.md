# 0005 — Four bugs fixed instead of handed back, and what recall actually needs

## Instruction (verbatim)

> so if you just found a bug can you fix it so i dont have to remember it?

and, quoting my own previous paragraph back:

> What's actually broken is recall. flock finds 4 standards. swarm finds 0. In this exact project those are the same thing — the architect's own output last turn said "flock", and the drones corpus has a spec called 0079-the-working-swarm.md. So a query using the spec's word misses every standard that governs it. That's the problem embeddings solve: matching on meaning, not spelling.

> can we fix this?

## Commit hash

`084f568` — branch `screens-step4`. This report is the commit that follows it.

## What changed

**The dashboard's command list was mostly fiction.** It presented itself as
the factory's write surface; of its twelve names, five were real tools. The
other seven — `df.amendments.approve`, `df.runs.steer`,
`df.conversations.send` and the rest — were invented while the screens were
built against fixtures. I had reported *one* of them to you as a note. It is
now split: `FACTORY_COMMANDS` holds the five real tools under their real
names and argument shapes (`df.specs.approve` takes `amendment_id`,
`df.work.steer` takes `message`); `PENDING_COMMANDS` holds actions no
factory tool performs yet, typed separately so nothing can mistake one for a
working call.

**`pnpm check:commands` keeps it that way.** It reads both sides' source and
fails if a factory command is not registered in `src/DarkFactory.Mcp/Tools`
— or if a pending command has since been built and was never promoted. It
runs in `pnpm check`.

**The persona card's install button dispatched `df.team.raise_budget`**,
copied from the button above it. Installing a persona is not raising a
budget. My bug.

**ADR-0036 contradicted ADR-0023**, which was accepted first. Amended, not
rewritten: the factory ingests a standards server through `list` and `get`
and retrieves from its own index; server-side `query` is a standalone
surface. `docs/conventions/standards.md` updated to match. No wire change,
no version bump.

**`dashboard/CLAUDE.md` and `dashboard/AGENTS.md` are gone**, with
`agentRules: false` in `next.config.ts`. My `git add -A` in `5e244b1`
committed them. The CLAUDE.md is one line — `@AGENTS.md` — so every Claude
Code session under `dashboard/` has been loading Next's boilerplate as
project instructions beside the curated root contract. Next writes them only
when it detects an agent running `next dev`, which is why deleting alone
would not have held: the next session recreates them.

## Verified how

| Command | Result |
|---|---|
| `pnpm check` | exit 0 — now includes `Commands: 5 factory commands all registered by the factory; 5 pending, none yet built.` |
| `pnpm --filter dashboard build` | exit 0 |
| `pnpm test:visual` | exit 0 — 14 passed |
| `check:commands` · planted: invented name in `FACTORY_COMMANDS` | exit 1 — names it |
| `check:commands` · planted: real tool left in `PENDING_COMMANDS` | exit 1 — says to promote it |
| `check:commands` · restored | exit 0; `store.tsx` byte-identical to pre-test |
| `next dev` · planted: `agentRules` removed | reached Ready, no crash, **files generated** |
| `next dev` · `agentRules: false` | reached Ready, no crash, **files not generated** |

Not run: `dotnet test` (no C# changed) and `./scripts/check-stack.sh` (not a
commit to `main`).

## Decisions I made that weren't specified

- **Pending commands are kept and typed, not deleted.** A button keeps its
  eventual name, and the type system says it is not a working call.
- **The guard checks both directions.** Catching invented names alone would
  leave a built tool wired to a local no-op forever, silently.
- **`df.team.hire` is a new *pending* name** for the install action. It is a
  placeholder for a tool that does not exist, and the list it sits in says so.
- **`agentRules: false` rather than committing Next's files.** Next's own
  note recommends committing them. The root `CLAUDE.md` is this repo's agent
  contract and is curated on purpose; if Next's note is ever wanted, it
  should arrive as a reviewed change.
- **ADR-0036 amended in place with a dated section**, the superseded
  paragraph marked rather than deleted, so the history reads straight.

## Things I was wrong about

- **My first `agentRules` test was invalid, and I nearly reported it as
  proof.** Both dev runs crashed on Turbopack's `OS file watch limit reached`
  — right where Next writes the files — so "not generated" meant nothing
  either way. Rerun in webpack polling mode, with the assertion that the
  server reached Ready uncrashed, it is the result in the table above.
- **I called pid 15206 "an orphaned `next-server` from this session".** It
  was the Docker dashboard's own server — `docker top` confirmed it. Killing
  it by pid would have taken your running dashboard down; the guard I had put
  in front of the kill (only if its cwd is this repo's `dashboard/`) refused,
  because a container process's cwd is not visible from the host. Host
  `pgrep` lists container processes; check `docker top` before killing a
  `next-server` by pid.
- **The command bug was seven of twelve, not the one I reported.**

## What I did not do and why

- **Recall — "can we fix this?" — is not built.** It is not a bug; it is
  ADR-0023's retrieval, accepted and never implemented. The plan is under
  *Next*, written down so nobody has to remember it.
- **The file-watch limit is not fixed.** On this machine `next dev` with
  Turbopack crashes with `OS file watch limit reached`: the limit is 65,536
  and VS Code, codex and the file indexer consume most of it. Raising it
  needs sudo, which I cannot and should not do for you:
  `sudo sysctl fs.inotify.max_user_watches=524288`, persisted in
  `/etc/sysctl.d/`. Without sudo:
  `WATCHPACK_POLLING=true pnpm --filter dashboard exec next dev --webpack`.
  The Docker dashboard on 13000 is unaffected.
- **`contracts/schemas/describe.schema.json` still carries the uncommitted
  `domains[]` change** from the workspace-profile work, parked when the
  focus moved to spec generation.
- **Five pending commands have no factory tool:** `df.batches.compose`,
  `df.batches.deploy`, `df.servers.authorize`, `df.team.raise_budget`,
  `df.team.hire`.
- **The edit to handoff 0002 belongs to another session** and is left alone.
  `Dark Factory.html` is still untracked.

## Next

**Recall, precisely.** Measured last turn: search is already fast — p50
about 2.7 ms end to end — so vectorizing would make it *slower*. The problem
is recall: `swarm` finds 0 standards, `flock` finds 4, and in this project
they mean the same thing. And the architect never uses that search at all:
its standards are hardcoded at `ConversationService.cs:300` and
`StageContextBuilder.cs:55` (`ArchitectPrompt.DefaultStandards`).

1. **Implement `ISpokeClient`.** The interface exists; nothing implements it,
   so the factory has no runtime MCP client. `McpServerProbe` is the pattern.
2. **Ingest.** On standards-server registration, `df.standards.list` then
   `get` into `standards_index`; `list`'s `updated` drives re-ingest.
3. **Triage with the `router` role.** It is already mapped to
   `AssignmentPoints.Triage` on `claude-haiku-4-5` and never invoked. Give it
   the indexed titles and summaries plus the turn, and take back the relevant
   ids. This is ADR-0023's cheap-model triage, and it is what fixes
   swarm/flock: a model knows those words mean the same thing. No embedding
   provider, no vector store. Cost: one Haiku call per turn.
4. **Replace both hardcoded `DefaultStandards`** with the triaged standards,
   and record the ingest time in the context pack.
5. **Verify** by re-running the swarm/flock comparison against the context
   pack the architect actually receives.
6. **Embeddings only when the corpus outgrows a triage prompt.** At about
   85 documents no vector database is needed. When it is, the store follows
   the SQL Server / Azure SQL decision, and the provider is Foundry —
   Anthropic has no embeddings API.

**Still open, from earlier:** whether the first real flow's Approve button
uses today's all-or-nothing `df.specs.approve`, or waits for ADR-0035's
dependency-ordered approval.
