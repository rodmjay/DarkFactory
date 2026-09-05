# Sessions

Several Claude sessions work on this repository at once. This is who owns
what, and the rules that exist because ignoring them has already cost us
something.

> Created 2026-09-05 because it was referenced and did not exist. The
> ownership table below is written from what the sessions have actually been
> doing; correct it rather than working around it.

---

## Rules

**Every workstream gets its own git worktree and its own branch.** Not the
shared checkout. On 2026-09-05 two sessions committing in one working tree
produced a commit titled "3d acceptance demo passes end to end" that
contained almost no 3d work and most of another session's in-progress
scaffolding — `git add -A` cannot tell whose changes are whose.

```
git worktree add -b <name> ../darkfactory-<name> main
```

**`./scripts/check-stack.sh` must pass before committing to `main`.** It
builds every Compose service from an empty volume, waits for all of them to
report healthy, and exercises the web path a developer takes. It has caught
two breaks that every test suite in the repo was structurally unable to see.

**No check counts until it has been shown to fail against planted breakage.**
See [docs/TESTING.md](docs/TESTING.md) — that file is the rule and the
evidence for it.

**Cross-cutting work is handed off by message, not fixed in place.** If a
change belongs to another session's area, say so and let them land it. The
exception is a regression you caused: own it, fix it on a branch off `main`,
and hand the branch over.

**Never rewrite history another session may be sitting on** without saying so
first. Tag the old tip if you must.

---

## Ownership

| Area | Session | Worktree |
|---|---|---|
| The factory — engine, spec graph, MCP surface, gateway, migrations, Compose, `scripts/` | `darkfactory-14` | `/home/rodmjay/dev/darkfactory` (`main`) |
| The design system — `packages/ui`, `dashboard/`, tokens, showcase, visual tests | `darkfactory-1e` | `/home/rodmjay/dev/darkfactory-design-system` (`design-system`) |

The boundary is areas of the tree, not subject matter. The design-system
session owns the pnpm and workspace consequences wherever they land —
including `dashboard/Dockerfile` and the dashboard's part of the Compose
build — because that is where the decisions that cause them were made.

---

## Where things are

- **Steps 1–3** of the architecture brief are closed. The factory runs
  conversation → spec amendment → plan → implement → verify → ship end to end
  against live models; evidence in [docs/evidence/3d/](docs/evidence/3d/).
- **Design system** step 1 (tokens, themes, base set, showcase) is on `main`;
  step 2 (domain components, visual suite, tokens-only check) is on the
  `design-system` branch awaiting review; step 3 is the export bundle for
  Claude Design.
- **Step 4** is the web UI on `@dark-factory/ui` with PowerSync per ADR-0031.

---

## Open, and belonging to the factory session

**Rationale is collected on every amendment and never stored.**
`specdiff.schema.json` carries `rationale` on creates, revises, retires,
`edge_adds` and `edge_retires`; the model is asked why and answers. The
internal `SpecDiff` (`src/DarkFactory.Data/SpecDiff.cs`) has no rationale
field on any element, so it is dropped when the wire document is translated —
*before* the amendment is persisted, not at approval. `amendments.diff_json`
therefore never holds it, and `df.specs.approve` renders it back as null
forever.

It survives in `turns.payloads`, which stores the original document, so it is
recoverable by joining `amendments.turn_id → turns` — but not from the
amendment, which is where every surface that shows a diff reads.

This is a provenance gap, not a UI one. ADR-0016 requires an amendment to
carry who and when; what is being lost is *why*, per change, which is the half
a reviewer actually needs.
