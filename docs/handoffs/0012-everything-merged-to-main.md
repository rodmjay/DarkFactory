# 0012 — Everything merged to main, and a check-stack leak fixed

## Instruction (verbatim)
> check in all dark factory to github main branch

## Commit hash
Merges `d8ab1dd` (drones-intake), `51d3e2b` (screens-step4), `72fba9a` (spec-strategies); the check-stack fix and this report in the commit after them.

## What changed
- **Three branches merged into main:** `drones-intake` (step-4 screens through guided spec building), `screens-step4`, and `spec-strategies` (another session's ADR-0039/0040, docs only).
- **Leftovers committed first** on `screens-step4`, as found in the main checkout: the brief-v2 handoff text, a multi-domain `describe.schema.json` (optional `domains`), and the Claude Design bundle `Dark Factory.html`.
- **Renumbered mine:** ADR-0039 → 0041, handoff 0010 → 0011, since `spec-strategies` took those numbers first.
- **check-stack leaked a server on every run.** `kill "$pid"` stopped pnpm, not `next-server`, and the asset check ran after that kill, so it passed only because the orphan kept answering. Yesterday's orphan held port 14999 and made today's run fail with a 500. Now: the server runs in its own process group, is stopped on every path after the asset check, and the script refuses to start on a busy port.

## Verified how
- On the merged tree in a clean worktree: `dotnet test DarkFactory.sln` → 268 passed, 2 skipped · `pnpm check` → 0 · `pnpm test:visual` → 14/14 · `./scripts/check-stack.sh` → 0, and port 14999 free afterwards.
- Planted: `WEB_SMOKE_PORT=13000` (the live dashboard) → FAIL "port 13000 is already serving something".
- Conflicts: `0002` took main's text (a superset). `describe.schema.json` took screens-step4's version plus `corpus`, the only change drones-intake made to the file; a script confirmed no drones-intake key or domain value was lost.

## Decisions I made that weren't specified
- Merged `spec-strategies`, which is another session's work, because "all" was asked. Docs only, and it merged clean.
- Committed the multi-domain schema and the HTML bundle as found, each in its own labelled commit.

## Things I was wrong about
- I blamed the first check-stack failure on my own parallel gates. Re-running alone failed identically; it was the orphan.

## What I did not do and why
- No code reads `domains` yet. The schema accepts it; nothing uses it.
- The two `0002-*` handoffs predate this; not renumbered.
- Branches and worktrees kept. Deleting them is Rod's call.
- Not deployed. The live stack still runs `drones-intake`, which is identical in code.

## Next
Deploy (commands in 0011), then walk the guide on `/conversation`.
