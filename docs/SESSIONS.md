# Sessions

One builder, one reviewing architect.

- **The builder** is the Claude Code session running in this repository. It
  reads instructions, writes code, runs the gates, commits, and reports.
- **The reviewing architect** is the Claude chat Rod runs. It sees no
  terminal, no diff, and no screen — only what the builder writes into this
  repository.
- **Instructions arrive from Rod**, who pastes the architect's instruction
  into the builder and carries the builder's report back.

> Rewritten 2026-09-05 when the multi-session phase ended. The ownership
> table that used to be here is gone: there is nobody to hand work to now,
> so work that would once have been handed off is either done or recorded
> under *What I did not do* in a handoff.

---

## The interface is the repository

The chat is a notification; the repository is the record. Every instruction
and every report is written to `docs/handoffs/NNNN-<slug>.md` before it is
summarised in chat, so intent and outcome sit together and neither side
re-derives what was asked. The format is in [CLAUDE.md](../CLAUDE.md).

Architectural decisions go to `docs/adr/`, in the same commit as the change
they justify — never to chat alone. An architect's ruling becomes an ADR
amendment. The brief is the source of truth.

Visual evidence goes under `docs/evidence/<step>/` and is referenced by
path. Rod uploads it to the architect when a visual judgment is needed;
nothing else waits on that.

---

## Rules

These exist because ignoring them has already cost us something. The cost is
recorded next to each one so it does not read as ceremony.

**A risky change gets its own git worktree and its own branch.** Work on
`main` by default now that there is one session; branch when a change is
big enough to want a PR, or risky enough that you want `main` untouched
while it is in progress.

```
git worktree add -b <name> ../darkfactory-<name> main
```

On 2026-09-05 two sessions committing in one working tree produced a commit
titled "3d acceptance demo passes end to end" that contained almost no 3d
work and most of another session's in-progress scaffolding — `git add -A`
cannot tell whose changes are whose. One session makes that specific
collision impossible, but a half-finished refactor sitting in the working
tree during an unrelated commit is the same failure with one author.

**`./scripts/check-stack.sh` must pass before committing to `main`.** It
builds every Compose service from an empty volume, waits for all of them to
report healthy, and exercises the web path a developer takes. It has caught
three breaks that every test suite in the repo was structurally unable to
see — most recently an orphaned standalone server that held the port, so
every assertion answered from the *previous* build with confidence.

**No check counts until it has been shown to fail against planted
breakage.** See [TESTING.md](TESTING.md) — that file is the rule and the
evidence for it. Write down the breakage, break it, confirm red, put it
back.

**The pre-push hook is armed, and a push without it does not count.**

```
git config core.hooksPath scripts/hooks
```

`scripts/hooks/pre-push` scans for credentials and fails closed: if
`scripts/check-secrets.sh` is missing it refuses the push rather than
waving it through. A local commit holding a key is recoverable; a pushed
one is only fixable by rotating the credential. Check the config is set
after any fresh clone or new worktree — git does not ship hooks with a
repository, so a new checkout starts unarmed.

**Never rewrite history that has been pushed** without saying so first. Tag
the old tip if you must.

---

## Where things are

- **Steps 1–3** of the architecture brief are closed. The factory runs
  conversation → spec amendment → plan → implement → verify → ship end to
  end against live models; evidence in [evidence/3d/](evidence/3d/).
- **The design system** is on `main` through step 2: tokens, themes, base
  set, domain components, showcase, tokens-only check, visual suite, and
  the Claude Design export bundle under `packages/ui/export/`.
- **Step 4** is the web UI on `@dark-factory/ui` with PowerSync per
  ADR-0031, built from screen contracts — see
  [SCREEN-CONTRACT.md](SCREEN-CONTRACT.md). It starts when the Claude
  Design prototype for the conversation screen exists.
