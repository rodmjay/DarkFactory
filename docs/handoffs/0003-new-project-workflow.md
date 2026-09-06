# 0003 — The new-project workflow, wired to the real factory

## Instruction (verbatim)

> looks good! lets keep building out the features so its fully funcitonal

then, mid-turn:

> lets start with new project workflow

## Commit hash

`5e244b1` — branch `screens-step4`. This report is the commit that follows it.

## What changed

**The factory has no REST API worth the name.** `/health`, signed artifact
URLs and a webhook are the whole of it; the product surface is 25 `df.*`
MCP tools plus a SignalR hub. So "wire the dashboard to the factory" means
"call MCP", and `dashboard/lib/factory/mcp.ts` is the one place that knows
how.

It speaks streamable HTTP in its **stateless** form: the factory returns no
`mcp-session-id`, so a tool call is one POST with no session to establish
or keep alive. That is written down in the file, because the stateful form
is much more work and a factory that starts issuing session ids should
break loudly here rather than quietly everywhere.

**The seam held.** `useQuery(select)` still reads a synchronous mirror;
`LocalDbProvider` now hydrates the `projects` slice from
`df.projects.list` on mount. No screen changed shape to accommodate real
data, which is what the seam was for.

**The new-project workflow is real end to end.** "Register a project" opens
a dialog that asks one question — the workspace server's MCP URL —
dispatches `df.projects.register`, and re-reads the roster. Everything else
is the factory's answer: it runs `df.describe` against the URL, exercises
every declared capability, derives the name and seeds the default team.
A form that asked for those would be asking the user to guess at facts the
factory is about to establish.

**Honesty about mixed data.** Seven screens still render fixtures. Rather
than let a populated table imply otherwise, the header carries a chip —
`Live`, `Reading…`, `Factory unreachable`, or `Prototype data` — and the
fixture ticker goes quiet on a wired screen. `WIRED_SCREENS` in
`store.tsx` is the list; it is in code rather than in a document that would
drift from it.

**Unknown is not zero.** `df.projects.list` does not carry runs, amendments,
deployable batches or spend, so `ProjectRow` types those as `number | null`
and they render as a hoverable "—". Making the type nullable is what found
the five call sites that would otherwise have printed a confident `0`.

**Two defects fixed on the way:**

- `StageTimeline` keyed its rows by stage name, so a run that retried a
  stage rendered one `implement` instead of two. React said so in the
  console; the fixture with an over-budget attempt is what surfaced it.
- `/` was a page that called `redirect()`. A redirecting *page* is still a
  rendered route: Next emits a 307 whose body is an error stub referencing
  that route's own CSS chunk — a chunk which, because the page renders
  nothing, was never emitted. It is now a routing-layer redirect in
  `next.config.ts`, with no body at all.

**One check changed.** `check-stack.sh` fetched `/` without following
redirects and scraped the redirect stub for a stylesheet. That worked by
luck while the stub happened to name a chunk that existed. It now uses
`curl -fsSL`, because the step is called "the path a developer takes" and a
developer's browser follows redirects.

## Verified how

| Command | Result |
|---|---|
| `pnpm check` | exit 0 — 76 files no raw colours; 162 token pairs pass contrast; export bundle current; lint clean; typecheck clean |
| `pnpm test:visual` | exit 0 — 14 passed |
| `dotnet test DarkFactory.sln` | exit 0 — 194 passed, 2 skipped |
| `DASHBOARD_PORT=3400 ./scripts/check-stack.sh` | exit 0 — six services healthy from an empty volume; `assets resolve (64977 bytes)` |

**The changed check was shown to fail against planted breakage.** Commenting
out the `cpSync` in `serve-standalone.mjs` reproduces the historical failure
the check exists for — a standalone server with no `.next/static`. With
`-L` in place the check still goes red: `status=404 bytes=9`. Restored, it
returns `status=200 bytes=64977`. A check that only ever passed after being
edited has not been tested.

**The workflow was driven through the browser, not asserted.** Registering
the reference server returns the existing project unchanged (the factory is
idempotent on workspace URL); registering a second URL created a genuinely
new project with its own seeded team, and the roster went to two. The
failure paths were exercised too: a malformed URL is refused locally without
a 30-second handshake, and a real-but-non-conformant endpoint surfaces the
factory's own words — *"df.describe is mandatory (docs/adr/0018); a server
that cannot answer it is not registered."*

## Decisions I made that weren't specified

- **Server actions, not route handlers.** The factory's endpoint is an
  internal Compose address. `lib/factory/mcp.ts` imports `server-only` so a
  mistaken client import fails the build rather than shipping that address
  to a browser.
- **Failures are values, not exceptions.** Nothing in `actions.ts` throws:
  an unreachable workspace server is something the user can act on, so it
  renders in the dialog instead of replacing the page with an error
  boundary.
- **The MCP wrapper is stripped from error text.** The factory's sentence is
  the useful half; `An error occurred invoking 'df.projects.register'` in
  front of it is machinery the reader did not ask about.
- **The current project stays the fixture project.** Putting a real
  project's name above fixture counts in the header is the one thing the
  seam exists to prevent.
- **A domain failure is not a transport failure.** MCP reports the former as
  a result with `isError`; collapsing them would turn "that URL is not a
  conformant workspace server" into "the factory is down".

## Things I was wrong about

- I read a 500 from `check-stack` and first blamed an unrelated container
  (`pro2-shell`) squatting host port 3000. Re-running on a free port
  reproduced the failure, so the squatter was a real but separate problem
  and the regression was mine.
- I then chased a 404 on a stylesheet that was actually a stale orphaned
  server on port 14999 answering for a previous build — the exact scenario
  `serve-standalone.mjs` documents and refuses. It was doing its job; I was
  misreading its output.
- My first hydration effect set state synchronously, which
  `react-hooks/set-state-in-effect` rejected. The fix was to put the update
  in a promise callback — and `live` already starts at `hydrating`, so
  there was nothing to set on the way in.

## What I did not do and why

- **Only `projects` is wired.** Conversation, amendments, spec graph,
  batches, team, servers and usage still render fixtures. The tools exist
  for most of them — `df.specs.*`, `df.conversations.*`, `df.servers.list`,
  `df.work.*` — so these are follow-on work, not blocked work.
- **No SignalR.** The factory publishes run events on `/hubs/runs`; the
  batches screen's live activity list is still fixtures. Needs assigning
  with the batches screen.
- **No PowerSync.** Unchanged from 0002: reads go through a server action
  rather than a local SQLite mirror. The call sites are the shape PowerSync
  needs, and `store.tsx` is still the only file that has to change.
- **`df.projects.register` is idempotent on URL, and the UI does not say
  so.** Registering an already-registered workspace silently returns the
  existing project, which reads as success on a no-op. Worth a distinct
  message once the tool tells us which happened.
- **A note for whoever runs this stack:** an unrelated container on this
  machine holds host port 3000, which is `check-stack`'s default dashboard
  port. `DASHBOARD_PORT=3400` works around it. The check does not detect a
  foreign listener the way `serve-standalone.mjs` does — that asymmetry is
  worth closing.

## Next

1. Wire the servers screen to `df.servers.list` — the next-smallest real
   slice, and it makes the connector story honest.
2. Wire the spec graph to `df.specs.query` / `df.specs.get` /
   `df.specs.neighborhood`.
3. Subscribe the batches screen to `/hubs/runs` for live run events.
