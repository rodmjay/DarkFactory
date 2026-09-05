# Testing

**No check counts until it has been shown to fail against planted breakage.**

That is the rule. The rest of this file is why, and it is short because the
rule is the point.

---

## Why

A check that has only ever passed has not been tested — the code has. The two
are easy to confuse, and the difference only shows up when something breaks
and the check stays green.

On 2026-09-05 three checks were each proposed as sufficient by whoever
proposed them, and each was proved insufficient by running it against
something actually broken. Every one was found because the previous one went
green:

| assertion | what it missed |
|---|---|
| `GET /api/health` returns 200 | passed against a genuinely broken `pnpm start` — `next start` served the ordinary build sitting beside the standalone one. Wrong build, right status. |
| the server log carries no unsupported-configuration warning | caught that one; the signal was the log, not the response |
| a stylesheet extracted from the served markup resolves | catches the next shape — 200, clean log, blank white page with no CSS |

The same mistake had two other shapes the same day:

- A tokens-only lint rule and a visual regression suite were both trusted only
  after being made to fail — the lint rule against a planted violation of each
  kind it claims to catch, the visual suite against a shifted accent hue.
- That visual suite's first threshold was `maxDiffPixelRatio`, and a *ratio*
  tolerance is weakest exactly where there is most content: an accent change
  failed three captures and slipped past a 22,000-pixel section entirely. It
  is `maxDiffPixels` now, absolute.

And one that is worth separating, because no assertion was wrong — the suite
simply could not reach it. 189 tests passed while the factory would not boot:
a hand-built `JsonSerializerOptions` had no `TypeInfoResolver`, which throws
only when the MCP SDK marks the options read-only during startup. Nothing in
the suite starts the host. `scripts/check-stack.sh` caught it.

**When something is broken and the suite is green, ask what the suite
structurally cannot reach before adding another test of what it already
covers.**

---

## How to apply it

When you add a check:

1. Write down the specific breakage it is supposed to catch.
2. Break that thing.
3. Confirm the check goes red.
4. Put it back.

If it stays green, the assertion is on the wrong signal — not on a signal that
needs tightening. Tightening a threshold on the wrong signal produces a check
that is both strict and blind.

This applies to lint rules, screenshot baselines, smoke checks, health probes
and integration tests alike. It costs a couple of minutes and it is the only
thing that distinguishes a gate from a decoration.

Two smaller habits from the same day, worth the same discipline:

- **Measure before designing around a cost.** A flag was proposed to keep a
  Next build out of the pre-commit check; the build turned out to be 5.4
  seconds, noise against the Docker build already in it. Designing around an
  unmeasured cost is how a check ends up with three modes and no users.
- **Cover every path to the same artifact.** `check-stack.sh` covered the path
  the container takes and not the path a developer takes, so a broken
  `pnpm start` sat on `main` behind a green check. Two gates, each covering
  its author's path, recreates the problem one level up.

---

## The gates

| Command | Covers |
|---|---|
| `dotnet test DarkFactory.sln` | the .NET solution |
| `./scripts/check-stack.sh` | every Compose service healthy from an empty volume, plus the web path a developer takes |
| `./scripts/demo-3d.sh` | the 3d acceptance condition end to end against live models, writing evidence to `docs/evidence/3d/` |
| `pnpm check` | tokens-only, contrast, lint, typecheck |
| `pnpm test:visual` | the design-system showcase, per section and full page, in both themes |

`check-stack.sh` must pass before committing to `main`.
