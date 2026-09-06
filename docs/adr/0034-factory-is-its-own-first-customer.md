# ADR-0034: The factory is its own first customer (north star)

## Status
Accepted — **the north star**. Sequenced after the first push; step (1) can
begin once the conversation surface is usable.

## Context
Every claim in [the brief](../architecture-brief.md) — that a conversation
can maintain a specification graph, that approved amendments can be executed
in ordered batches, that standards can be applied at generation time — is
currently believed rather than known. The product has been built and
demonstrated against a scratch project ([3d evidence](../evidence/3d/)), not
against a codebase anyone cares about.

A demo project cannot find the failures that matter. It has no history, no
accumulated decisions to contradict, no standards worth enforcing, and
nobody who minds if the output is mediocre. The failures that decide whether
this is a product — a spec graph that becomes unreadable at a few hundred
nodes, an approval flow that is tolerable once and infuriating weekly, a
conversation that cannot find the decision it needs — only appear under real
use, by someone who has to live with the result.

We have a codebase that qualifies, and we are already its most demanding
user.

## Decision

**Dark Factory builds Dark Factory.** The repository is registered as a
customer project of a Dark Factory instance and real work is routed through
the product.

**The repo ships a specialized `df` project server** — the reference server
configured for this stack: `dotnet test`, `pnpm test` and
`scripts/check-stack.sh` as verify, with `df.describe` reporting C#,
Next.js, Postgres and Compose — plus a `.dark-factory/workspace.json`.

**A second factory instance does the work; never the one being edited.** A
`staging` Compose profile runs a second factory with its own database,
pointed at the real checkout. **The running factory never modifies itself.**

**The sequence is three stages, in order:**

1. **The staging factory maintains the repo's own specifications.** The
   brief and the ADRs are imported as the first spec graph.
2. **Additive, mechanically verifiable work** — test harnesses, docs,
   design-system showcase states. Work whose success a gate can judge
   without anyone's opinion.
3. **The engine itself**, gated on the candidate build passing the committed
   `docs/evidence` suites before it replaces the stable one.

**The repo's standards become the first first-party standards server.** The
conventions in `docs/conventions/`, the rules in
[TESTING.md](../TESTING.md) and [DESIGN.md](../../packages/ui/DESIGN.md) are
already the shape of a standards server's content.

## Consequences

- **Anything that feels awkward doing to ourselves is a product bug, found
  for free.** This is the point of the ADR. A friction a customer would
  quietly tolerate — or quietly leave over — is one we cannot ignore,
  because we hit it on a Tuesday trying to get something else done.
- The staging factory writing to the real checkout makes the isolation rule
  load-bearing rather than stylistic: an engine that can edit the engine
  that is running it has no stable ground to stand on during a failure.
  Stage 3's promotion gate is the same reasoning one level up.
- Stage ordering is a risk ramp, not bureaucracy. Stage 2 is chosen because
  a mechanical gate decides whether the factory did the job, so the question
  "is the factory good enough yet" has an answer that is not a matter of
  taste.
- The evidence suites under `docs/evidence/` acquire a second role: they were
  a record of what happened, and they become the promotion gate for engine
  changes. That raises the bar on what counts as evidence — a suite that
  merely records the last run cannot decide whether the next build is
  better.
- Importing the brief and the ADRs as the first spec graph is a real test of
  [ADR-0016](0016-spec-graph-content-addressed-append-only.md)'s grain
  question: these documents are exactly the mix of fine-grained rules and
  broad decisions that the open question about node grain is about.
- Our tolerance for our own output becomes the product's quality floor. That
  is the intended pressure.

## Owner
Backend session, after the first push.
