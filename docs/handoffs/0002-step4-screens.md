# 0002 — The eight step-4 screens, from the Claude Design prototype

## Instruction (verbatim)

> Use the claude_design MCP (https://api.anthropic.com/v1/design/mcp, auth via /design-login) to import this project:
> https://claude.ai/design/p/3d697d16-03d5-490d-b787-b7778c71831e?file=Dark+Factory.dc.html
>
> Focus on these files (the whole project is readable):
> - `Dark Factory.dc.html`
>
> Also read these files the selection imports:
> - `dashboard/public/design/portrait-sample.svg`
> - `packages/ui/src/tokens.css`
> - `support.js`
>
> Implement: `Dark Factory.dc.html`

Followed, after the MCP route was refused, by:

> i added the html prorotype to the scratch space dark factory.html, please implement it

and

> its in workspace root

## Commit hash

`1323071` — branch `screens-step4`. This report is the commit that follows it.

## What changed

**The prototype could not be reached over MCP.** `DesignSync` refused every
method: design-system authorization is missing and `/design-login` cannot
run in a non-interactive session. `WebFetch` on the project URL returns 403.
The prototype arrived instead as `Dark Factory.html` in the workspace root —
a self-unpacking bundle whose manifest holds React, the `dc-runtime`
(`support.js`), the woff2 faces and `portrait-sample.svg`, with the design
itself in the `__bundler/template` script. It was unpacked in the scratchpad
and read from there. **It is deliberately not committed**: 758 KB of
base64-bundled artifact, and the design is now expressed as eight screen
contracts plus the code.

**Eight screens**, in the ADR-0021 payload vocabulary, under one shell:

| Route | Contract |
|---|---|
| `/conversation` | [docs/screens/conversation.md](../screens/conversation.md) |
| `/amendments` | [docs/screens/amendments.md](../screens/amendments.md) |
| `/specs` | [docs/screens/specs.md](../screens/specs.md) |
| `/batches` | [docs/screens/batches.md](../screens/batches.md) |
| `/team` | [docs/screens/team.md](../screens/team.md) |
| `/servers` | [docs/screens/servers.md](../screens/servers.md) |
| `/usage` | [docs/screens/usage.md](../screens/usage.md) |
| `/projects` | [docs/screens/projects.md](../screens/projects.md) |

**The local-first seam.** `dashboard/lib/local/` is the shape ADR-0031 will
sit behind: `schema.ts` names the synced tables and only the columns a
screen renders, `store.tsx` exposes `useQuery(select)` and
`dispatch(df.*)`, `fixtures.ts` holds the contents. No screen fetches.
Wiring PowerSync is a change inside `store.tsx` — `useQuery` becomes a SQL
query, `dispatch` becomes a backend-connector call — and no screen moves.

**Two component changes in `packages/ui`, made before the screens used
them**, each with a showcase frame, a manifest entry and a refreshed
baseline:

- `PersonaCard` gained `onTeam`. `installed` and `on this team` are
  different facts — a persona can be bought for the org and used by nobody —
  and collapsing them makes "why is this not running my work" unanswerable
  from the card.
- `ServerCard` no longer reports a health for an unauthorized built-in
  connector. It rendered `conformant` for something nothing had ever
  contacted, which asserts a conformance check that never ran.

**Removed:** `dashboard/app/projects`, `dashboard/app/runs`,
`dashboard/lib/factory-api.ts`. These were server components calling
`fetch`, which the CLAUDE.md screen rule forbids; the prototype's Projects
and Batches screens supersede them.

**The showcase is no longer framed by the product shell.** The root layout
now carries only fonts and theme; the eight screens share
`app/(factory)/layout.tsx`. `/design` has its own complete sticky header, so
it was gaining a second one — it now carries its own link back instead.

## Verified how

| Command | Result |
|---|---|
| `pnpm check` | exit 0 — tokens-only 75 files no raw colours; 162 token pairs pass contrast both themes; export bundle current; lint clean; typecheck clean |
| `pnpm test:visual` | exit 0 — 14 passed |
| `dotnet test DarkFactory.sln` | exit 0 — 194 passed, 2 skipped (live-model integration) |
| `./scripts/check-stack.sh` | exit 0 — all six services healthy from an empty volume; web path passes |

**The visual baselines were shown to fail before they were updated.** All
ten screenshot tests went red on the first run. Rather than assume my
changes were the cause, I stashed the whole branch and re-ran: 14/14 passed
clean. The cause was real and mine — removing the header from the root
layout shifts every section of `/design` — so the baselines were updated
against a change I had confirmed, not against an unexplained diff.

Every screen and state was also rendered in a real browser against the
production build and inspected, with console and page errors collected: no
page errors. That pass is what turned up the seven defects below.

## Decisions I made that weren't specified

- **A typed local seam rather than real PowerSync**, chosen with the user.
  ADR-0031 is accepted but has no dependency, no code and no sync service in
  the stack, and inventing one is far outside "implement this prototype".
- **Scenario state lives in the URL** (`?state=parked`). Every state on
  every screen is a link somebody can open, screenshot or file a bug
  against, which is the difference between a state that is claimed and a
  state that is shown. The state strip at the bottom is visible scaffolding
  and comes out with the fixtures.
- **The prototype's own inconsistency was not reproduced.** Its amendments
  screen shows the ledger amendment as awaiting; its batches screen counts
  it in a two-item backlog. Counts here are computed from one set of rows,
  so the backlog holds one. The screens agree with each other instead of
  with the mockup.
- **Seven defects found by looking at the rendered screens**, all fixed:
  the conversation opened at the top rather than the newest turn (which hid
  the parked and syncing states entirely); the amendment detail printed its
  diff summary twice; the spec browse list expanded every node's full
  revision history alongside the detail pane that already showed it; the
  usage screen turned "+9pp" into "+9.0%" — percentage points are not
  percent; an empty org still showed a project name, a live batch ticker and
  step counts; plus the two component fixes above.
- **`CostBar`'s full-width bar for a member with no cap was left alone.** It
  looks like "at capacity" at a glance, but the component documents the
  choice — without a budget the bar is a breakdown and the width is the
  total — and changing it is a design-system decision, not a screen one.

## Things I was wrong about

- I first typed the layouts with Next's generated `LayoutProps`, copying the
  existing root layout. That type only exists after `next build` writes it,
  so `typecheck` failed from a clean tree. Both layouts now type `children`
  explicitly.
- I hardcoded the backlog count in the amendments empty state, having just
  said counts should be computed. The real fix was upstream: "nothing
  awaiting you" means nothing *proposed*, so the fixture keeps the approved
  and rejected rows and the count computes.
- I ran a screenshot pass against a server that was still serving the
  previous build. `scripts/serve-standalone.mjs` refuses a busy port and
  says exactly why, which is the only reason it was caught.

## What I did not do and why

- **No PowerSync.** Decided with the user; see above. The seam is shaped for
  it and `store.tsx` carries the note.
- **No `df.*` command actually reaches the factory.** `dispatch` applies the
  optimistic local effect and stops. Approve and reject are wired end to end
  in the UI because they have a visible local consequence; the rest are real
  call sites with no server behind them yet. Needs assigning alongside the
  PowerSync backend connector.
- **No visual baselines for the eight screens.** `playwright.config.ts`
  points `testDir` at `packages/ui/tests` and covers the showcase only.
  Screen-level screenshot tests are a worthwhile addition — every state is
  already a URL, which is most of the work — but adding a second test
  project is a change to the shared visual-test setup and belongs in its own
  commit. Needs assigning.
- **The prototype's drag-to-reorder backlog is not draggable.** The rows
  carry `draggable` and the affordance, but `df.batches.reorder` has no
  handler. Reordering without persistence is a lie about what happened.
- **`Dark Factory.html` is left untracked in the workspace root.** It is the
  source artifact, not a deliverable; it should be deleted or moved out once
  this is reviewed.

## Next

1. Wire the PowerSync backend connector behind `store.tsx` and delete
   `fixtures.ts` and the state strip.
2. Add a Playwright project for the eight screens, one capture per state URL.
3. Implement `df.batches.reorder` and make the backlog genuinely draggable.
