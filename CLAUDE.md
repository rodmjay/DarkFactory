# Dark Factory

The contract every session starts from. Everything longer than a paragraph
lives elsewhere and is linked.

---

## The product

Dark Factory is a hosted, web-based service a company converses with to
maintain the specifications for its entire product stack, and which then
builds against those specifications with architectural standards applied at
generation time. The buyer is a CTO or lead already shipping agent-written
code who can feel it turning shapeless. What we sell is judgment — the
conversation, the specification graph, the orchestration, and the standards.
The hands (repos, deploys, infrastructure) are connectors and MCP servers.
The factory never becomes the only way to touch a project: a developer can
always bypass it, and drift is detected and reconciled, not prevented.

**North star: ship at agent speed without losing the architecture.**

## The four-step flow

1. **Converse.** You talk to an agent that knows your specifications and
   maintains them from your answers.
2. **Approve specs.** Settled conversation becomes a proposed amendment to
   the spec graph, reviewed and approved.
3. **Execute to test.** Approved specs are queued in ordered batches. Each
   batch runs — plan, implement, verify — until everything in it passes.
4. **Deploy.** A batch that has fully passed is deployed as a unit.

Everything else exists to make those four steps work.

---

## File map

| Where | What |
|---|---|
| [docs/architecture-brief.md](docs/architecture-brief.md) | **The source of truth.** Product, flow, ADR-0015→0032, data model, MCP surface, step definitions. Read it before proposing anything. |
| [docs/adr/](docs/adr/) | ADR-0001→0033, one file each. Decisions the brief amends are amended there too. |
| [docs/conventions/](docs/conventions/) | The `df` convention: describe, envelope, plugin, theme, workspace. |
| [contracts/schemas/](contracts/schemas/) | JSON Schema for the wire types: envelope, describe, spec, specdiff, plan, changeset, testreport, hookresult. |
| [docs/evidence/](docs/evidence/) | Run and visual evidence, by step. Referenced by path, never pasted. |
| [docs/handoffs/](docs/handoffs/) | Instruction in, report out. See *Handoff* below. |
| [docs/SESSIONS.md](docs/SESSIONS.md) | Who does what, and the rules with their costs. |
| [docs/TESTING.md](docs/TESTING.md) | The planted-breakage rule and the evidence for it. |
| [docs/SCREEN-CONTRACT.md](docs/SCREEN-CONTRACT.md) | The template every screen arrives as and is built from. |
| [packages/ui/DESIGN.md](packages/ui/DESIGN.md) | The design system: tokens, rules, components, enforcement. |
| `dashboard/app/design` | The showcase — the living spec. If a state is not on this page, it is not in the system. |
| `src/` · `tests/` | The .NET factory: engine, spec graph, MCP surface, gateway, migrations. |
| `reference/workspace-mcp` | The reference `df` workspace server. |

---

## Rules

**Tokens only — no raw colour, anywhere.** No `bg-gray-800`, no hex, no
inline colour, in `packages/ui` or `dashboard`. Enforced by the compiler:
`tokens.css` opens with `--color-*: initial`, which deletes Tailwind's
palette, so a raw colour class compiles to nothing and renders as nothing.
`pnpm check:tokens` is the second net, because invisible is not the same as
loud. Details in [DESIGN.md](packages/ui/DESIGN.md).

**No check counts until it has been shown to fail against planted
breakage.** Write down the breakage, break it, confirm the check goes red,
put it back. A check that has only ever passed has not been tested — the
code has. See [TESTING.md](docs/TESTING.md).

**`./scripts/check-stack.sh` must be green before any commit to `main`.** It
is the only gate that starts the whole stack from an empty volume, and it
has caught three failures the test suites were structurally unable to reach.

**No push without the pre-push hook.** `git config core.hooksPath
scripts/hooks`, once per clone and per worktree. It scans for credentials
and fails closed. A pushed key is only fixable by rotating it.

**Decisions go to ADRs, not chat.** An architectural choice the brief did
not cover gets its ADR in the same commit. An architect's ruling becomes an
ADR amendment.

**Never absorb work outside the instruction silently.** Adjacent work goes
under *What I did not do* in the handoff, with a one-line reason, and gets
assigned.

---

## Commands

| Command | What it covers |
|---|---|
| `docker compose up` | The whole stack locally — factory, worker, Postgres, workspace server. |
| `./scripts/check-stack.sh` | Every Compose service healthy from an empty volume, plus the web path a developer takes. **Required before committing to `main`.** |
| `./scripts/demo-3d.sh` | The 3d acceptance condition end to end against live models, writing evidence to `docs/evidence/3d/`. |
| `dotnet test DarkFactory.sln` | The .NET solution. |
| `pnpm check` | Tokens, contrast, export freshness, lint, typecheck. |
| `pnpm test:visual` | The showcase, per section and full page, both themes, against baselines. |
| `pnpm -r lint` · `pnpm -r typecheck` | The workspace packages individually. |

There is no `pnpm -r test`: no workspace package defines a `test` script.
The web tests are `pnpm test:visual`; the unit tests are `dotnet test`.

**Before merging anything:** `pnpm check`, `pnpm test:visual`, `dotnet test
DarkFactory.sln`, and `./scripts/check-stack.sh` from a clean checkout.

---

## Handoff

The reviewing architect sees only what is written here. The repository is
the interface; the chat is the notification.

Instructions are copied **verbatim** into the top of the next handoff file
before acting. Reports are written to `docs/handoffs/NNNN-<slug>.md` first,
then summarised in chat. One screen, fixed shape:

```
# NNNN — <title>

## Instruction (verbatim)
<what was asked, unedited>

## Commit hash
## What changed
## Verified how          commands and exit codes, not adjectives
## Decisions I made that weren't specified
## Things I was wrong about
## What I did not do and why
## Next
```

Evidence lives in the repo, so a report is never the only record.

---

## Working on screens

Screens come from Claude Design as **screen contracts**, not pixels — see
[SCREEN-CONTRACT.md](docs/SCREEN-CONTRACT.md). Implement from the contract.
If it references a component or state that is not on the showcase, add it to
`packages/ui` first — token, states, showcase entry, screenshot baseline —
and only then build the screen. A screen using something the showcase does
not have is a wrong screen, not a new component.

Screens are data-driven pages over the ADR-0021 payload vocabulary and
PowerSync (ADR-0031): no page fetches, pages render local SQLite and
dispatch `df.*` commands. Step 4 order: conversation, amendments, spec
graph, batches and runs, team, servers, usage, projects.
